using Microsoft.Extensions.Configuration;

namespace WillscotAutomation.Config;

/// <summary>
/// Provides strongly-typed access to appsettings.json configuration.
/// Environment is determined by TEST_ENV environment variable (QA | Stage | Prod).
/// </summary>
public static class ConfigReader
{
    private static readonly IConfiguration Configuration;
    private static readonly string _env;

    static ConfigReader()
    {
        _env = Environment.GetEnvironmentVariable("TEST_ENV") ?? "QA";

        Configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    public static string BaseUrl =>
        Configuration[$"{_env}:TestSettings:BaseUrl"]
        ?? throw new InvalidOperationException("TestSettings:BaseUrl is not configured.");

    public static string BrowserType =>
        Environment.GetEnvironmentVariable("BROWSER")
        ?? Configuration[$"{_env}:TestSettings:Browser"]
        ?? "chromium";

    public static bool Headless =>
        bool.Parse(
            Environment.GetEnvironmentVariable("HEADLESS")
            ?? Configuration[$"{_env}:TestSettings:Headless"]
            ?? "true");

    public static int DefaultTimeout =>
        int.Parse(Configuration[$"{_env}:TestSettings:DefaultTimeout"] ?? "30000");

    public static int NavigationTimeout =>
        int.Parse(Configuration[$"{_env}:TestSettings:NavigationTimeout"] ?? "60000");

    public static float SlowMo =>
        float.Parse(Configuration[$"{_env}:TestSettings:SlowMo"] ?? "0");

    public static int ViewportWidth =>
        int.Parse(Configuration[$"{_env}:TestSettings:ViewportWidth"] ?? "1920");

    public static int ViewportHeight =>
        int.Parse(Configuration[$"{_env}:TestSettings:ViewportHeight"] ?? "1080");

    public static int PageLoadThresholdMs =>
        int.Parse(Configuration[$"{_env}:TestSettings:PageLoadThresholdMs"] ?? "30000");

    public static int RetryCount =>
        int.Parse(Configuration[$"{_env}:TestSettings:RetryCount"] ?? "1");

    public static bool ScreenshotOnFailure =>
        bool.Parse(Configuration[$"{_env}:TestSettings:ScreenshotOnFailure"] ?? "true");

    public static string LogLevel =>
        Configuration[$"{_env}:TestSettings:LogLevel"] ?? "Information";

    /// <summary>Email notification settings — all values from the active environment's "EmailSettings" section.</summary>
    public static EmailSettings EmailSettings =>
        Configuration.GetSection($"{_env}:EmailSettings").Get<EmailSettings>()
        ?? new EmailSettings();

    /// <summary>
    /// Teams notification settings — all values from the active environment's "TeamsSettings" section.
    /// WebhookUrl can also be supplied via the TEAMS_WEBHOOK_URL environment variable,
    /// which takes precedence over the JSON config value.
    /// </summary>
    public static TeamsSettings TeamsSettings
    {
        get
        {
            var enableTeams = bool.Parse(Configuration[$"{_env}:TeamsSettings:EnableTeams"] ?? "false");
            var webhookUrl  = Configuration[$"{_env}:TeamsSettings:WebhookUrl"]     ?? "";
            var projectName = Configuration[$"{_env}:TeamsSettings:ProjectName"]    ?? "WillScot Homepage Automation";
            var reportUrl   = Configuration[$"{_env}:TeamsSettings:AllureReportUrl"] ?? "";
            var buildUrl    = Configuration[$"{_env}:TeamsSettings:BuildUrl"]       ?? "";

            var settings = new TeamsSettings
            {
                EnableTeams     = enableTeams,
                WebhookUrl      = webhookUrl,
                ProjectName     = projectName,
                AllureReportUrl = reportUrl,
                BuildUrl        = buildUrl
            };

            var envUrl = Environment.GetEnvironmentVariable("TEAMS_WEBHOOK_URL");
            if (!string.IsNullOrWhiteSpace(envUrl))
                settings = settings with { WebhookUrl = envUrl, EnableTeams = true };

            return settings;
        }
    }

    /// <summary>
    /// Zephyr Essential + Jira integration settings — shared across all environments.
    /// Sensitive tokens can be supplied via environment variables:
    ///   ZEPHYR_JWT_TOKEN  — overrides ZephyrJwtToken
    ///   JIRA_API_TOKEN    — overrides JiraApiToken
    /// </summary>
    public static ZephyrSettings ZephyrSettings
    {
        get
        {
            var settings = Configuration.GetSection("ZephyrSettings").Get<ZephyrSettings>()
                           ?? new ZephyrSettings();

            var envJwt = Environment.GetEnvironmentVariable("ZEPHYR_JWT_TOKEN");
            if (!string.IsNullOrWhiteSpace(envJwt))
                settings = settings with { ZephyrJwtToken = envJwt, EnableZephyr = true };

            var envJiraToken = Environment.GetEnvironmentVariable("JIRA_API_TOKEN");
            if (!string.IsNullOrWhiteSpace(envJiraToken))
                settings = settings with { JiraApiToken = envJiraToken };

            return settings;
        }
    }
}
