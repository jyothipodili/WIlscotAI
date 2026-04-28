namespace WillscotAutomation.Config;

/// <summary>
/// Strongly-typed model for the "TeamsSettings" section in appsettings.json.
/// Each environment's appsettings.{ENV}.json can override WebhookUrl independently.
/// </summary>
public sealed record TeamsSettings
{
    /// <summary>Set to false to skip Teams notification (e.g. local dev runs).</summary>
    public bool EnableTeams { get; init; } = false;

    /// <summary>
    /// Power Automate HTTP-trigger webhook URL.
    /// Never hardcode here — set per environment in appsettings.{ENV}.json
    /// or via the TEAMS_WEBHOOK_URL environment variable.
    /// </summary>
    public string WebhookUrl { get; init; } = "";

    /// <summary>Project name displayed in the Teams message.</summary>
    public string ProjectName { get; init; } = "WillScot Homepage Automation";

    /// <summary>
    /// Optional Allure report link shown in the Teams message.
    /// Leave empty if the report URL is not known at run time.
    /// </summary>
    public string AllureReportUrl { get; init; } = "";

    /// <summary>
    /// Optional CI build URL override.
    /// If empty, TeamsNotifier auto-detects from GitHub Actions / Jenkins / Azure DevOps env vars.
    /// </summary>
    public string BuildUrl { get; init; } = "";
}
