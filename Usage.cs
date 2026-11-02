using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIMeter;

internal enum Provider { Codex, Claude }

/// One usage window: the percent left, when it resets, and how long it lasts.
internal sealed record UsageWindow(int Remaining, DateTime? Reset, double Seconds);

/// The shortest window (five hours, or a month on Codex's free plan), the weekly one when
/// the plan has it, and the plan name the provider reports ("Free", "Pro").
internal sealed record Reading(UsageWindow Primary, UsageWindow? Secondary = null, string? Plan = null)
{
    public const double FiveHours = 5 * 3600, Week = 7 * 86_400;

    public Reading(int remaining, DateTime? reset) : this(new UsageWindow(remaining, reset, FiveHours)) { }

    public int Remaining => Primary.Remaining;
    public DateTime? Reset => Primary.Reset;
}

/// What one usage check produced: a reading, or why there isn't one (and, when the
/// provider asked us to back off, when to try again).
/// SignIn marks a missing or expired sign-in, so the panel can link to the troubleshooting guide.
internal sealed record Outcome(Reading? Reading, string? Problem = null, DateTime? RetryAt = null, bool SignIn = false);

/// Reads the sign-in that the Codex CLI or Claude Code already saved, and asks that
/// provider (and only that provider) how much of the current usage window is left.
internal static class Usage
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AI-Meter/{SettingsWindow.Version} (Windows)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static Task<Outcome> FetchAsync(Provider provider) =>
        provider == Provider.Codex ? FetchCodexAsync() : FetchClaudeAsync();

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string ConfigDir(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } dir ? dir : Path.Combine(Home, fallback);

    // MARK: Codex

    private static async Task<Outcome> FetchCodexAsync()
    {
        var auth = ReadFile(Path.Combine(ConfigDir("CODEX_HOME", ".codex"), "auth.json"));
        var (token, account) = ParseCodexAuth(auth);
        if (token is null) return new Outcome(null, Strings.Current.CodexNoSignIn, SignIn: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("ChatGPT-Account-Id", account ?? "");
        var reply = await SendAsync(request);
        return Failure(reply.Status, reply.RetryAfter, Provider.Codex, Strings.Current.CodexExpired, DateTime.Now)
            ?? Result(ParseCodexUsage(reply.Body), Provider.Codex);
    }

    public static (string? Token, string? Account) ParseCodexAuth(string? json)
    {
        var tokens = Get(Parse(json), "tokens");
        return (Text(Get(tokens, "access_token")), Text(Get(tokens, "account_id")));
    }

    /// The free plan has one 30-day window and no weekly one; paid plans have five hours and a week.
    public static Reading? ParseCodexUsage(string? json)
    {
        var root = Parse(json);
        var limit = Get(root, "rate_limit");
        UsageWindow? Window(string key, double fallback)
        {
            var window = Get(limit, key);
            if (Number(Get(window, "used_percent")) is not { } used) return null;
            var seconds = Number(Get(window, "limit_window_seconds")) is { } s and > 0 ? s : fallback;
            return new UsageWindow(Math.Max(0, 100 - (int)used), Date(Get(window, "reset_at")), seconds);
        }
        if (Window("primary_window", Reading.FiveHours) is not { } primary) return null;
        return new Reading(primary, Window("secondary_window", Reading.Week), PlanName(Text(Get(root, "plan_type"))));
    }

    // MARK: Claude

    /// Claude Code on Windows keeps its login in %USERPROFILE%\.claude\.credentials.json
    /// (or under CLAUDE_CONFIG_DIR). The usage response has `five_hour` and `seven_day`
    /// windows, each with `utilization` (percent used) and `resets_at`.
    private static async Task<Outcome> FetchClaudeAsync()
    {
        var credentialsFile = Path.Combine(ConfigDir("CLAUDE_CONFIG_DIR", ".claude"), ".credentials.json");
        var (token, expires, plan) = ParseClaudeCredentials(ReadFile(credentialsFile));
        if (token is null) return new Outcome(null, Strings.Current.ClaudeNoSignIn, SignIn: true);
        // Claude Code renews its sign-in only while it runs, and the desktop app keeps a
        // separate one, so the saved token often expires. Asking with it would just spend
        // the usage endpoint's small request allowance, so renew it first.
        if (expires is { } e && e < DateTime.Now)
        {
            var expired = new Outcome(null, string.Format(Strings.Current.ClaudeExpiredAt, Strings.Time(e)), SignIn: true);
            if (!await RenewClaudeSignInAsync()) return expired;
            (token, expires, plan) = ParseClaudeCredentials(ReadFile(credentialsFile));
            if (token is null || expires < DateTime.Now) return expired;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        var reply = await SendAsync(request);
        return Failure(reply.Status, reply.RetryAfter, Provider.Claude, Strings.Current.ClaudeExpired, DateTime.Now)
            ?? Result(ParseClaudeUsage(reply.Body) is { } reading ? reading with { Plan = PlanName(plan) } : null, Provider.Claude);
    }

    private static DateTime? lastRenewal;

    /// Runs `claude doctor`, which makes the Claude Code CLI renew its saved sign-in
    /// without sending a prompt (so it uses none of your allowance). Tried at most once
    /// every 10 minutes; returns whether it ran.
    private static async Task<bool> RenewClaudeSignInAsync()
    {
        if (lastRenewal is { } last && DateTime.Now - last < TimeSpan.FromMinutes(10)) return false;
        lastRenewal = DateTime.Now;
        if (FindClaudeCli() is not { } cli) return false;
        // cmd /c drops the outer quotes, leaving "<path>" doctor.
        var start = cli.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe", $"/c \"\"{cli}\" doctor\"")
            : new ProcessStartInfo(cli, "doctor");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WorkingDirectory = Home;
        try
        {
            using var process = Process.Start(start);
            if (process is null) return false;
            // .NET Framework has no WaitForExitAsync, and its Kill can't stop the node process
            // that claude.cmd starts, so taskkill /T ends the whole tree.
            if (!await Task.Run(() => process.WaitForExit(60_000)))
                Process.Start(new ProcessStartInfo("taskkill", $"/T /F /PID {process.Id}") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static string? FindClaudeCli()
    {
        var folders = new List<string>
        {
            Path.Combine(Home, ".local", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm")
        };
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var folder in folders)
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
                if (File.Exists(Path.Combine(folder, name))) return Path.Combine(folder, name);
        return null;
    }

    public static (string? Token, DateTime? Expires, string? Plan) ParseClaudeCredentials(string? json)
    {
        var oauth = Get(Parse(json), "claudeAiOauth");
        var token = Text(Get(oauth, "accessToken")) is { Length: > 0 } t ? t : null;
        return (token, Date(Get(oauth, "expiresAt")), Text(Get(oauth, "subscriptionType")));
    }

    /// "free" → "Free", "pro" → "Pro"; null when the provider doesn't say.
    public static string? PlanName(string? plan) =>
        string.IsNullOrEmpty(plan) ? null : char.ToUpperInvariant(plan![0]) + plan.Substring(1);

    /// Turns an unsuccessful reply into an outcome, or returns null for a 2xx reply.
    public static Outcome? Failure(int status, TimeSpan? retryAfter, Provider provider, string expired, DateTime now)
    {
        if (status is >= 200 and < 300) return null;
        if (status == 429)
        {
            // Honor Retry-After, within 1 minute to 2 hours; otherwise wait 15 minutes.
            var wait = retryAfter is { } r ? TimeSpan.FromSeconds(Math.Max(60, Math.Min(r.TotalSeconds, 7200))) : TimeSpan.FromMinutes(15);
            // No problem text: the last reading stays on screen until the wait is over.
            return new Outcome(null, RetryAt: now + wait);
        }
        if (status is 401 or 403) return new Outcome(null, expired, SignIn: true);
        return new Outcome(null, UnreachableText(provider));
    }

    /// How long until a usage window resets ("Resets in 2 h 14 min") and when ("today at 8:14 PM"),
    /// in the app's language.
    public static (string In, string At) ResetText(DateTime reset, DateTime now)
    {
        var t = Strings.Current;
        var left = reset - now;
        var span = left <= TimeSpan.Zero ? t.ResettingNow
            : left < TimeSpan.FromMinutes(1) ? t.ResetsSoon
            : left < TimeSpan.FromHours(1) ? string.Format(t.ResetsMinutes, (int)Math.Ceiling(left.TotalMinutes))
            : left < TimeSpan.FromDays(1) ? (left.Minutes > 0 ? string.Format(t.ResetsHoursMinutes, (int)left.TotalHours, left.Minutes) : string.Format(t.ResetsHours, (int)left.TotalHours))
            : left.Hours > 0 ? string.Format(t.ResetsDaysHours, (int)left.TotalDays, left.Hours) : string.Format(t.ResetsDays, (int)left.TotalDays);
        var day = (reset.Date - now.Date).Days switch { 0 => t.Today, 1 => t.Tomorrow, _ => t.DayName(reset) };
        return (span, string.Format(t.DayAtTime, day, Strings.Time(reset)));
    }

    private static string UnreachableText(Provider provider) => string.Format(Strings.Current.Unreachable, provider);

    private static Outcome Result(Reading? reading, Provider provider) =>
        reading is null ? new Outcome(null, UnreachableText(provider)) : new Outcome(reading);

    public static Reading? ParseClaudeUsage(string? json)
    {
        var root = Parse(json);
        UsageWindow? Window(string key, double seconds)
        {
            var window = Get(root, key);
            if (Number(Get(window, "utilization")) is not { } used) return null;
            return new UsageWindow(Math.Max(0, 100 - (int)Math.Round(used, MidpointRounding.AwayFromZero)), Date(Get(window, "resets_at")), seconds);
        }
        if (Window("five_hour", Reading.FiveHours) is not { } fiveHour) return null;
        return new Reading(fiveHour, Window("seven_day", Reading.Week));
    }

    // MARK: Helpers

    private static async Task<(int Status, string? Body, TimeSpan? RetryAfter)> SendAsync(HttpRequestMessage request)
    {
        try
        {
            using var response = await Http.SendAsync(request);
            var retry = response.Headers.RetryAfter is { } r
                ? r.Delta ?? (r.Date is { } d ? d - DateTimeOffset.UtcNow : null)
                : null;
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(), retry);
        }
        catch (HttpRequestException) { return (0, null, null); }
        catch (TaskCanceledException) { return (0, null, null); }
    }

    private static string? ReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json!); } catch (JsonException) { return null; }
    }

    private static JsonNode? Get(JsonNode? node, string key) => node is JsonObject obj ? obj[key] : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var number)) return number;
        if (value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    /// Accepts epoch seconds, epoch milliseconds, or an ISO 8601 string; returns local time.
    private static DateTime? Date(JsonNode? node)
    {
        if (Number(node) is { } seconds)
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds > 1e11 ? seconds : seconds * 1000)).LocalDateTime;
        if (Text(node) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date.LocalDateTime;
        return null;
    }
}
