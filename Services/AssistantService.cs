using System.Globalization;
using System.Text.Json;
using CalAssistant.Models;
using MaIN.Core.Hub;
using MaIN.Core.Hub.Utils;
using MaIN.Domain.Configuration.BackendInferenceParams;

namespace CalAssistant.Services;

/// <summary>
/// Conversation orchestrator. qwen3:4b handles native tool calling via Ollama.
/// The model decides when to invoke a tool (list_day / create_event); MaIN.NET runs the loop.
/// Registered as Scoped — one state per Blazor circuit.
/// </summary>
public class AssistantService
{
    public const string ModelName = "qwen3:4b";

    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IMaINHub _hub;
    private readonly CalendarService _calendar;
    private readonly ILogger<AssistantService> _log;

    public List<ChatMessage> Messages { get; } = new();
    public bool Busy { get; private set; }
    public event Action? Changed;
    private void Raise() => Changed?.Invoke();

    public AssistantService(IMaINHub hub, CalendarService calendar, ILogger<AssistantService> log)
    {
        _hub = hub;
        _calendar = calendar;
        _log = log;
        Messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Text = "Hi! I'm your calendar assistant. I can show your day plan, schedule meetings, " +
                   "or set reminders. Try *\"what do I have today?\"* or *\"schedule a meeting with Kate tomorrow at 3pm\"*."
        });
    }

    public bool CalendarConfigured => _calendar.IsConfigured;
    public bool CalendarConnected => _calendar.IsConnected;

    public async Task ConnectCalendarAsync() => await _calendar.ConnectAsync();

    /// <summary>Main entry: processes a user message and appends the assistant reply.</summary>
    public async Task<ChatMessage> SendAsync(string userText)
    {
        Messages.Add(new ChatMessage { Role = ChatRole.User, Text = userText });
        Busy = true;
        Raise();

        ChatMessage reply;
        try
        {
            reply = await RunWithToolsAsync(userText);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to process message");
            reply = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = "Something went wrong. Try rephrasing your request."
            };
        }
        finally
        {
            Busy = false;
        }

        Messages.Add(reply);
        Raise();
        return reply;
    }

    private async Task<ChatMessage> RunWithToolsAsync(string userText)
    {
        var now = DateTime.Now;
        DayPlan? dayPlanResult = null;
        CalEvent? createdEventResult = null;

        var system =
            "You are a friendly calendar assistant. Reply in English, warmly and concisely.\n" +
            $"Current date and time: {now:yyyy-MM-dd HH:mm} ({En.DateTimeFormat.GetDayName(now.DayOfWeek)}).\n" +
            "When the user wants to see their day plan — call the list_day tool.\n" +
            "When they want to schedule a meeting or set a reminder — call the create_event tool.\n" +
            "Resolve relative dates and times ('today', 'tomorrow', 'at 3pm') to absolute values before calling a tool.\n" +
            "After receiving a tool result, write a short natural reply. " +
            "Do NOT list events as bullet points — the UI renders the timeline.";

        var tools = new ToolsConfigurationBuilder()
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
                    if (!_calendar.IsConnected)
                        return "ERROR: calendar is not connected.";

                    var args = JsonSerializer.Deserialize<DateArgs>(argsJson, JsonOpts);
                    if (!DateTime.TryParse(args?.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        date = DateTime.Today;

                    var plan = await _calendar.GetDayAsync(date);
                    dayPlanResult = plan;

                    if (plan.Count == 0)
                        return $"No events on {date:MMMM d, yyyy}.";

                    var lines = plan.Events.Select(e =>
                        $"{e.TimeLabel}: {e.Title}{(e.Location is not null ? $" ({e.Location})" : "")}");
                    return $"Found {plan.Count} events on {date:MMMM d, yyyy}:\n" + string.Join("\n", lines);
                })
            .AddTool(
                name: "create_event",
                description: "Creates a new event (or reminder) in Google Calendar.",
                parameters: new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Event title" },
                        start = new { type = "string", description = "Start date/time, ISO 8601: YYYY-MM-DDTHH:mm" },
                        end = new { type = "string", description = "End date/time, ISO 8601: YYYY-MM-DDTHH:mm. Defaults to one hour after start." },
                        location = new { type = "string", description = "Location (optional)" },
                        reminder_minutes = new { type = "integer", description = "Reminder X minutes before (optional)" }
                    },
                    required = new[] { "title", "start" }
                },
                execute: async (string argsJson) =>
                {
                    if (!_calendar.IsConnected)
                        return "ERROR: calendar is not connected.";

                    var args = JsonSerializer.Deserialize<CreateEventArgs>(argsJson, JsonOpts);
                    if (args is null || string.IsNullOrWhiteSpace(args.Title) || string.IsNullOrWhiteSpace(args.Start))
                        return "ERROR: missing required fields (title, start).";

                    if (!DateTime.TryParse(args.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                        return $"ERROR: invalid date/time: {args.Start}.";

                    DateTime end;
                    if (!string.IsNullOrWhiteSpace(args.End) &&
                        DateTime.TryParse(args.End, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
                        end = parsedEnd;
                    else
                        end = start.AddHours(1);

                    var created = await _calendar.CreateEventAsync(
                        args.Title, start, end, args.Location,
                        reminderMinutes: args.ReminderMinutes);

                    createdEventResult = created;
                    return $"Created event \"{created.Title}\" on {start:MMMM d, yyyy h:mm tt}–{end:h:mm tt}.";
                })
            .WithToolChoice("auto")
            .WithMaxIterations(5)
            .Build();

        var result = await _hub.Chat()
            .WithModel(ModelName)
            .WithMessage(userText)
            .WithSystemPrompt(system)
            .WithInferenceParams(new OllamaInferenceParams { Temperature = 0.4f })
            .WithTools(tools)
            .DisableCache()
            .CompleteAsync();

        var text = result.Message.Content?.Trim() ?? "Something went wrong — no response from the model.";

        return new ChatMessage
        {
            Role = ChatRole.Assistant,
            Text = text,
            Day = dayPlanResult,
            CreatedEvent = createdEventResult
        };
    }

    private sealed class DateArgs
    {
        public string? Date { get; set; }
    }

    private sealed class CreateEventArgs
    {
        public string? Title { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Location { get; set; }
        public int? ReminderMinutes { get; set; }
    }
}
