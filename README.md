# Hermes

A desktop widget for Verzló students: your Inna timetable, assignments and
notifications drawn straight onto the wallpaper, with a tray icon for settings.

Everything runs by itself: Hermes starts with Windows, signs in to Inna in the background
with your saved Google session, fetches your timetable, assignments, grades and attendance,
and refreshes daily. You only sign in once, the very first time.

## Requirements

- Windows 10 or 11, x64
- .NET 10 SDK to build (the published exe needs nothing installed)
- WebView2 runtime (ships with Windows 10/11; used from stage 2)

Install the SDK without admin rights:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
& $env:TEMP\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
```

or with winget: `winget install Microsoft.DotNet.SDK.10`.

## Build and run

```powershell
dotnet build src\Hermes -c Debug
src\Hermes\bin\Debug\net10.0-windows\win-x64\Hermes.exe
```

## Publish the single exe

```powershell
dotnet publish src\Hermes -c Release -o publish
```

Result: `publish\Hermes.exe` (self-contained, win-x64, about 165 MB). Copy it
anywhere and run it. On launch it points the "start with Windows" entry at
wherever the exe currently is.

Single-file compression is off on purpose: it halves the file size but makes
the app use about 90 MB more RAM, which matters for something that runs all day.

## Where things are stored

| What | Where |
| --- | --- |
| Settings (JSON, incl. overlay positions per monitor) | `%APPDATA%\Hermes\settings.json` |
| Ticked-off assignments | `%LOCALAPPDATA%\Hermes\local-state.json` |
| Log | `%LOCALAPPDATA%\Hermes\hermes.log` |
| Data cache (stage 3) | `%LOCALAPPDATA%\Hermes\cache.json` |
| Inna browser session (stage 2) | `%LOCALAPPDATA%\Hermes\WebView2\` |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Hermes` |

Your Google password is never stored; only the WebView2 session cookies are.

Delete `settings.json` to reset all settings and positions.

## Using it

- **Tray icon** (the caduceus): left-click opens Settings. Right-click: Refresh now,
  Edit layout, Hide/Show overlay, Settings, Sign out of Inna, Exit.
- **Edit layout**: outlines appear around the three parts. Drag to move, drag a corner
  to resize, press *Lokið/Done* on any part to save. Positions are stored per monitor
  and resolution.
- **Themes** (Settings, Útlit): Navy, Forest, Ocean, Plum, Ember, Graphite. A theme colours
  the Settings panel and tray menu, and on the overlay the current class, the live progress
  segment and the deadline dots. Add one by appending to `Themes.All` in `Core/Themes.cs`.
- **Tick off an assignment**: click it. It fades with a strikethrough and hides a day
  later. Click again to undo.
- **Today / week**: click *Í dag* or *Vika* above the schedule.
- **Hotkey** (default Ctrl+Alt+H): refresh, or hide/show the overlay (set in Settings).
- **Debug section** in Settings: click the version line at the bottom five times.

## Inna sign-in

- On first run a sign-in window shows Inna's own login page (www.inna.is). You sign in
  yourself once (Google, or Ísland.is / Inna password). Hermes notices when the browser
  reaches the school system (`nam.inna.is/Components/Students/...`) and closes the window.
- The route in is `www.inna.is/auth/google` → Google → `r.inna.is/auth/google/callback` →
  `r.inna.is/adgangur` → `nam.inna.is`. The school system's session cookies only last until
  the browser closes, but Google's cookies in the profile last about a year, so on every
  start Hermes runs that route in a hidden browser and is signed in again with no input.
- The session lives in the WebView2 profile at `%LOCALAPPDATA%\Hermes\WebView2`. Password
  saving and autofill are switched off there, so no password is ever stored. Settings only
  record *which* route you used (`InnaLoginMethod`), never a credential.
- That background sign-in runs 5 s after start and on Refresh now. If it ever stops at a
  Google page that needs input, the overlay shows *Skrá inn aftur* under Tilkynningar and a
  Windows notification appears. `Hermes.exe --inna-diagnose <file>` writes the route it took
  (hosts and paths, storage key names, cookie names; no values) for debugging.
- Google sometimes refuses sign-in in embedded browsers. Hermes then retries once with a
  plain Edge user agent (remembered in `InnaEdgeUserAgent`); if Google still refuses, the
  window offers Ísland.is or an Inna password instead. It does not try to bypass Google.
- Sign out (tray or Settings) clears the whole Hermes WebView2 profile.

## Data from Inna

After the background sign-in, the same hidden browser calls Inna's own JSON API on
`nam.inna.is` (one script, all requests in parallel, about a second):

| What | Endpoint |
| --- | --- |
| Student and term ids | `/api/UserData/GetLoggedInUser` (`studentId`, `defaultTermId`) |
| Course names | `/api/StudentGrades/GetStudentGrades?termId=` (`moduleName` → `subjectName`) |
| Timetable, this and next week | `/api/Timetable/GetTimetable?student_id=&date_from=dd.MM.yyyy&date_to=` |
| Assignments / próf | `/api/GetAssignments/GetStudentAssignments?type=0` / `type=1` |
| Grades per course | `/api/StudentInformation/currentGroups`, then `/api/GetAssignments/GetStudentAssignmentsGroup?control=2&groupId=` |
| Attendance | `/api/Attendance/GetAttendance?termId=&type=0` (`attendanceTerm.realAttendance`) |

The result is cached in `%LOCALAPPDATA%\Hermes\cache.json` and shown with "Uppfært <time>"
(with the day when the copy is older than today). Handed-in assignments are hidden; próf are
labelled. The cache remembers when each grade and assignment first appeared, so *Ný einkunn*
and *Nýtt verkefni* only show for new ones (nothing counts as new on the very first fetch).

## Automatic refresh and notifications

- Hermes signs in and fetches on every start, then daily at the refresh time (Settings,
  default 16:00). A missed refresh (PC asleep or off at 16:00) runs on wake or at the next
  start; while offline it retries every 15 minutes. Refresh now and the hotkey fetch at once.
- Notifications (Windows notifications from the tray icon), each switchable in Settings:
  new grades, new assignments (one combined notification per fetch), and an evening
  reminder (default 19:00, once a day) listing open work due tomorrow.
- The progress bar and current-class highlight update every 15 seconds from the cached
  timetable; they don't need Inna.

## Breaks

Breaks come from the gaps in the timetable. Gaps of 15 minutes or more are listed in the
schedule and drawn as thin segments on the progress bar: the frímínútur after first class,
and *Hádegishlé* for the long midday gap (12:20-13:00). Shorter gaps are not listed, but
the bar text still says e.g. "Frímínútur · 3 mín eftir · næst Stærðfræði 09:35".

## Calendar export

Settings → Dagatal → *Flytja út .ics* writes the next two weeks of classes plus every open
assignment and próf (with a reminder an hour before). With *Halda .ics skránni uppfærðri*
on, the file is rewritten after every fetch. Event UIDs are stable, so re-importing updates
events instead of duplicating them. `Hermes.exe --export-ics <file>` writes it from the cache.
Direct Google Calendar sync is not included: it needs a Google Cloud OAuth client that you
create once (Inna's Google sign-in only grants Inna your email address).

## Text colour

*Ljós* (white text, soft dark shadow), *Dökkur* (dark text, no shadow) or *Sjálfvirkt*.
Auto decides per part: it reproduces how Windows places the wallpaper (Fill, Fit, Stretch,
Center, Tile, Span, plus the background colour around it), samples a 16×16 grid behind the
part's text and takes the brightness that 75% of the area stays under (a plain average hides
a bright patch). Dark text above 0.6, back to light below 0.5. It re-checks when the wallpaper
changes, after Edit layout, and every 10 minutes (slideshows). The log records each decision.

The tray icon's hover text shows the current line, e.g. "Danska · 35 mín eftir · næst Íslenska 13:00".

## Debug capture

Settings → click the version line five times → *Villuleit* → tick *Skrá netbeiðnir Innu* →
*Hefja upptöku*. Inna opens signed in; click through the pages and press *Vista skrá*. The
file lands in `%LOCALAPPDATA%\Hermes\captures\inna-capture-<time>.json` (also saved on close).

Recorded: navigations, in-page route changes, and every `*.inna.is` response except images,
fonts and CSS. Bodies are kept for data (JSON/XML/text) and HTML, scripts by URL only.
Masked before saving: Cookie/Authorization/CSRF headers (only Accept, Content-Type,
X-Requested-With and Referer are kept), secret-looking query values, `;jsessionid=`, JWTs,
kennitölur and email addresses. Names, grades and the timetable stay in, by design.

`Hermes.exe --capture-test <file>` runs the recorder on Inna's public front page and checks
the masking on made-up samples.

## How the desktop overlay works

The three parts are separate transparent, borderless WPF windows with
`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (no taskbar button, not in Alt+Tab, never
focused). `DesktopLayer` keeps them directly above the window that hosts the desktop
icons (`SHELLDLL_DefView` in Progman/WorkerW), so they sit under every app. When
Show desktop (Win+D) raises that host, the overlay follows it up, and drops back
when apps return. This is the approach Rainmeter uses for "on desktop" skins.

They are not reparented into WorkerW behind the icons, because windows there
cannot receive clicks and are destroyed when Explorer restarts.

Clicks pass through to the desktop (`WS_EX_TRANSPARENT`) except while the cursor is
over something clickable (assignment rows, links, the today/week toggle), checked
every 40 ms.

## Development flags

```powershell
# Pretend it is a given time (time keeps running from there)
Hermes.exe --fake-now "2026-11-05 14:12"

# Render the overlay over your wallpaper to <dir>\overlay.png and exit
Hermes.exe --fake-now "2026-11-05 14:12" --snapshot C:\temp\hermes-snap
```

`tools\make-icon.ps1` regenerates `hermes.ico` (navy) and `hermes-light.ico`
(white, used for the tray on a dark taskbar) from `Assets\verzlo-logo.jpg`.

## Project layout

```
src/Hermes/
  App.xaml(.cs)        startup, single instance, tray/hotkey wiring
  Core/                settings, strings (IS/EN), startup entry, hotkey, log, clock
  Data/                models, sample data, day progress logic, local state
  Overlay/             the three overlay windows, desktop layering, theme, edit chrome
  Tray/                notification-area icon and menu
  Ui/                  Settings window
tools/make-icon.ps1    logo to .ico conversion
```
