namespace WillscotAutomation.Config;

/// <summary>
/// Strongly-typed model for the "EmailSettings" section in appsettings.json.
/// </summary>
public sealed class EmailSettings
{
    /// <summary>Set to false to skip email entirely (useful for local dev runs).</summary>
    public bool   EnableEmail     { get; init; } = false;

    /// <summary>Outlook / Office365 SMTP host.</summary>
    public string SmtpServer      { get; init; } = "smtp.office365.com";

    /// <summary>587 = STARTTLS (recommended for Office365).</summary>
    public int    Port            { get; init; } = 587;

    /// <summary>The "From" address — must be an authorised sender in your O365 tenant.</summary>
    public string SenderEmail     { get; init; } = "";

    /// <summary>App Password or OAuth token. Never commit the real value to source control.</summary>
    public string SenderPassword  { get; init; } = "";

    /// <summary>Semicolon-separated list of To addresses. e.g. "a@company.com;b@company.com"</summary>
    public string RecipientEmails { get; init; } = "";

    /// <summary>Semicolon-separated CC addresses (optional).</summary>
    public string Cc              { get; init; } = "";
}
