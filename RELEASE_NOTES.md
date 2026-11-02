AI Meter shows how much of your Claude Code or Codex usage is left, right on the Windows taskbar, along with when it resets.

**Install**
1. Download `AI-Meter-1.2.0-Windows-Setup.exe` below and run it. No administrator rights needed.
2. If Windows SmartScreen says it protected your PC, choose **More info › Run anyway**. The installer isn't code-signed yet.
3. Click "AI %" on the taskbar and pick **Claude** or **Codex**.

**What's new in 1.2.0**
- A Settings window, opened with the gear in the panel: turn **Start with Windows** on or off, and see the version and the Support AI Meter link. It replaces the About and heart buttons
- AI Meter asks once, on first launch, whether to start with Windows
- If Windows' own Startup apps settings have AI Meter turned off, Settings says so and links straight there
- Fixes AI Meter closing right after it opens once it had a saved reading, most often in Spanish, German, French, and other languages with longer text. The tray tooltip was longer than Windows allows
- The taskbar label uses your language's abbreviation: "IA 79%" in Spanish, French, and Portuguese, and "KI 79%" in German

**What was new in 1.1.0**
- See all your limits: the panel shows the 5-hour limit and the weekly limit, each with its own color bar and reset time
- Codex's free plan shows its monthly limit, named as such, instead of a 5-hour one
- Your plan (Free, Plus, Pro, Max) shows next to the first limit
- A new, cleaner panel that matches the Mac version: the Claude/Codex switch and buttons on one row, and one card with your limits
- No more "limiting usage checks" warning: when a provider asks AI Meter to slow down, it quietly keeps the last reading, and remembers the wait even if you restart it

**What was new in 1.0.4**
- AI Meter now speaks your language: it follows your Windows display language in Spanish, German, French, Portuguese, Japanese, Simplified Chinese, and Korean, and uses English for any other language
- Times follow your Windows regional format, such as 15:40 or 3:40 PM

**What was new in 1.0.3**
- The About button opens an About AI Meter window, like the macOS version: the app icon, version, and a Support AI Meter link
- The "AI 79%" taskbar text is a little smaller (15 px), in the same font as the clock, so it sits more naturally next to it

**What was new in 1.0.2**
- Larger "AI 79%" text on the taskbar, about 30% bigger, so it's easier to read at a glance
- When AI Meter can't find your Claude Code or Codex sign-in, the panel now links straight to the [troubleshooting guide](https://guruhat.com/apps/ai-meter/troubleshooting/), which walks you through signing in from a terminal
- Clearer message: signing in to the Claude app isn't enough, because AI Meter reads the sign-in saved by `claude` in a terminal

**What was new in 1.0.1**
- A much smaller download: about 2 MB instead of 175 MB. AI Meter now uses the .NET Framework that comes with Windows instead of bundling its own runtime

**What's in AI Meter**
- "AI 79%" right on the taskbar, next to the system tray, in thin text that follows your light or dark taskbar
- Click it to open the usage panel: switch between Claude and Codex, see what's left with a color bar, and see when your usage resets, both how long is left and the exact time
- Right-click for Refresh now, Show AI % on the taskbar, Start with Windows, and Quit
- When the taskbar text is off, or the taskbar is vertical or auto-hidden, the percentage shows as a tray icon instead. Windows 11 may tuck new tray icons behind the **^** arrow; drag AI Meter onto the taskbar to keep it visible
- Follows Windows light or dark mode
- Refreshes every five minutes, backs off when a provider rate-limits it, and keeps showing the last reading if a check fails

Requires Windows 10 or 11 (x64). Free, MIT-licensed, no account or analytics.

More at https://guruhat.com/apps/ai-meter/
