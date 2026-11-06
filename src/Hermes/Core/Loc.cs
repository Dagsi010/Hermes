using System.ComponentModel;
using System.Globalization;

namespace Hermes.Core;

/// <summary>
/// Icelandic/English strings. XAML binds through the indexer
/// ({Binding [key], Source={x:Static core:Loc.Instance}}) so text updates live
/// when the language changes.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    static readonly CultureInfo Icelandic = new("is-IS");
    static readonly CultureInfo English = new("en-GB");

    string _language = "is";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string Language
    {
        get => _language;
        set
        {
            var lang = value == "en" ? "en" : "is";
            if (lang == _language) return;
            _language = lang;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke();
        }
    }

    public bool IsEnglish => _language == "en";
    public CultureInfo Culture => IsEnglish ? English : Icelandic;

    public string this[string key] => Get(key);

    public string Get(string key) =>
        Strings.TryGetValue(key, out var s) ? (IsEnglish ? s.En : s.Is) : key;

    public static string T(string key) => Instance.Get(key);
    public static string F(string key, params object[] args) =>
        string.Format(Instance.Culture, Instance.Get(key), args);

    /// <summary>"18 mín", "1 klst 5 mín".</summary>
    public static string Duration(TimeSpan span)
    {
        var minutes = Math.Max(0, (int)Math.Ceiling(span.TotalMinutes));
        if (minutes < 60) return F("dur.min", minutes);
        return minutes % 60 == 0 ? F("dur.h", minutes / 60) : F("dur.hmin", minutes / 60, minutes % 60);
    }

    /// <summary>Countdown label for a due date: "í dag", "á morgun", "4 dagar".</summary>
    public static string Countdown(DateTime due, DateTime now)
    {
        var days = (due.Date - now.Date).Days;
        if (days < 0 || (days == 0 && due < now)) return T("due.overdue");
        if (days == 0) return T("due.today");
        if (days == 1) return T("due.tomorrow");
        // Icelandic: 21, 31, ... take the singular, except 11.
        var singular = days % 10 == 1 && days % 100 != 11;
        return F(Instance.IsEnglish || !singular ? "due.days" : "due.day", days);
    }

    public static string Time(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DateLong(DateTime d) =>
        d.ToString(Instance.IsEnglish ? "dddd d MMMM" : "dddd d. MMMM", Instance.Culture);

    public static string DayShort(DateTime d)
    {
        var name = Instance.Culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.');
        return Instance.IsEnglish ? $"{name} {d.Day}" : $"{name} {d.Day}.";
    }

    public static string DayName(DateTime d) => Instance.Culture.DateTimeFormat.GetDayName(d.DayOfWeek);

    static readonly Dictionary<string, (string Is, string En)> Strings = new()
    {
        // Tray
        ["tray.refresh"] = ("Uppfæra núna", "Refresh now"),
        ["tray.editLayout"] = ("Breyta uppsetningu", "Edit layout"),
        ["tray.hide"] = ("Fela yfirlag", "Hide overlay"),
        ["tray.show"] = ("Sýna yfirlag", "Show overlay"),
        ["tray.settings"] = ("Stillingar", "Settings"),
        ["tray.signOut"] = ("Skrá út af Innu", "Sign out of Inna"),
        ["tray.exit"] = ("Hætta", "Exit"),

        // Overlay
        ["ov.today"] = ("Í dag", "Today"),
        ["ov.week"] = ("Vika", "Week"),
        ["ov.work"] = ("Verkefni", "Assignments"),
        ["ov.notices"] = ("Tilkynningar", "Notifications"),
        ["ov.noWork"] = ("Ekkert á dagskrá", "Nothing due"),
        ["ov.noClasses"] = ("Engir tímar í dag", "No classes today"),
        ["ov.next"] = ("Næst", "Next"),
        ["ov.updated"] = ("Uppfært {0}", "Updated {0}"),
        ["ov.sample"] = ("Prufugögn", "Sample data"),
        ["ov.exam"] = ("próf", "exam"),
        ["ov.part.schedule"] = ("Stundatafla", "Schedule"),
        ["ov.part.work"] = ("Verkefni og tilkynningar", "Work and notifications"),
        ["ov.part.progress"] = ("Framvinda", "Progress"),
        ["ov.done"] = ("Lokið", "Done"),

        // Progress text
        ["pr.left"] = ("{0} eftir", "{0} left"),
        ["pr.next"] = ("næst {0} {1}", "next {0} {1}"),
        ["pr.last"] = ("síðasti tími", "last class"),
        ["br.break"] = ("Frímínútur", "Break"),
        ["br.lunch"] = ("Hádegishlé", "Lunch break"),
        ["pr.before"] = ("Fyrsti tími: {0} kl. {1}", "First class: {0} at {1}"),
        ["pr.beforeIn"] = ("eftir {0}", "in {0}"),
        ["pr.after"] = ("Skóla lokið", "School's out"),
        ["pr.tomorrow"] = ("á morgun {0} {1}", "tomorrow {0} {1}"),
        ["pr.onDay"] = ("{0} {1} {2}", "{0} {1} {2}"),
        ["pr.noSchool"] = ("Engin kennsla í dag", "No school today"),
        ["pr.dayDone"] = ("Dagurinn {0}% búinn", "Day {0}% done"),
        ["pr.endsAt"] = ("skóla lýkur {0}", "school ends {0}"),
        ["pr.endedAt"] = ("Skóla lauk {0}", "School ended {0}"),

        // Durations and due dates
        ["dur.min"] = ("{0} mín", "{0} min"),
        ["dur.h"] = ("{0} klst", "{0} h"),
        ["dur.hmin"] = ("{0} klst {1} mín", "{0} h {1} min"),
        ["due.today"] = ("í dag", "today"),
        ["due.tomorrow"] = ("á morgun", "tomorrow"),
        ["due.day"] = ("{0} dagur", "{0} day"),
        ["due.days"] = ("{0} dagar", "{0} days"),
        ["due.overdue"] = ("of seint", "overdue"),

        // Notices
        ["n.grade"] = ("Ný einkunn: {0} {1}", "New grade: {0} {1}"),
        ["n.attendance"] = ("Mæting {0}%", "Attendance {0}%"),
        ["n.newWork"] = ("Nýtt verkefni: {0}", "New assignment: {0}"),
        ["n.signIn"] = ("Skrá inn aftur", "Sign in again"),

        // Settings
        ["s.title"] = ("Hermes · Stillingar", "Hermes · Settings"),
        ["s.general"] = ("Almennt", "General"),
        ["s.startWithWindows"] = ("Ræsa með Windows", "Start with Windows"),
        ["s.language"] = ("Tungumál", "Language"),
        ["s.appearance"] = ("Útlit", "Appearance"),
        ["s.theme"] = ("Þema", "Theme"),
        ["s.textColor"] = ("Litur texta", "Text colour"),
        ["s.color.light"] = ("Ljós", "Light"),
        ["s.color.dark"] = ("Dökkur", "Dark"),
        ["s.color.auto"] = ("Sjálfvirkt", "Auto"),
        ["s.textSize"] = ("Leturstærð", "Text size"),
        ["s.monitor"] = ("Skjár", "Monitor"),
        ["s.monitor.primary"] = ("aðalskjár", "primary"),
        ["s.editLayout"] = ("Breyta uppsetningu", "Edit layout"),
        ["s.refresh"] = ("Uppfærsla", "Refresh"),
        ["s.refreshTime"] = ("Sækja gögn daglega kl.", "Refresh daily at"),
        ["s.hotkey"] = ("Flýtilykill", "Hotkey"),
        ["s.hotkey.hint"] = ("Smelltu á hnappinn og ýttu svo á nýja samsetningu, t.d. Ctrl+Alt+K eða F9. Esc hættir við.",
            "Click the button, then press the new combination, e.g. Ctrl+Alt+K or F9. Esc cancels."),
        ["s.hotkey.press"] = ("Ýttu á lyklana...", "Press the keys..."),
        ["s.hotkey.inUse"] = ("Ekki tókst að skrá flýtilykil (í notkun?)", "Could not register hotkey (in use?)"),
        ["s.hotkeyAction"] = ("Flýtilykill gerir", "Hotkey does"),
        ["s.hotkey.refresh"] = ("Uppfæra gögn", "Refresh data"),
        ["s.hotkey.toggle"] = ("Fela/sýna yfirlag", "Hide/show overlay"),
        ["s.notifications"] = ("Tilkynningar", "Notifications"),
        ["s.reminder"] = ("Kvöldáminning um skil á morgun", "Evening reminder for work due tomorrow"),
        ["s.reminderTime"] = ("Áminning kl.", "Reminder at"),
        ["s.newWork"] = ("Láta vita af nýjum verkefnum", "Alert on new assignments"),
        ["s.newGrades"] = ("Láta vita af nýjum einkunnum", "Alert on new grades"),
        ["s.attendance"] = ("Viðvörun ef mæting fer undir", "Warn when attendance drops below"),
        ["s.calendar"] = ("Dagatal", "Calendar"),
        ["s.exportIcs"] = ("Flytja út .ics", "Export .ics"),
        ["s.calendarSync"] = ("Halda .ics skránni uppfærðri", "Keep the .ics file updated"),
        ["s.calendarHint"] = ("Skráin inniheldur tíma næstu tveggja vikna og öll opin verkefni og próf. Opnaðu hana í Outlook eða Windows-dagatalinu, eða flyttu inn í Google Calendar (Stillingar > Innflutningur). Bein samstilling við Google Calendar þarf Google-aðgangslykil sem þarf að setja upp einu sinni og er því ekki með.",
            "The file holds the next two weeks of classes and all open assignments and exams. Open it in Outlook or Windows Calendar, or import it into Google Calendar (Settings > Import). Direct Google Calendar sync needs a Google API key set up once, so it isn't included."),
        ["s.icsSaved"] = ("Vistað: {0}", "Saved: {0}"),
        ["cal.teacher"] = ("Kennari", "Teacher"),
        ["cal.due"] = ("Skil", "Due"),
        ["cal.exam"] = ("Próf", "Exam"),
        ["s.inna"] = ("Inna", "Inna"),
        ["s.inna.notSignedIn"] = ("Ekki skráð(ur) inn", "Not signed in"),
        ["s.inna.signedIn"] = ("Innskráð(ur) í Innu", "Signed in to Inna"),
        ["s.inna.checking"] = ("Athuga innskráningu...", "Checking sign-in..."),
        ["s.inna.expired"] = ("Innskráning er útrunnin", "Sign-in has expired"),
        ["s.inna.offline"] = ("Ekki náðist í Innu (nettenging?)", "Could not reach Inna (offline?)"),
        ["s.inna.unknown"] = ("Staða óþekkt", "Status unknown"),
        ["s.inna.method"] = ("Leið: {0}", "Method: {0}"),
        ["s.inna.lastFetch"] = ("síðast sótt {0}", "last fetched {0}"),
        ["s.inna.privacy"] = ("Hermes geymir aldrei lykilorð. Aðeins innskráningarkökur Innu og Google eru geymdar í %LOCALAPPDATA%\\Hermes\\WebView2.",
            "Hermes never stores passwords. Only Inna's and Google's sign-in cookies are kept in %LOCALAPPDATA%\\Hermes\\WebView2."),
        ["s.checkNow"] = ("Athuga núna", "Check now"),
        ["tray.signIn"] = ("Skrá inn í Innu", "Sign in to Inna"),
        ["n.signInFirst"] = ("Skrá inn í Innu", "Sign in to Inna"),
        ["t.reminder.title"] = ("Skil á morgun ({0})", "Due tomorrow ({0})"),
        ["t.news.title"] = ("{0} nýtt í Innu", "{0} new in Inna"),
        ["b.expired.title"] = ("Innskráning í Innu útrunnin", "Inna sign-in expired"),
        ["b.expired.text"] = ("Smelltu hér til að skrá þig inn aftur.", "Click here to sign in again."),

        // Sign-in window
        ["si.title"] = ("Hermes · Innskráning", "Hermes · Sign in"),
        ["si.header"] = ("Skráðu þig inn í Innu", "Sign in to Inna"),
        ["si.note"] = ("Þú skráir þig sjálf(ur) inn hjá Innu og Google. Hermes geymir aldrei lykilorðið þitt, aðeins innskráninguna sjálfa.",
            "You sign in with Inna and Google yourself. Hermes never stores your password, only the signed-in session."),
        ["si.success"] = ("Innskráning tókst", "Signed in"),
        ["si.blocked.title"] = ("Google leyfði ekki innskráningu hér", "Google blocked sign-in here"),
        ["si.blocked.text"] = ("Google lokar stundum á innskráningu í innbyggðum vöfrum og Hermes reynir ekki að fara fram hjá því. Prófaðu aðra leið inn í Innu:",
            "Google sometimes blocks sign-in inside embedded browsers, and Hermes won't try to get around that. Try another way into Inna:"),
        ["si.island"] = ("Ísland.is (rafræn skilríki)", "Ísland.is (electronic ID)"),
        ["si.islandNote"] = ("Virkar, en þá þarf að staðfesta í símanum í hvert sinn sem innskráning rennur út.",
            "Works, but you confirm on your phone each time the sign-in expires."),
        ["si.password"] = ("Lykilorð frá Innu", "Inna password"),
        ["si.passwordNote"] = ("Ef skólinn hefur gefið þér lykilorð að Innu (\"Lykilorð\" á forsíðunni).",
            "If your school has given you an Inna password (\"Lykilorð\" on the front page)."),
        ["si.retryGoogle"] = ("Reyna Google aftur", "Try Google again"),
        ["s.signIn"] = ("Skrá inn", "Sign in"),
        ["s.signOut"] = ("Skrá út", "Sign out"),
        ["s.debug"] = ("Villuleit", "Debug"),
        ["s.debugCapture"] = ("Skrá netbeiðnir Innu (debug capture)", "Log Inna network requests (debug capture)"),
        ["s.openDataFolder"] = ("Opna gagnamöppu", "Open data folder"),
        ["s.openCapture"] = ("Hefja upptöku", "Start capture"),
        ["s.captureHint"] = ("Opnar Innu í glugga og skráir allar beiðnir sem síðan gerir. Vistað í %LOCALAPPDATA%\\Hermes\\captures.",
            "Opens Inna in a window and records every request the page makes. Saved to %LOCALAPPDATA%\\Hermes\\captures."),

        // Capture window
        ["cap.title"] = ("Hermes · Upptaka", "Hermes · Capture"),
        ["cap.header"] = ("Tek upp beiðnir Innu", "Recording Inna's requests"),
        ["cap.count"] = ("{0} beiðnir, {1} með gögnum", "{0} requests, {1} with data"),
        ["cap.steps"] = ("Opnaðu í Innu, eina í einu: stundatöflu (í dag og vikuna), verkefni, einkunnir og mætingu. Bíddu þar til hver síða hefur hlaðist, ýttu svo á Vista og sendu skrána.",
            "In Inna, open one at a time: the timetable (today and the week), assignments, grades and attendance. Wait for each page to load, then press Save and send the file."),
        ["cap.privacy"] = ("Kökur, innskráningarlyklar, kennitölur og netföng eru falin. Nöfn, einkunnir og stundatafla eru í skránni; skoðaðu hana áður en þú sendir.",
            "Cookies, sign-in tokens, kennitölur and email addresses are masked. Names, grades and the timetable are in the file; look it over before sending."),
        ["cap.save"] = ("Vista skrá", "Save file"),
        ["cap.close"] = ("Loka", "Close"),
        ["s.version"] = ("Útgáfa {0}", "Version {0}"),
    };
}
