# AI Meter for Windows

**Your remaining Claude Code or Codex usage, right in the Windows system tray.**

AI Meter is a free, open-source GuruHat tray app. It shows how much of your current usage window is left as "AI 79%" on the taskbar, next to the system tray (black on a light taskbar, white on a dark one) and shows when the window resets. It refreshes every five minutes using the sign-in you already have.

- **Claude:** the five-hour window, using the Claude Code sign-in in `%USERPROFILE%\.claude\.credentials.json` (or `CLAUDE_CONFIG_DIR`).
- **Codex:** your current allowance, using the ChatGPT sign-in the Codex CLI saves in `%USERPROFILE%\.codex\auth.json` (or `CODEX_HOME`).

Website: https://guruhat.com/apps/ai-meter/ · macOS version: https://github.com/getguruhat/ai-meter

## Download

Get `AI-Meter-<version>-Windows-Setup.exe` from the [latest release](../../releases/latest). The installer runs without administrator rights and installs to `%LOCALAPPDATA%\Programs`.

The installer isn't code-signed yet. If SmartScreen says it protected your PC, choose **More info › Run anyway**.

Windows 11 may hide new tray icons behind the **^** arrow. Drag AI Meter onto the taskbar, or turn it on under **Settings › Personalization › Taskbar › Other system tray icons**.

## Using it

Click the tray icon to open the usage panel: switch between **Claude** and **Codex**, see the exact percentage, how long until the window resets and at what time, and refresh, open About, or quit. It follows your Windows light or dark mode. Right-click it for **Refresh now**, **Show AI % on the taskbar**, **Start with Windows**, and **Quit**. With the taskbar text off, or when the taskbar is vertical or auto-hidden, AI Meter shows the percentage as a tray icon instead. If it shows **–**, AI Meter couldn't find a sign-in for that provider or the provider changed its usage endpoint. Sign in to Claude Code or the Codex CLI ([troubleshooting guide](https://guruhat.com/apps/ai-meter/troubleshooting/)), then refresh. When the problem is a missing or expired sign-in, the panel shows a **Troubleshooting** link to the same guide.

## Privacy

Your token is sent only to the provider it belongs to: `https://api.anthropic.com/api/oauth/usage` for Claude and `https://chatgpt.com/backend-api/wham/usage` for Codex. AI Meter has no account, analytics, or GuruHat server. Settings and the last reading are stored in `%LOCALAPPDATA%\GuruHat\AIMeter`.

## Build

Requires the .NET 8 SDK on Windows. The app targets .NET Framework 4.8, which comes with Windows 10 and 11, so the build is a small folder (`AIMeter.exe`, its `.config`, and the System.Text.Json DLLs) rather than a self-contained runtime.

```powershell
dotnet publish AIMeter.csproj -c Release -o publish
publish\AIMeter.exe --smoke-test   # writes smoke-test.log and icon previews to screenshots\
```

GitHub Actions builds the installer with Inno Setup on every push. Pushing a `v*` tag publishes a release.

### Microsoft Store (MSIX)

The same build also makes `AI-Meter-<version>-x64.msix` (the **AI-Meter-Windows-msix** artifact) from `msix/AppxManifest.xml`, then test-installs it on the runner. Upload that file to Partner Center as is: the Store signs it when it publishes. The `Identity` and `PublisherDisplayName` in the manifest must match Partner Center's **Product identity** page. In the packaged app, **Start with Windows** uses the package's startup task instead of the registry, and Windows removes the app's settings when it's uninstalled.

## License

MIT. AI Meter is an independent project and is not affiliated with Anthropic or OpenAI.
