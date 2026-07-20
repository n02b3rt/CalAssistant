using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalAssistant.Models;
using MaIN.Core.Hub;
using MaIN.Core.Hub.Utils;
using MaIN.Domain.Configuration.BackendInferenceParams;
using MaIN.Domain.Entities;
using MaIN.Domain.Entities.Tools;

namespace CalAssistant.Services;

/// <summary>
/// Conversation orchestrator. A local Qwen3 model handles native tool calling via Ollama.
/// The model decides when to invoke a tool (list_day / check_availability / create_event);
/// MaIN.NET runs the tool loop. Registered as Scoped — one state per Blazor circuit.
///
/// Key behaviours:
///  - Full conversation history is sent every turn, so multi-turn slot-filling works
///    ("schedule a meeting" → "what time?" → "3pm" → created).
///  - The model must NOT invent event details; missing info triggers a follow-up question.
///  - create_event checks the calendar for conflicts first (C#-side, deterministic).
/// </summary>
public class AssistantService : IDisposable
{
    /// <summary>Fallback model when Assistant:Model config is absent.</summary>
    public const string DefaultModel = "qwen3:1.7b";

    /// <summary>Upper bound on a single turn — protects the UI from a runaway generation.</summary>
    private const int ResponseTimeoutSeconds = 60;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex ThinkTag = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly IMaINHub _hub;
    private readonly CalendarService _calendar;
    private readonly Localizer _loc;
    private readonly ILogger<AssistantService> _log;
    private readonly string _modelName;
    private readonly ChatMessage _greeting;

    // Cap history sent to the model (keeps 1.7b within its context window).
    private const int MaxHistoryMessages = 14;

    public List<ChatMessage> Messages { get; } = new();
    public bool Busy { get; private set; }

    /// <summary>Localization key describing what the assistant is doing right now (live status feedback).</summary>
    public string StatusKey { get; private set; } = "status.thinking";

    public event Action? Changed;
    private void Raise() => Changed?.Invoke();

    public AssistantService(IMaINHub hub, CalendarService calendar, Localizer loc, IConfiguration cfg, ILogger<AssistantService> log)
    {
        _hub = hub;
        _calendar = calendar;
        _loc = loc;
        _log = log;
        _modelName = cfg["Assistant:Model"] ?? DefaultModel;

        _greeting = new ChatMessage { Role = ChatRole.Assistant, Text = _loc["assistant.greeting"] };
        Messages.Add(_greeting);

        _loc.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        // Re-localize the greeting in place so the first bubble follows the language toggle.
        _greeting.Text = _loc["assistant.greeting"];
        Raise();
    }

    public void Dispose() => _loc.Changed -= OnLanguageChanged;

    /// <summary>The model actually in use (for display in the UI).</summary>
    public string ModelName => _modelName;

    public bool CalendarConfigured => _calendar.IsConfigured;
    public bool CalendarConnected => _calendar.IsConnected;

    public async Task ConnectCalendarAsync() => await _calendar.ConnectAsync();

    /// <summary>Main entry: processes a user message and appends the assistant reply.</summary>
    public async Task<ChatMessage> SendAsync(string userText)
    {
        Messages.Add(new ChatMessage { Role = ChatRole.User, Text = userText });
        Busy = true;
        StatusKey = "status.thinking";
        Raise();

        ChatMessage reply;
        try
        {
            reply = await RunWithToolsAsync();
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("Turn timed out after {Sec}s", ResponseTimeoutSeconds);
            reply = new ChatMessage { Role = ChatRole.Assistant, Text = _loc["err.timeout"] };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to process message");
            reply = new ChatMessage { Role = ChatRole.Assistant, Text = _loc["err.generic"] };
        }
        finally
        {
            Busy = false;
        }

        Messages.Add(reply);
        Raise();
        return reply;
    }

    private async Task<ChatMessage> RunWithToolsAsync()
    {
        var now = DateTime.Now;
        DayPlan? dayPlanResult = null;
        CalEvent? createdEventResult = null;

        var language = _loc.LanguageName;
        var system = BuildSystemPrompt(now, language);
        var tools = BuildTools(
            onDayPlan: p => dayPlanResult = p,
            onCreated: e => createdEventResult = e);

        // Hard safety timeout so a runaway generation can never hang the UI.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ResponseTimeoutSeconds));

        var result = await _hub.Chat()
            .WithModel(_modelName)
            .WithMessages(BuildConversation())
            .WithSystemPrompt(system)
            .WithInferenceParams(new OllamaInferenceParams
            {
                Temperature = 0.15f,
                // Cap generation so a small model can't run away (bounds latency and stops repetition loops).
                MaxTokens = 500,
                // Disable Qwen3 "thinking" — huge latency win, and we don't surface reasoning anyway.
                AdditionalParams = new Dictionary<string, object> { ["think"] = false }
            })
            .WithTools(tools)
            .DisableCache()
            .CompleteAsync(cancellationToken: cts.Token, toolCallback: OnToolInvoked);

        var text = CleanReply(result.Message.Content);
        // For created events, ALWAYS use a deterministic confirmation — small models often
        // misstate the date/time in prose ("21 maja" instead of "21 lipca"). Correctness > flavour.
        if (createdEventResult is not null)
            text = FallbackText(createdEventResult, null, language);
        else if (string.IsNullOrWhiteSpace(text))
            text = FallbackText(null, dayPlanResult, language);

        return new ChatMessage
        {
            Role = ChatRole.Assistant,
            Text = text,
            Day = dayPlanResult,
            CreatedEvent = createdEventResult
        };
    }

    private static string BuildSystemPrompt(DateTime now, string language)
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        return
            "You are a helpful, friendly personal calendar assistant.\n" +
            $"Current date and time: {now:yyyy-MM-dd HH:mm} ({en.DateTimeFormat.GetDayName(now.DayOfWeek)}). Timezone: the user's local time.\n\n" +
            $"LANGUAGE — CRITICAL: The user is writing in {language}. You MUST write your entire reply in {language}. Do not use any other language.\n\n" +
            "TOOLS:\n" +
            "- list_day(date): show the user's schedule for a given day.\n" +
            "- check_availability(start, end): check whether a time range is free.\n" +
            "- create_event(title, start, end?, location?, reminder_minutes?, force?): create an event or reminder.\n\n" +
            "RULES — follow strictly:\n" +
            "0. For ANY question about the user's schedule, plan, events, or what they have to do on a given day " +
            "(today / tomorrow / 'dzisiaj' / 'jutro' / a specific date), you MUST call the list_day tool with that date. " +
            "Never answer such questions from memory or from earlier messages — always call list_day.\n" +
            "1. NEVER invent or assume event details. To create an event you need at least: a TITLE, a DATE, and a START TIME. " +
            "If any of these is missing or unclear, ASK ONE short follow-up question and STOP — do not call create_event with guessed values.\n" +
            "2. When scheduling, you don't need to call check_availability yourself — create_event verifies conflicts. " +
            "If it returns a CONFLICT, in ONE short sentence tell the user what clashes, then ask whether to book anyway, pick another time, or cancel. " +
            "Only call create_event with force=true after the user explicitly confirms they want to double-book.\n" +
            "3. Resolve relative dates/times ('today', 'tomorrow', 'jutro', 'at 3pm', 'o 15') to absolute values using the current date/time above.\n" +
            "4. If the user gives no duration, assume 60 minutes.\n" +
            "5. After a tool runs, reply briefly and naturally. Do NOT list events as bullet points — the UI renders a visual timeline.\n" +
            "6. Be concise and warm. Confirm what you did in one or two sentences.";
    }

    /// <summary>
    /// Live status feedback: MaIN calls this when the model invokes a tool. We map the tool to a
    /// plain-language status so the user sees what's actually happening (not just a spinner).
    /// </summary>
    private Task OnToolInvoked(ToolInvocation inv)
    {
        StatusKey = inv.ToolName switch
        {
            "list_day"           => "status.reading",
            "check_availability" => "status.checking",
            "create_event"       => "status.creating",
            _                    => "status.writing"
        };
        Raise();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Maps recent chat history to MaIN messages. Drops the UI-only greeting and any leading
    /// assistant turns — the conversation sent to the model MUST start with a user message,
    /// otherwise small models (qwen3:1.7b) stop emitting tool calls reliably.
    /// </summary>
    private List<Message> BuildConversation()
    {
        var relevant = Messages
            .SkipWhile(m => m.Role != ChatRole.User)          // drop greeting / leading assistant turns
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .ToList();

        if (relevant.Count > MaxHistoryMessages)
        {
            relevant = relevant.GetRange(relevant.Count - MaxHistoryMessages, MaxHistoryMessages);
            // After trimming, make sure we still start on a user turn.
            var start = relevant.FindIndex(m => m.Role == ChatRole.User);
            if (start > 0) relevant = relevant.GetRange(start, relevant.Count - start);
        }

        return relevant.Select(m => new Message
        {
            Role = m.Role == ChatRole.User ? "User" : "Assistant",
            Content = m.Text,
            Type = MessageType.NotSet,
            Time = m.Timestamp
        }).ToList();
    }

    private ToolsConfiguration BuildTools(Action<DayPlan> onDayPlan, Action<CalEvent> onCreated) =>
        new ToolsConfigurationBuilder()
            .AddTool(
                name: "list_day",
                description: "Fetches Google Calendar events for a given day. Call when the user asks about their schedule or plan.",
                parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        date = new { type = "string", description = "Date in YYYY-MM-DD format" }
                    },
                    required = new[] { "date" }
                },
                execute: async (string argsJson) =>
                {
                    if (!_calendar.IsConnected) return "ERROR: calendar is not connected.";

                    var args = JsonSerializer.Deserialize<DateArgs>(argsJson, JsonOpts);
                    if (!DateTime.TryParse(args?.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        date = DateTime.Today;

                    var plan = await _calendar.GetDayAsync(date);
                    onDayPlan(plan);

                    if (plan.Count == 0) return $"No events on {date:yyyy-MM-dd}. The day is free.";
                    var lines = plan.Events.Select(e =>
                        $"{e.TimeLabel}: {e.Title}{(e.Location is not null ? $" ({e.Location})" : "")}");
                    return $"{plan.Count} event(s) on {date:yyyy-MM-dd}:\n" + string.Join("\n", lines);
                })
            .AddTool(
                name: "check_availability",
                description: "Checks whether a time range is free of other events. Use when the user asks if they have time, or to verify a slot.",
                parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        start = new { type = "string", description = "Start date/time, ISO 8601: YYYY-MM-DDTHH:mm" },
                        end = new { type = "string", description = "End date/time, ISO 8601: YYYY-MM-DDTHH:mm" }
                    },
                    required = new[] { "start", "end" }
                },
                execute: async (string argsJson) =>
                {
                    if (!_calendar.IsConnected) return "ERROR: calendar is not connected.";

                    var args = JsonSerializer.Deserialize<RangeArgs>(argsJson, JsonOpts);
                    if (!DateTime.TryParse(args?.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                        return "ERROR: invalid start.";
                    if (!DateTime.TryParse(args?.End, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                        end = start.AddHours(1);

                    var conflicts = await _calendar.GetConflictsAsync(start, end);
                    if (conflicts.Count == 0)
                        return $"FREE: nothing scheduled between {start:yyyy-MM-dd HH:mm} and {end:HH:mm}.";
                    return "BUSY: overlaps with " + string.Join("; ", conflicts.Select(c => $"{c.TimeLabel} {c.Title}"));
                })
            .AddTool(
                name: "create_event",
                description: "Creates an event or reminder in Google Calendar. Verifies the slot is free first (unless force=true).",
                parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Event title" },
                        start = new { type = "string", description = "Start date/time, ISO 8601: YYYY-MM-DDTHH:mm" },
                        end = new { type = "string", description = "End date/time, ISO 8601. Defaults to one hour after start." },
                        location = new { type = "string", description = "Location (optional)" },
                        reminder_minutes = new { type = "integer", description = "Popup reminder X minutes before (optional)" },
                        force = new { type = "boolean", description = "Set true ONLY after the user confirms they want to book despite a conflict." }
                    },
                    required = new[] { "title", "start" }
                },
                execute: async (string argsJson) =>
                {
                    if (!_calendar.IsConnected) return "ERROR: calendar is not connected.";

                    var args = JsonSerializer.Deserialize<CreateEventArgs>(argsJson, JsonOpts);
                    if (args is null || string.IsNullOrWhiteSpace(args.Title) || string.IsNullOrWhiteSpace(args.Start))
                        return "ERROR: missing required fields. Ask the user for the title, date and start time — do not guess.";

                    if (!DateTime.TryParse(args.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                        return $"ERROR: invalid start '{args.Start}'. Ask the user to clarify the date/time.";

                    DateTime end;
                    if (!string.IsNullOrWhiteSpace(args.End) &&
                        DateTime.TryParse(args.End, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
                        end = parsedEnd;
                    else
                        end = start.AddHours(1);
                    if (end <= start) end = start.AddHours(1);

                    // Conflict guard — don't double-book unless explicitly forced.
                    if (!args.Force)
                    {
                        var conflicts = await _calendar.GetConflictsAsync(start, end);
                        if (conflicts.Count > 0)
                        {
                            var clash = string.Join("; ", conflicts.Select(c => $"{c.TimeLabel} {c.Title}"));
                            return $"CONFLICT: the requested slot ({start:yyyy-MM-dd HH:mm}–{end:HH:mm}) overlaps with {clash}. " +
                                   "Do NOT create it yet. Tell the user about the clash and ask whether to book anyway " +
                                   "(then retry with force=true), choose another time, or cancel.";
                        }
                    }

                    var created = await _calendar.CreateEventAsync(
                        args.Title, start, end, args.Location, reminderMinutes: args.ReminderMinutes);
                    onCreated(created);

                    return $"SUCCESS: created \"{created.Title}\" on {start:yyyy-MM-dd HH:mm}–{end:HH:mm}" +
                           (args.ReminderMinutes is int rm ? $" with a {rm}-minute reminder" : "") + ".";
                })
            .WithToolChoice("auto")
            .WithMaxIterations(5)
            .Build();

    /// <summary>Strips Qwen3 &lt;think&gt; blocks and trims. May return empty (caller substitutes a fallback).</summary>
    private static string CleanReply(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return ThinkTag.Replace(raw, "").Trim();
    }

    /// <summary>
    /// When the model performs a tool action but returns no prose (common with small models),
    /// synthesize a sensible confirmation from the tool result, in the user's language.
    /// </summary>
    private static string FallbackText(CalEvent? created, DayPlan? day, string language)
    {
        var pl = language == "Polish";
        if (created is not null)
        {
            var startS = created.Start?.ToString("dd.MM.yyyy HH:mm") ?? "";
            var endS = created.End?.ToString("HH:mm") ?? "";
            var rem = created.ReminderMinutes is int rm
                ? (pl ? $", przypomnienie {rm} min wcześniej" : $", reminder {rm} min before")
                : "";
            return pl
                ? $"Gotowe ✅ Dodałem „{created.Title}” — {startS}–{endS}{rem}."
                : $"Done ✅ Added \"{created.Title}\" — {startS}–{endS}{rem}.";
        }
        if (day is not null)
        {
            if (day.Count == 0)
                return pl ? "Ten dzień jest wolny — nic nie zaplanowano." : "That day is free — nothing scheduled.";
            return pl
                ? $"Masz {day.Count} {(day.Count == 1 ? "wydarzenie" : "wydarzeń")} tego dnia — szczegóły poniżej."
                : $"You have {day.Count} event(s) that day — details below.";
        }
        return pl
            ? "Hmm, nie jestem pewien, co dokładnie zrobić — możesz doprecyzować?"
            : "Hmm, I'm not sure what to do exactly — could you clarify?";
    }

    private sealed class DateArgs { public string? Date { get; set; } }
    private sealed class RangeArgs { public string? Start { get; set; } public string? End { get; set; } }

    private sealed class CreateEventArgs
    {
        public string? Title { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Location { get; set; }
        public int? ReminderMinutes { get; set; }
        public bool Force { get; set; }
    }
}
