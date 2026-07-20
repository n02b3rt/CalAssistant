namespace CalAssistant.Models;

public enum ChatRole { User, Assistant }

/// <summary>Chat message. May carry a DayPlan for timeline rendering.</summary>
public class ChatMessage
{
    public ChatRole Role { get; set; }
    public string Text { get; set; } = "";
    public DayPlan? Day { get; set; }
    public CalEvent? CreatedEvent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsTyping { get; set; }
}

/// <summary>Simplified Google Calendar event for display.</summary>
public class CalEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public bool AllDay { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public int? ReminderMinutes { get; set; }

    public string TimeLabel => AllDay
        ? "all day"
        : $"{Start:HH:mm}–{End:HH:mm}";

    public double DurationHours =>
        (Start.HasValue && End.HasValue) ? (End.Value - Start.Value).TotalHours : 1;
}

/// <summary>Day plan — data for the timeline component.</summary>
public class DayPlan
{
    public DateTime Date { get; set; }
    public List<CalEvent> Events { get; set; } = new();
    public int Count => Events.Count;
    public double TotalHours => Events.Where(e => !e.AllDay).Sum(e => e.DurationHours);
    public CalEvent? Next => Events
        .Where(e => !e.AllDay && e.Start.HasValue && e.Start.Value >= DateTime.Now)
        .OrderBy(e => e.Start)
        .FirstOrDefault();
}
