using System.Globalization;

namespace CalAssistant.Services;

public enum AppLang { Pl, En }

/// <summary>
/// Tiny two-language (PL/EN) localizer. Scoped — one selected language per Blazor circuit.
/// UI strings come from here; the assistant also reads <see cref="LanguageName"/> to reply in the chosen language.
/// </summary>
public class Localizer
{
    public AppLang Lang { get; private set; } = AppLang.Pl;

    public event Action? Changed;

    public void Set(AppLang lang)
    {
        if (Lang == lang) return;
        Lang = lang;
        Changed?.Invoke();
    }

    public void Toggle() => Set(Lang == AppLang.Pl ? AppLang.En : AppLang.Pl);

    /// <summary>"Polish" / "English" — passed to the model as the required reply language.</summary>
    public string LanguageName => Lang == AppLang.Pl ? "Polish" : "English";

    public CultureInfo Culture => CultureInfo.GetCultureInfo(Lang == AppLang.Pl ? "pl-PL" : "en-US");

    public string this[string key] =>
        (Lang == AppLang.Pl ? Pl : En).TryGetValue(key, out var v) ? v : key;

    public IReadOnlyList<string> Suggestions => Lang == AppLang.Pl
        ? new[]
        {
            "Co mam dzisiaj do zrobienia?",
            "Jaki mam plan na jutro?",
            "Umów spotkanie z Kasią jutro o 15:00",
            "Przypomnij mi o lekach dziś o 21:00"
        }
        : new[]
        {
            "What do I have today?",
            "What's my schedule tomorrow?",
            "Schedule a meeting with Kate tomorrow at 3pm",
            "Remind me about meds today at 9pm"
        };

    private static readonly Dictionary<string, string> Pl = new()
    {
        ["app.title"] = "Asystent Kalendarza",
        ["brand.name"] = "Asystent Kalendarza",
        ["brand.tag"] = "Twój kalendarz, po ludzku",
        ["status.connected"] = "Kalendarz połączony",
        ["status.notConnected"] = "Nie połączono",
        ["status.missingCreds"] = "Brak credentials.json",
        ["btn.connect"] = "Połącz kalendarz",
        ["btn.connecting"] = "Łączę…",
        ["btn.send"] = "Wyślij",
        ["composer.placeholder"] = "Napisz wiadomość…",
        ["suggestions.title"] = "Na dobry początek",
        ["help.googleTitle"] = "Podłączenie Google",
        ["help.g1"] = "Google Cloud → włącz Calendar API",
        ["help.g2"] = "Utwórz OAuth Client ID typu Desktop",
        ["help.g3"] = "Zapisz plik jako credentials.json w katalogu projektu",
        ["help.g4"] = "Uruchom ponownie i kliknij „Połącz kalendarz”",
        ["help.readme"] = "Szczegóły w README.md",
        ["lang.switch"] = "EN",
        ["assistant.greeting"] = "Cześć! Jestem Twoim asystentem kalendarza. Pokażę Ci plan dnia, umówię spotkanie " +
                                 "(najpierw sprawdzę, czy masz wolne) albo ustawię przypomnienie. Napisz po prostu, czego potrzebujesz.",
        ["status.thinking"] = "Analizuję Twoją prośbę…",
        ["status.reading"] = "Zaglądam do Twojego kalendarza…",
        ["status.checking"] = "Sprawdzam, czy masz wtedy wolne…",
        ["status.creating"] = "Zapisuję wydarzenie w kalendarzu…",
        ["status.writing"] = "Formułuję odpowiedź…",
        ["err.timeout"] = "To zajęło zbyt długo. Spróbuj jeszcze raz albo sformułuj krócej.",
        ["err.generic"] = "Coś poszło nie tak. Spróbuj ująć to inaczej.",
        // DayTimeline
        ["dt.event"] = "wydarzenie",
        ["dt.events"] = "wydarzenia/wydarzeń",
        ["dt.booked"] = "zajęte",
        ["dt.free"] = "wolne",
        ["dt.freeDay"] = "Wolny dzień — nic nie zaplanowano",
        ["dt.nextUp"] = "Najbliżej",
        ["dt.allDay"] = "cały dzień",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["app.title"] = "Calendar Assistant",
        ["brand.name"] = "Calendar Assistant",
        ["brand.tag"] = "Your calendar, in plain words",
        ["status.connected"] = "Calendar connected",
        ["status.notConnected"] = "Not connected",
        ["status.missingCreds"] = "Missing credentials.json",
        ["btn.connect"] = "Connect calendar",
        ["btn.connecting"] = "Connecting…",
        ["btn.send"] = "Send",
        ["composer.placeholder"] = "Type a message…",
        ["suggestions.title"] = "Try one of these",
        ["help.googleTitle"] = "Connect Google",
        ["help.g1"] = "Google Cloud → enable Calendar API",
        ["help.g2"] = "Create an OAuth Client ID of type Desktop",
        ["help.g3"] = "Save the file as credentials.json in the project root",
        ["help.g4"] = "Restart and click “Connect calendar”",
        ["help.readme"] = "See README.md for details",
        ["lang.switch"] = "PL",
        ["assistant.greeting"] = "Hi! I'm your calendar assistant. I can show your day, schedule meetings " +
                                 "(checking you're actually free first), or set reminders. Just tell me what you need.",
        ["status.thinking"] = "Understanding your request…",
        ["status.reading"] = "Looking at your calendar…",
        ["status.checking"] = "Checking whether you're free then…",
        ["status.creating"] = "Saving the event to your calendar…",
        ["status.writing"] = "Writing the reply…",
        ["err.timeout"] = "That took too long. Please try again or phrase it more briefly.",
        ["err.generic"] = "Something went wrong. Please try rephrasing.",
        // DayTimeline
        ["dt.event"] = "event",
        ["dt.events"] = "events",
        ["dt.booked"] = "booked",
        ["dt.free"] = "free",
        ["dt.freeDay"] = "Free day — nothing scheduled",
        ["dt.nextUp"] = "Up next",
        ["dt.allDay"] = "all day",
    };
}
