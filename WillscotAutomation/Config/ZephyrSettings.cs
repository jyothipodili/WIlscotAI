namespace WillscotAutomation.Config;

/// <summary>
/// Strongly-typed model for the "ZephyrSettings" section in appsettings.json.
/// Controls the post-run Zephyr Essential + Jira integration.
///
/// Set EnableZephyr = true to activate.  All tokens can also be supplied via
/// environment variables (ZEPHYR_JWT_TOKEN, JIRA_API_TOKEN) to avoid storing
/// secrets in JSON files.
/// </summary>
public sealed record ZephyrSettings
{
    /// <summary>Set to true to enable post-run Zephyr integration.</summary>
    public bool EnableZephyr { get; init; } = false;

    /// <summary>Jira Cloud base URL — no trailing slash (e.g. "https://org.atlassian.net").</summary>
    public string JiraBaseUrl { get; init; } = "";

    /// <summary>Jira account email used for Basic authentication.</summary>
    public string JiraEmail { get; init; } = "";

    /// <summary>
    /// Jira API token for Basic auth.
    /// Alternatively set via JIRA_API_TOKEN environment variable.
    /// </summary>
    public string JiraApiToken { get; init; } = "";

    /// <summary>
    /// Zephyr Essential JWT access token — generated in Jira → Zephyr Essential → API Access.
    /// Alternatively set via ZEPHYR_JWT_TOKEN environment variable.
    /// </summary>
    public string ZephyrJwtToken { get; init; } = "";

    /// <summary>Zephyr Essential REST API base URL (no trailing slash).</summary>
    public string ZephyrApiBaseUrl { get; init; } = "https://api.zephyrscale.smartbear.com/v2";

    /// <summary>Jira project key (e.g. "SCRUM").</summary>
    public string ProjectKey { get; init; } = "SCRUM";

    /// <summary>
    /// Jira issue key to transition to "Done" when all tests pass (e.g. "SCRUM-1").
    /// Leave empty to skip the Jira transition step.
    /// </summary>
    public string JiraStoryKey { get; init; } = "";

    /// <summary>Prefix used in auto-generated Test Plan names.</summary>
    public string TestPlanNamePrefix { get; init; } = "WillScot Regression";

    /// <summary>Prefix used in auto-generated Test Cycle names.</summary>
    public string TestCycleNamePrefix { get; init; } = "Auto Cycle";
}
