namespace AIMeter;

/// Run with `AIMeter.exe --smoke-test`. Checks response parsing, saved settings, and the
/// tray icon drawing, writes icon previews to screenshots/, and logs to smoke-test.log.
internal static class SmokeTest
{
    private static readonly List<string> Log = [];

    public static int Run()
    {
        var failures = 0;
        void Check(string name, bool ok) { Log.Add($"{(ok ? "PASS" : "FAIL")} {name}"); if (!ok) failures++; }

        try
        {
            var codex = Usage.ParseCodexUsage("""{"rate_limit":{"primary_window":{"used_percent":41.9,"reset_at":1792543200}}}""");
            Check("Codex usage", codex?.Remaining == 59 && codex.Reset == DateTimeOffset.FromUnixTimeSeconds(1792543200).LocalDateTime);
            var codexMs = Usage.ParseCodexUsage("""{"rate_limit":{"primary_window":{"used_percent":"100","reset_at":1792543200000}}}""");
            Check("Codex usage with string percent and millisecond reset", codexMs?.Remaining == 0 && codexMs.Reset == DateTimeOffset.FromUnixTimeSeconds(1792543200).LocalDateTime);
            Check("Codex error response", Usage.ParseCodexUsage("""{"detail":"Unauthorized"}""") is null);
            var (token, account) = Usage.ParseCodexAuth("""{"tokens":{"access_token":"abc","account_id":"acct"}}""");
            Check("Codex auth file", token == "abc" && account == "acct");
            Check("Codex auth missing", Usage.ParseCodexAuth(null).Token is null && Usage.ParseCodexAuth("not json").Token is null);

            var claude = Usage.ParseClaudeUsage("""{"five_hour":{"utilization":41.5,"resets_at":"2026-10-20T21:41:00.123456+00:00"},"seven_day":{"utilization":12,"resets_at":null}}""");
            Check("Claude usage", claude?.Remaining == 58 && claude.Reset == new DateTimeOffset(2026, 10, 20, 21, 41, 0, 123, TimeSpan.Zero).AddTicks(4560).LocalDateTime);
            Check("Claude over limit", Usage.ParseClaudeUsage("""{"five_hour":{"utilization":104}}""")?.Remaining == 0);
            Check("Claude error response", Usage.ParseClaudeUsage("""{"type":"error"}""") is null && Usage.ParseClaudeUsage("[]") is null);
            var creds = Usage.ParseClaudeCredentials("""{"claudeAiOauth":{"accessToken":"tok","refreshToken":"r","expiresAt":1792543200000}}""");
            Check("Claude credentials", creds.Token == "tok" && creds.Expires == DateTimeOffset.FromUnixTimeSeconds(1792543200).LocalDateTime);
            Check("Claude credentials empty", Usage.ParseClaudeCredentials("""{"claudeAiOauth":{"accessToken":""}}""").Token is null);

            var now = new DateTime(2026, 10, 20, 18, 0, 0);
            Check("Success is not a failure", Usage.Failure(200, null, Provider.Claude, "x", now) is null);
            var limited = Usage.Failure(429, TimeSpan.FromSeconds(1597), Provider.Claude, "x", now);
            Check("429 honors Retry-After", limited?.RetryAt == now.AddSeconds(1597) && limited.Problem!.Contains("limiting"));
            Check("429 without Retry-After waits 15 minutes", Usage.Failure(429, null, Provider.Codex, "x", now)?.RetryAt == now.AddMinutes(15));
            Check("429 Retry-After capped at 2 hours", Usage.Failure(429, TimeSpan.FromHours(9), Provider.Codex, "x", now)?.RetryAt == now.AddHours(2));
            Check("401 asks to sign in", Usage.Failure(401, null, Provider.Claude, "expired", now) is { Problem: "expired", RetryAt: null, SignIn: true });
            Check("500 is temporary", Usage.Failure(500, null, Provider.Codex, "x", now) is { RetryAt: null } o && o.Problem!.Contains("Trying again"));

            Check("Reset in hours", Usage.ResetText(now.AddHours(2).AddMinutes(14), now) is ("Resets in 2 h 14 min", var at) && at.StartsWith("today at"));
            Check("Reset in minutes", Usage.ResetText(now.AddSeconds(30 * 60 + 20), now).In == "Resets in 31 min");
            Check("Reset under a minute", Usage.ResetText(now.AddSeconds(30), now).In == "Resets in under a minute");
            Check("Reset tomorrow", Usage.ResetText(now.AddHours(22), now) is ("Resets in 22 h", var at2) && at2.StartsWith("tomorrow at"));
            Check("Reset in days", Usage.ResetText(now.AddDays(3).AddHours(5), now).In == "Resets in 3 d 5 h");
            Check("Reset passed", Usage.ResetText(now.AddMinutes(-1), now).In == "Resetting now");

            var folder = Path.Combine(Path.GetTempPath(), "AIMeter-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = Settings.Load(folder);
                settings.Provider = Provider.Claude;
                settings.Store(Provider.Claude, new Reading(37, new DateTime(2026, 10, 20, 21, 41, 0)), now);
                var loaded = Settings.Load(folder);
                var (cached, cachedAt) = loaded.Cached(Provider.Claude);
                Check("Settings round trip", loaded.Provider == Provider.Claude && cached?.Remaining == 37 && cachedAt == now && loaded.Cached(Provider.Codex).Reading is null);
            }
            finally { Directory.Delete(folder, true); }

            Directory.CreateDirectory("screenshots");
            foreach (var size in new[] { 16, 20, 24, 32 })
                foreach (var remaining in new int?[] { null, 5, 41, 100 })
                {
                    foreach (var light in new[] { true, false })
                    {
                        using var bitmap = MeterIcon.Draw(remaining, size, light);
                        var name = $"{size}-{remaining?.ToString() ?? "none"}-{(light ? "light" : "dark")}";
                        bitmap.Save($"screenshots/icon-{name}.png");
                        // Clear background, and the text drawn in black (light taskbar) or white (dark).
                        var ink = Enumerable.Range(0, size * size).Select(i => bitmap.GetPixel(i % size, i / size)).Where(p => p.A > 200).ToList();
                        Check($"Icon {name}", bitmap.GetPixel(0, 0).A == 0 && ink.Count > 0 && ink.All(p => light ? p.R <= 5 : p.R >= 250));
                    }
                }
            using (var icon = MeterIcon.Create(41)) Check("Tray icon handle", icon.Width > 0);

            foreach (var (themeName, light) in new[] { ("light", true), ("dark", false) })
                foreach (var scale in new[] { 1f, 1.5f })
                {
                    using var bitmap = TaskbarLabel.Draw("AI 79%", scale, (int)(48 * scale), light);
                    var name = $"{themeName}-{scale * 100:0}";
                    bitmap.Save($"screenshots/taskbar-label-{name}.png");
                    var ink = Enumerable.Range(0, bitmap.Width * bitmap.Height).Select(i => bitmap.GetPixel(i % bitmap.Width, i / bitmap.Width)).Where(p => p.A > 200).ToList();
                    Check($"Taskbar label {name}", bitmap.GetPixel(0, 0).A == 1 && bitmap.Width > bitmap.Height && ink.Count > 0 && ink.All(p => light ? p.R <= 5 : p.R >= 250));
                }

            using var panel = new MeterPanel(_ => { }, () => { }, () => { }, () => { }, () => { }, () => { });
            var previews = new (string Name, PanelState State)[]
            {
                ("reading", new PanelState(Provider.Claude, new Reading(58, DateTime.Now.AddHours(2).AddMinutes(14)), null)),
                ("low", new PanelState(Provider.Codex, new Reading(12, DateTime.Now.AddDays(2)), "Codex is limiting usage checks. Next check at 3:41 PM.")),
                ("signed-out", new PanelState(Provider.Codex, null, "No Codex sign-in found. Run codex and sign in with ChatGPT.", SignIn: true))
            };
            foreach (var (name, previewState) in previews)
                foreach (var (themeName, look) in new[] { ("light", Theme.Light), ("dark", Theme.Dark) })
                {
                    using var bitmap = panel.Preview(previewState, look);
                    bitmap.Save($"screenshots/panel-{name}-{themeName}.png");
                    Check($"Panel {name} {themeName}", bitmap.Height > 100 && bitmap.GetPixel(3, bitmap.Height / 2).ToArgb() == look.Back.ToArgb());
                }

            Check("Version", AboutWindow.Version.Count(c => c == '.') == 2);
            using var about = new AboutWindow(() => { });
            foreach (var (themeName, look) in new[] { ("light", Theme.Light), ("dark", Theme.Dark) })
            {
                using var bitmap = about.Preview(look);
                bitmap.Save($"screenshots/about-{themeName}.png");
                // The embedded app icon is drawn at the top center.
                Check($"About {themeName}", bitmap.Height > 300 && bitmap.GetPixel(3, 3).ToArgb() == look.Back.ToArgb()
                    && bitmap.GetPixel(bitmap.Width / 2, (int)(98 * bitmap.Height / 430f)).ToArgb() != look.Back.ToArgb());
            }
        }
        catch (Exception error)
        {
            Log.Add("FAIL unexpected " + error);
            failures++;
        }

        Log.Add(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        File.WriteAllLines("smoke-test.log", Log);
        return failures == 0 ? 0 : 1;
    }
}
