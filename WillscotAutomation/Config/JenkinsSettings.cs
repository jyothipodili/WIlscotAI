namespace WillscotAutomation.Config;

public record JenkinsSettings
{
    public string BaseUrl  { get; init; } = "http://localhost:8080";
    public string JobName  { get; init; } = "WillScot-Automation";
    public string Username { get; init; } = "";
    public string ApiToken { get; init; } = "";
}
