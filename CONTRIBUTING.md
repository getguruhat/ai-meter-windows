# Contributing to AI Meter

Thanks for helping. AI Meter is a small, free app that shows how much of your Claude Code or Codex usage is left in the taskbar. Bug reports, ideas and pull requests are all welcome.

## Reporting a problem

Open an issue with the bug report form. Include your AI Meter version, your Windows version, and whether it's Claude Code or Codex. Never paste tokens or the contents of credential files.

The macOS version lives in [getguruhat/ai-meter](https://github.com/getguruhat/ai-meter).

## Building it

```
dotnet publish AIMeter.csproj -c Release -o publish
publish\AIMeter.exe --smoke-test
```

See the README for details. AI Meter reads `%USERPROFILE%\.claude\.credentials.json` for Claude Code and `%USERPROFILE%\.codex\auth.json` for Codex, so sign in with `claude` or `codex` in a terminal once before testing.

## Pull requests

- Keep a pull request to one change, and say how you tested it.
- Match the style of the code around it.
- AI Meter sends a token only to the provider it belongs to (Anthropic or OpenAI) and has no analytics. Changes that add tracking, accounts or other servers won't be merged.
- By contributing, you agree that your work is released under this repo's MIT license.

## Questions

Open an issue, or email hello@guruhat.com.
