using System.Globalization;

namespace AIMeter;

/// The app's text in the Windows display language, with English for any other language.
/// "AI Meter", the provider names, and the "AI 79%" label are the same in every language.
/// Any property a translation doesn't set stays English. Templates take {0}, {1} like string.Format.
internal sealed record Strings
{
    public static void Use(CultureInfo culture) => Current = For(culture);

    /// Picks the closest translation: "de-AT" uses "de", "zh-CN" uses "zh-Hans".
    /// Traditional Chinese has no translation yet, so it falls back to English.
    public static Strings For(CultureInfo culture)
    {
        for (var c = culture; !Equals(c, CultureInfo.InvariantCulture); c = c.Parent)
            if (All.TryGetValue(c.Name, out var strings)) return strings with { Culture = culture };
        return English with { Culture = culture };
    }

    /// For day and month names; the time itself follows the regional format.
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    // Panel
    public string Remaining { get; init; } = "remaining";
    public string ResetNotReported { get; init; } = "Reset time not reported";
    public string Unavailable { get; init; } = "AI usage unavailable";
    public string UnavailableHint { get; init; } = "Reset time shows once usage can be read.";
    public string Troubleshooting { get; init; } = "Open the troubleshooting guide ›";
    public string Refresh { get; init; } = "Refresh";
    public string About { get; init; } = "About AI Meter";
    public string Support { get; init; } = "Support AI Meter";
    public string Quit { get; init; } = "Quit";

    // Right-click menu
    public string RefreshNow { get; init; } = "Refresh now";
    public string ShowOnTaskbar { get; init; } = "Show AI % on the taskbar";
    public string StartWithWindows { get; init; } = "Start with Windows";
    public string StartupBlocked { get; init; } = "Windows has AI Meter turned off at startup. Turn it on in Settings › Apps › Startup.";

    // About window
    public string Version { get; init; } = "Version {0}";
    public string Tagline { get; init; } = "A GuruHat open-source tool";
    public string Thanks { get; init; } = "Thank you for supporting this project!";

    // Taskbar tooltip: "AI Meter · Claude: 58% left, resets today at 8:14 PM"
    public string TipUnavailable { get; init; } = "unavailable";
    public string TipLeft { get; init; } = "{0}% left";
    public string TipResets { get; init; } = "resets {0}";

    // Problems
    public string CodexNoSignIn { get; init; } = "No Codex sign-in found. Run codex and sign in with ChatGPT.";
    public string CodexExpired { get; init; } = "Codex sign-in expired. Run codex once to renew it.";
    public string ClaudeNoSignIn { get; init; } = "No Claude Code sign-in found in a terminal. Signing in to the Claude app isn't enough.";
    public string ClaudeExpiredAt { get; init; } = "Claude sign-in expired at {0}. Run claude in a terminal once to renew it.";
    public string ClaudeExpired { get; init; } = "Claude sign-in expired. Use Claude Code once to renew it.";
    public string Unreachable { get; init; } = "Couldn't get usage from {0}. Trying again in 5 minutes.";
    public string RateLimited { get; init; } = "{0} is limiting usage checks. Next check at {1}.";

    // Reset time
    public string ResettingNow { get; init; } = "Resetting now";
    public string ResetsSoon { get; init; } = "Resets in under a minute";
    public string ResetsMinutes { get; init; } = "Resets in {0} min";
    public string ResetsHours { get; init; } = "Resets in {0} h";
    public string ResetsHoursMinutes { get; init; } = "Resets in {0} h {1} min";
    public string ResetsDays { get; init; } = "Resets in {0} d";
    public string ResetsDaysHours { get; init; } = "Resets in {0} d {1} h";
    public string Today { get; init; } = "today";
    public string Tomorrow { get; init; } = "tomorrow";
    /// A date format for days after tomorrow, such as "Tue, Nov 17".
    public string Day { get; init; } = "ddd, MMM d";
    public string DayAtTime { get; init; } = "{0} at {1}";

    /// "8:14 PM" or "20:14", as set in Windows' regional format.
    public static string Time(DateTime time) => time.ToString("t", CultureInfo.CurrentCulture);

    public string DayName(DateTime date) => date.ToString(Day, Culture);

    private static readonly Strings English = new();

    private static readonly Dictionary<string, Strings> All = new()
    {
        ["es"] = new()
        {
            Remaining = "restante",
            ResetNotReported = "Hora de reinicio no disponible",
            Unavailable = "Uso de IA no disponible",
            UnavailableHint = "La hora de reinicio aparecerá cuando se pueda leer el uso.",
            Troubleshooting = "Abrir la guía de solución de problemas ›",
            Refresh = "Actualizar",
            About = "Acerca de AI Meter",
            Support = "Apoyar AI Meter",
            Quit = "Salir",
            RefreshNow = "Actualizar ahora",
            ShowOnTaskbar = "Mostrar AI % en la barra de tareas",
            StartWithWindows = "Iniciar con Windows",
            StartupBlocked = "Windows tiene AI Meter desactivado al inicio. Actívalo en Configuración › Aplicaciones › Inicio.",
            Version = "Versión {0}",
            Tagline = "Una herramienta de código abierto de GuruHat",
            Thanks = "¡Gracias por apoyar este proyecto!",
            TipUnavailable = "no disponible",
            TipLeft = "{0}% restante",
            TipResets = "se reinicia {0}",
            CodexNoSignIn = "No se encontró ningún inicio de sesión de Codex. Ejecuta codex e inicia sesión con ChatGPT.",
            CodexExpired = "El inicio de sesión de Codex caducó. Ejecuta codex una vez para renovarlo.",
            ClaudeNoSignIn = "No se encontró ningún inicio de sesión de Claude Code en una terminal. Iniciar sesión en la app de Claude no es suficiente.",
            ClaudeExpiredAt = "El inicio de sesión de Claude caducó a las {0}. Ejecuta claude una vez en una terminal para renovarlo.",
            ClaudeExpired = "El inicio de sesión de Claude caducó. Usa Claude Code una vez para renovarlo.",
            Unreachable = "No se pudo obtener el uso de {0}. Se reintentará en 5 minutos.",
            RateLimited = "{0} está limitando las consultas de uso. Próxima consulta a las {1}.",
            ResettingNow = "Reiniciando ahora",
            ResetsSoon = "Se reinicia en menos de un minuto",
            ResetsMinutes = "Se reinicia en {0} min",
            ResetsHours = "Se reinicia en {0} h",
            ResetsHoursMinutes = "Se reinicia en {0} h {1} min",
            ResetsDays = "Se reinicia en {0} d",
            ResetsDaysHours = "Se reinicia en {0} d {1} h",
            Today = "hoy",
            Tomorrow = "mañana",
            Day = "ddd d MMM",
            DayAtTime = "{0} a las {1}"
        },
        ["de"] = new()
        {
            Remaining = "verbleibend",
            ResetNotReported = "Zeitpunkt des Zurücksetzens unbekannt",
            Unavailable = "KI-Nutzung nicht verfügbar",
            UnavailableHint = "Der Zeitpunkt des Zurücksetzens erscheint, sobald die Nutzung gelesen werden kann.",
            Troubleshooting = "Anleitung zur Fehlerbehebung öffnen ›",
            Refresh = "Aktualisieren",
            About = "Über AI Meter",
            Support = "AI Meter unterstützen",
            Quit = "Beenden",
            RefreshNow = "Jetzt aktualisieren",
            ShowOnTaskbar = "AI % in der Taskleiste anzeigen",
            StartWithWindows = "Mit Windows starten",
            StartupBlocked = "Windows hat AI Meter beim Start deaktiviert. Aktivieren Sie es unter Einstellungen › Apps › Autostart.",
            Version = "Version {0}",
            Tagline = "Ein Open-Source-Tool von GuruHat",
            Thanks = "Vielen Dank für Ihre Unterstützung!",
            TipUnavailable = "nicht verfügbar",
            TipLeft = "{0} % übrig",
            TipResets = "wird {0} zurückgesetzt",
            CodexNoSignIn = "Keine Codex-Anmeldung gefunden. Führen Sie codex aus und melden Sie sich mit ChatGPT an.",
            CodexExpired = "Codex-Anmeldung abgelaufen. Führen Sie codex einmal aus, um sie zu erneuern.",
            ClaudeNoSignIn = "Keine Claude Code-Anmeldung in einem Terminal gefunden. Die Anmeldung in der Claude-App reicht nicht aus.",
            ClaudeExpiredAt = "Claude-Anmeldung um {0} abgelaufen. Führen Sie claude einmal in einem Terminal aus, um sie zu erneuern.",
            ClaudeExpired = "Claude-Anmeldung abgelaufen. Verwenden Sie Claude Code einmal, um sie zu erneuern.",
            Unreachable = "Nutzung von {0} konnte nicht abgerufen werden. Neuer Versuch in 5 Minuten.",
            RateLimited = "{0} begrenzt die Nutzungsabfragen. Nächste Abfrage um {1}.",
            ResettingNow = "Wird jetzt zurückgesetzt",
            ResetsSoon = "Zurücksetzung in weniger als einer Minute",
            ResetsMinutes = "Zurücksetzung in {0} Min.",
            ResetsHours = "Zurücksetzung in {0} Std.",
            ResetsHoursMinutes = "Zurücksetzung in {0} Std. {1} Min.",
            ResetsDays = "Zurücksetzung in {0} Tg.",
            ResetsDaysHours = "Zurücksetzung in {0} Tg. {1} Std.",
            Today = "heute",
            Tomorrow = "morgen",
            Day = "ddd, d. MMM",
            DayAtTime = "{0} um {1}"
        },
        ["fr"] = new()
        {
            Remaining = "restant",
            ResetNotReported = "Heure de réinitialisation non communiquée",
            Unavailable = "Utilisation de l'IA indisponible",
            UnavailableHint = "L'heure de réinitialisation s'affichera dès que l'utilisation pourra être lue.",
            Troubleshooting = "Ouvrir le guide de dépannage ›",
            Refresh = "Actualiser",
            About = "À propos d'AI Meter",
            Support = "Soutenir AI Meter",
            Quit = "Quitter",
            RefreshNow = "Actualiser maintenant",
            ShowOnTaskbar = "Afficher AI % dans la barre des tâches",
            StartWithWindows = "Démarrer avec Windows",
            StartupBlocked = "Windows a désactivé AI Meter au démarrage. Activez-le dans Paramètres › Applications › Démarrage.",
            Version = "Version {0}",
            Tagline = "Un outil open source de GuruHat",
            Thanks = "Merci de soutenir ce projet !",
            TipUnavailable = "indisponible",
            TipLeft = "{0} % restant",
            TipResets = "réinitialisation {0}",
            CodexNoSignIn = "Aucune connexion Codex trouvée. Lancez codex et connectez-vous avec ChatGPT.",
            CodexExpired = "La connexion Codex a expiré. Lancez codex une fois pour la renouveler.",
            ClaudeNoSignIn = "Aucune connexion Claude Code trouvée dans un terminal. Se connecter à l'app Claude ne suffit pas.",
            ClaudeExpiredAt = "La connexion Claude a expiré à {0}. Lancez claude une fois dans un terminal pour la renouveler.",
            ClaudeExpired = "La connexion Claude a expiré. Utilisez Claude Code une fois pour la renouveler.",
            Unreachable = "Impossible d'obtenir l'utilisation de {0}. Nouvel essai dans 5 minutes.",
            RateLimited = "{0} limite les vérifications d'utilisation. Prochaine vérification à {1}.",
            ResettingNow = "Réinitialisation en cours",
            ResetsSoon = "Réinitialisation dans moins d'une minute",
            ResetsMinutes = "Réinitialisation dans {0} min",
            ResetsHours = "Réinitialisation dans {0} h",
            ResetsHoursMinutes = "Réinitialisation dans {0} h {1} min",
            ResetsDays = "Réinitialisation dans {0} j",
            ResetsDaysHours = "Réinitialisation dans {0} j {1} h",
            Today = "aujourd'hui",
            Tomorrow = "demain",
            Day = "ddd d MMM",
            DayAtTime = "{0} à {1}"
        },
        ["pt"] = new()
        {
            Remaining = "restante",
            ResetNotReported = "Horário de redefinição não informado",
            Unavailable = "Uso de IA indisponível",
            UnavailableHint = "O horário de redefinição aparece quando o uso puder ser lido.",
            Troubleshooting = "Abrir o guia de solução de problemas ›",
            Refresh = "Atualizar",
            About = "Sobre o AI Meter",
            Support = "Apoiar o AI Meter",
            Quit = "Sair",
            RefreshNow = "Atualizar agora",
            ShowOnTaskbar = "Mostrar AI % na barra de tarefas",
            StartWithWindows = "Iniciar com o Windows",
            StartupBlocked = "O Windows desativou o AI Meter na inicialização. Ative-o em Configurações › Aplicativos › Inicialização.",
            Version = "Versão {0}",
            Tagline = "Uma ferramenta de código aberto da GuruHat",
            Thanks = "Obrigado por apoiar este projeto!",
            TipUnavailable = "indisponível",
            TipLeft = "{0}% restante",
            TipResets = "redefine {0}",
            CodexNoSignIn = "Nenhum login do Codex encontrado. Execute codex e entre com o ChatGPT.",
            CodexExpired = "O login do Codex expirou. Execute codex uma vez para renová-lo.",
            ClaudeNoSignIn = "Nenhum login do Claude Code encontrado em um terminal. Entrar no app do Claude não é suficiente.",
            ClaudeExpiredAt = "O login do Claude expirou às {0}. Execute claude uma vez em um terminal para renová-lo.",
            ClaudeExpired = "O login do Claude expirou. Use o Claude Code uma vez para renová-lo.",
            Unreachable = "Não foi possível obter o uso do {0}. Nova tentativa em 5 minutos.",
            RateLimited = "O {0} está limitando as verificações de uso. Próxima verificação às {1}.",
            ResettingNow = "Redefinindo agora",
            ResetsSoon = "Redefine em menos de um minuto",
            ResetsMinutes = "Redefine em {0} min",
            ResetsHours = "Redefine em {0} h",
            ResetsHoursMinutes = "Redefine em {0} h {1} min",
            ResetsDays = "Redefine em {0} d",
            ResetsDaysHours = "Redefine em {0} d {1} h",
            Today = "hoje",
            Tomorrow = "amanhã",
            Day = "ddd, d 'de' MMM",
            DayAtTime = "{0} às {1}"
        },
        ["ja"] = new()
        {
            Remaining = "残り",
            ResetNotReported = "リセット時刻は不明です",
            Unavailable = "AI 使用量を取得できません",
            UnavailableHint = "使用状況を読み取れるとリセット時刻が表示されます。",
            Troubleshooting = "トラブルシューティング ガイドを開く ›",
            Refresh = "更新",
            About = "AI Meter について",
            Support = "AI Meter を支援",
            Quit = "終了",
            RefreshNow = "今すぐ更新",
            ShowOnTaskbar = "タスク バーに AI % を表示",
            StartWithWindows = "Windows の起動時に開始",
            StartupBlocked = "Windows のスタートアップで AI Meter がオフになっています。[設定] › [アプリ] › [スタートアップ] でオンにしてください。",
            Version = "バージョン {0}",
            Tagline = "GuruHat のオープンソース ツール",
            Thanks = "このプロジェクトを支援していただきありがとうございます！",
            TipUnavailable = "取得できません",
            TipLeft = "残り {0}%",
            TipResets = "リセット: {0}",
            CodexNoSignIn = "Codex のサインインが見つかりません。codex を実行して ChatGPT でサインインしてください。",
            CodexExpired = "Codex のサインインの有効期限が切れました。codex を一度実行して更新してください。",
            ClaudeNoSignIn = "ターミナルでの Claude Code のサインインが見つかりません。Claude アプリへのサインインだけでは使えません。",
            ClaudeExpiredAt = "Claude のサインインは {0} に期限切れになりました。ターミナルで claude を一度実行して更新してください。",
            ClaudeExpired = "Claude のサインインの有効期限が切れました。Claude Code を一度使用して更新してください。",
            Unreachable = "{0} から使用状況を取得できませんでした。5 分後に再試行します。",
            RateLimited = "{0} が使用状況の確認を制限しています。次の確認は {1} です。",
            ResettingNow = "リセット中",
            ResetsSoon = "1 分以内にリセット",
            ResetsMinutes = "{0} 分後にリセット",
            ResetsHours = "{0} 時間後にリセット",
            ResetsHoursMinutes = "{0} 時間 {1} 分後にリセット",
            ResetsDays = "{0} 日後にリセット",
            ResetsDaysHours = "{0} 日 {1} 時間後にリセット",
            Today = "今日",
            Tomorrow = "明日",
            Day = "M月d日 (ddd)",
            DayAtTime = "{0} {1}"
        },
        ["zh-Hans"] = new()
        {
            Remaining = "剩余",
            ResetNotReported = "未提供重置时间",
            Unavailable = "无法获取 AI 用量",
            UnavailableHint = "读取到用量后将显示重置时间。",
            Troubleshooting = "打开故障排除指南 ›",
            Refresh = "刷新",
            About = "关于 AI Meter",
            Support = "支持 AI Meter",
            Quit = "退出",
            RefreshNow = "立即刷新",
            ShowOnTaskbar = "在任务栏显示 AI %",
            StartWithWindows = "开机时启动",
            StartupBlocked = "Windows 已在启动时关闭 AI Meter。请在“设置 › 应用 › 启动”中将其打开。",
            Version = "版本 {0}",
            Tagline = "GuruHat 开源工具",
            Thanks = "感谢你对本项目的支持！",
            TipUnavailable = "不可用",
            TipLeft = "剩余 {0}%",
            TipResets = "{0}重置",
            CodexNoSignIn = "未找到 Codex 登录信息。请运行 codex 并使用 ChatGPT 登录。",
            CodexExpired = "Codex 登录已过期。请运行一次 codex 以续期。",
            ClaudeNoSignIn = "未在终端中找到 Claude Code 登录信息。仅登录 Claude 应用是不够的。",
            ClaudeExpiredAt = "Claude 登录已于 {0} 过期。请在终端中运行一次 claude 以续期。",
            ClaudeExpired = "Claude 登录已过期。请使用一次 Claude Code 以续期。",
            Unreachable = "无法从 {0} 获取用量。将在 5 分钟后重试。",
            RateLimited = "{0} 正在限制用量查询。下次查询时间：{1}。",
            ResettingNow = "正在重置",
            ResetsSoon = "不到 1 分钟后重置",
            ResetsMinutes = "{0} 分钟后重置",
            ResetsHours = "{0} 小时后重置",
            ResetsHoursMinutes = "{0} 小时 {1} 分钟后重置",
            ResetsDays = "{0} 天后重置",
            ResetsDaysHours = "{0} 天 {1} 小时后重置",
            Today = "今天",
            Tomorrow = "明天",
            Day = "M月d日 ddd",
            DayAtTime = "{0} {1}"
        },
        ["ko"] = new()
        {
            Remaining = "남음",
            ResetNotReported = "초기화 시간이 제공되지 않음",
            Unavailable = "AI 사용량을 확인할 수 없음",
            UnavailableHint = "사용량을 읽을 수 있으면 초기화 시간이 표시됩니다.",
            Troubleshooting = "문제 해결 가이드 열기 ›",
            Refresh = "새로 고침",
            About = "AI Meter 정보",
            Support = "AI Meter 후원하기",
            Quit = "종료",
            RefreshNow = "지금 새로 고침",
            ShowOnTaskbar = "작업 표시줄에 AI % 표시",
            StartWithWindows = "Windows 시작 시 실행",
            StartupBlocked = "Windows 시작 시 AI Meter가 꺼져 있습니다. 설정 › 앱 › 시작 프로그램에서 켜세요.",
            Version = "버전 {0}",
            Tagline = "GuruHat 오픈 소스 도구",
            Thanks = "이 프로젝트를 후원해 주셔서 감사합니다!",
            TipUnavailable = "사용할 수 없음",
            TipLeft = "{0}% 남음",
            TipResets = "{0} 초기화",
            CodexNoSignIn = "Codex 로그인 정보를 찾을 수 없습니다. codex를 실행하고 ChatGPT로 로그인하세요.",
            CodexExpired = "Codex 로그인이 만료되었습니다. codex를 한 번 실행하여 갱신하세요.",
            ClaudeNoSignIn = "터미널에서 Claude Code 로그인 정보를 찾을 수 없습니다. Claude 앱에 로그인하는 것만으로는 충분하지 않습니다.",
            ClaudeExpiredAt = "Claude 로그인이 {0}에 만료되었습니다. 터미널에서 claude를 한 번 실행하여 갱신하세요.",
            ClaudeExpired = "Claude 로그인이 만료되었습니다. Claude Code를 한 번 사용하여 갱신하세요.",
            Unreachable = "{0}에서 사용량을 가져오지 못했습니다. 5분 후에 다시 시도합니다.",
            RateLimited = "{0}에서 사용량 확인을 제한하고 있습니다. 다음 확인: {1}",
            ResettingNow = "지금 초기화 중",
            ResetsSoon = "1분 이내 초기화",
            ResetsMinutes = "{0}분 후 초기화",
            ResetsHours = "{0}시간 후 초기화",
            ResetsHoursMinutes = "{0}시간 {1}분 후 초기화",
            ResetsDays = "{0}일 후 초기화",
            ResetsDaysHours = "{0}일 {1}시간 후 초기화",
            Today = "오늘",
            Tomorrow = "내일",
            Day = "M월 d일 (ddd)",
            DayAtTime = "{0} {1}"
        }
    };

    /// The language the app shows; the smoke test switches it with <see cref="Use"/>.
    /// Declared after the tables it reads, since static fields start in order.
    public static Strings Current { get; private set; } = For(CultureInfo.CurrentUICulture);
}
