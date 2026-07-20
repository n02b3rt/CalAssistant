using CalAssistant.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using GCalService = Google.Apis.Calendar.v3.CalendarService;

namespace CalAssistant.Services;

/// <summary>
/// Thin wrapper over Google Calendar API. Desktop app OAuth;
/// token cached locally in token-store/. Single user = singleton.
/// </summary>
public class CalendarService
{
    private static readonly string[] Scopes = { GCalService.Scope.Calendar };
    private const string AppName = "CalAssistant";

    private readonly string _credentialsPath;
    private readonly string _tokenFolder;
    private readonly ILogger<CalendarService> _log;

    private GCalService? _service;

    public CalendarService(IWebHostEnvironment env, IConfiguration cfg, ILogger<CalendarService> log)
    {
        _log = log;
        var root = env.ContentRootPath;
        _credentialsPath = Path.IsPathRooted(cfg["Google:CredentialsPath"] ?? "")
            ? cfg["Google:CredentialsPath"]!
            : Path.Combine(root, cfg["Google:CredentialsPath"] ?? "credentials.json");
        _tokenFolder = Path.Combine(root, "token-store");
    }

    /// <summary>Whether credentials.json exists (connection can be attempted).</summary>
    public bool IsConfigured => File.Exists(_credentialsPath);

    /// <summary>Whether an authorized connection is active.</summary>
    public bool IsConnected => _service is not null;

    /// <summary>
    /// Starts OAuth: opens the browser, user signs in to Google and grants access.
    /// Token is saved locally so later runs skip login.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_service is not null) return;
        if (!IsConfigured)
            throw new InvalidOperationException(
                $"Missing credentials.json ({_credentialsPath}). Download it from Google Cloud Console.");

        await using var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            Scopes,
            "user",
            ct,
            new FileDataStore(_tokenFolder, fullPath: true));

        _service = new GCalService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = AppName
        });
        _log.LogInformation("Google Calendar connected.");
    }

    /// <summary>Fetches primary calendar events for the given day (00:00–24:00 local time).</summary>
    public async Task<DayPlan> GetDayAsync(DateTime day, CancellationToken ct = default)
    {
        EnsureConnected();
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);

        var req = _service!.Events.List("primary");
        req.TimeMinDateTimeOffset = new DateTimeOffset(dayStart);
        req.TimeMaxDateTimeOffset = new DateTimeOffset(dayEnd);
        req.SingleEvents = true;
        req.ShowDeleted = false;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var items = (await req.ExecuteAsync(ct)).Items ?? new List<Event>();

        var plan = new DayPlan { Date = dayStart };
        foreach (var ev in items)
            plan.Events.Add(Map(ev));
        return plan;
    }

    /// <summary>Creates an event. When reminderMinutes is set, adds a popup reminder.</summary>
    public async Task<CalEvent> CreateEventAsync(
        string title, DateTime start, DateTime end,
        string? location = null, string? description = null,
        int? reminderMinutes = null, CancellationToken ct = default)
    {
        EnsureConnected();
        var ev = new Event
        {
            Summary = title,
            Location = location,
            Description = description,
            Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(start) },
            End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(end) },
        };

        if (reminderMinutes is int m)
        {
            ev.Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = new List<EventReminder>
                {
                    new() { Method = "popup", Minutes = m }
                }
            };
        }

        var created = await _service!.Events.Insert(ev, "primary").ExecuteAsync(ct);
        var mapped = Map(created);
        mapped.ReminderMinutes = reminderMinutes;
        return mapped;
    }

    private void EnsureConnected()
    {
        if (_service is null)
            throw new InvalidOperationException("Calendar is not connected. Click \"Connect calendar\".");
    }

    private static CalEvent Map(Event ev)
    {
        var startOffset = ev.Start?.DateTimeDateTimeOffset;
        var endOffset = ev.End?.DateTimeDateTimeOffset;
        var allDay = startOffset is null && ev.Start?.Date is not null;

        DateTime? start = startOffset?.LocalDateTime
            ?? (DateTime.TryParse(ev.Start?.Date, out var sd) ? sd : null);
        DateTime? end = endOffset?.LocalDateTime
            ?? (DateTime.TryParse(ev.End?.Date, out var ed) ? ed : null);

        return new CalEvent
        {
            Id = ev.Id ?? "",
            Title = string.IsNullOrWhiteSpace(ev.Summary) ? "(no title)" : ev.Summary,
            Start = start,
            End = end,
            AllDay = allDay,
            Location = ev.Location,
            Description = ev.Description
        };
    }
}
