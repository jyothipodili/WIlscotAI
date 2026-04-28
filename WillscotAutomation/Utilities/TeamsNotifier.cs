using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;
using WillscotAutomation.Config;

namespace WillscotAutomation.Utilities;

/// <summary>
/// Sends an automation-run summary to a Microsoft Teams channel via webhook.
///
/// Supports two webhook types — auto-detected from the URL:
///   1. Incoming Webhook connector  (webhook.office.com)
///      → Sends a MessageCard (legacy connector format — most compatible).
///      → Create via: Teams channel → ... → Connectors → Incoming Webhook
///   2. Teams Workflow webhook  (logic.azure.com / webhook.microsoft.com)
///      → Sends an Adaptive Card.
///      → Create via: Teams channel → ... → Workflows → "Post to channel on webhook request"
///
/// Set EnableTeams = false in config to suppress the notification.
/// </summary>
public static class TeamsNotifier
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IPAddress>
        _dnsCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient _http = CreateHttpClient();

    // ── Public API ─────────────────────────────────────────────────────────────

    public static async Task SendNotificationAsync(TestRunSummary summary)
    {
        var cfg = ConfigReader.TeamsSettings;

        // Log raw values so config loading issues are visible in the log file
        Log.Debug("[Teams] Config — EnableTeams={EnableTeams}  WebhookUrl={Url}",
            cfg.EnableTeams, MaskUrl(cfg.WebhookUrl ?? ""));

        // A non-empty WebhookUrl is treated as implicit opt-in, regardless of the
        // EnableTeams flag, to guard against config-binding issues silently swallowing
        // the notification when the URL is clearly set.
        var hasUrl = !string.IsNullOrWhiteSpace(cfg.WebhookUrl);

        if (!cfg.EnableTeams && !hasUrl)
        {
            Log.Information("[Teams] Notifications disabled and no WebhookUrl set — skipping.");
            return;
        }

        if (!hasUrl)
        {
            Log.Warning("[Teams] WebhookUrl is empty — skipping. " +
                        "Set TeamsSettings:WebhookUrl in appsettings.{ENV}.json.");
            return;
        }

        try
        {
            var env         = Environment.GetEnvironmentVariable("TEST_ENV") ?? "QA";
            var allPassed   = summary.Failed == 0;
            var statusEmoji = allPassed ? "✅" : "❌";
            var statusText  = allPassed ? "PASSED" : "FAILED";
            var buildNumber = ResolveBuildNumber();
            var buildUrl    = ResolveBuildUrl(cfg.BuildUrl);
            var reportUrl   = string.IsNullOrWhiteSpace(cfg.AllureReportUrl) ? null : cfg.AllureReportUrl;
            var duration    = FormatDuration(summary.Duration);

            Log.Information("[Teams] Sending notification — Status: {Status}  Total: {Total}  " +
                            "Passed: {Passed}  Failed: {Failed}  Skipped: {Skipped}",
                statusText, summary.Total, summary.Passed, summary.Failed, summary.Skipped);
            Log.Debug("[Teams] Webhook target: {Url}", MaskUrl(cfg.WebhookUrl ?? ""));

            var isLegacyConnector = IsIncomingWebhookConnector(cfg.WebhookUrl ?? "");

            if (isLegacyConnector)
            {
                Log.Warning("[Teams] *** DEPRECATED WEBHOOK DETECTED ***");
                Log.Warning("[Teams] Your URL is a webhook.office.com Incoming Webhook connector.");
                Log.Warning("[Teams] Microsoft retired these connectors in late 2024.");
                Log.Warning("[Teams] The endpoint still returns HTTP 200 but messages are NOT delivered to Teams.");
                Log.Warning("[Teams] HOW TO FIX:");
                Log.Warning("[Teams]   1. Open your Teams channel → click '...' → 'Workflows'");
                Log.Warning("[Teams]   2. Search 'Post to a channel when a webhook request is received'");
                Log.Warning("[Teams]   3. Set it up and copy the new webhook URL");
                Log.Warning("[Teams]   4. Update TeamsSettings:WebhookUrl in appsettings.QA.json with the new URL");
            }

            string json;
            if (isLegacyConnector)
            {
                Log.Debug("[Teams] Using MessageCard format (Incoming Webhook connector).");
                json = BuildMessageCard(cfg.ProjectName, env, statusEmoji, statusText, allPassed,
                                        summary, duration, buildNumber, buildUrl, reportUrl);
            }
            else
            {
                Log.Debug("[Teams] Using Adaptive Card format (Workflow webhook).");
                json = BuildAdaptiveCard(cfg.ProjectName, env, statusEmoji, statusText, allPassed,
                                         summary, duration, buildNumber, buildUrl, reportUrl);
            }

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(cfg.WebhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                if (isLegacyConnector)
                    Log.Warning("[Teams] Webhook returned HTTP {StatusCode} — but this is a retired connector " +
                                "so the message will NOT appear in Teams. Replace the URL as instructed above.",
                        (int)response.StatusCode);
                else
                    Log.Information("[Teams] Notification delivered (HTTP {StatusCode}).",
                        (int)response.StatusCode);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                Log.Warning("[Teams] Webhook returned HTTP {StatusCode}. Body: {Body}",
                    (int)response.StatusCode,
                    body.Length > 500 ? body[..500] + "…" : body);
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "[Teams] HTTP request failed — check network/webhook URL.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            Log.Error(ex, "[Teams] Request timed out after 30 s.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Teams] Unexpected error sending Teams notification.");
        }
    }

    // ── MessageCard (webhook.office.com Incoming Webhook) ─────────────────────

    private static string BuildMessageCard(
        string project, string env,
        string statusEmoji, string statusText, bool allPassed,
        TestRunSummary s, string duration,
        string buildNumber, string buildUrl, string? reportUrl)
    {
        var themeColor = allPassed ? "00B050" : "FF0000";

        // Build facts
        var facts = new JsonArray
        {
            Fact("Environment", env),
            Fact("Build",       buildNumber),
            Fact("Total",       s.Total.ToString()),
            Fact("✅ Passed",   s.Passed.ToString()),
            Fact("❌ Failed",   s.Failed.ToString()),
            Fact("⏭ Skipped",  s.Skipped.ToString()),
            Fact("Duration",    duration),
            Fact("Started",     s.StartUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"),
            Fact("Finished",    s.FinishUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"),
        };

        // Append failed scenario IDs if any
        if (s.Failed > 0)
        {
            var failedIds = s.ScenarioResults
                .Where(kv => !kv.Value)
                .Select(kv => kv.Key)
                .OrderBy(k => k);
            facts.Add(Fact("Failed Tests", string.Join(", ", failedIds)));
        }

        // Build action buttons
        var actions = new JsonArray();
        if (!string.IsNullOrWhiteSpace(buildUrl) && buildUrl != "N/A")
        {
            actions.Add(new JsonObject
            {
                ["@type"]  = "OpenUri",
                ["name"]   = "🔗 View Build",
                ["targets"] = new JsonArray
                {
                    new JsonObject { ["os"] = "default", ["uri"] = buildUrl }
                }
            });
        }
        if (!string.IsNullOrWhiteSpace(reportUrl))
        {
            actions.Add(new JsonObject
            {
                ["@type"]  = "OpenUri",
                ["name"]   = "📊 Allure Report",
                ["targets"] = new JsonArray
                {
                    new JsonObject { ["os"] = "default", ["uri"] = reportUrl }
                }
            });
        }

        var card = new JsonObject
        {
            ["@type"]      = "MessageCard",
            ["@context"]   = "https://schema.org/extensions",
            ["themeColor"] = themeColor,
            ["summary"]    = $"{statusEmoji} {project} — {statusText}",
            ["sections"]   = new JsonArray
            {
                new JsonObject
                {
                    ["activityTitle"]    = $"{statusEmoji} **{statusText}** — {project}",
                    ["activitySubtitle"] = $"Environment: {env} | Build: {buildNumber}",
                    ["facts"]            = facts,
                    ["markdown"]         = true
                }
            }
        };

        if (actions.Count > 0)
            card["potentialAction"] = actions;

        return card.ToJsonString();
    }

    // ── Adaptive Card (Teams Workflow / logic.azure.com) ──────────────────────

    private static string BuildAdaptiveCard(
        string project, string env,
        string statusEmoji, string statusText, bool allPassed,
        TestRunSummary s, string duration,
        string buildNumber, string buildUrl, string? reportUrl)
    {
        var statusColor = allPassed ? "Good" : "Attention";

        var bodyItems = new JsonArray
        {
            new JsonObject
            {
                ["type"]   = "TextBlock",
                ["text"]   = $"{statusEmoji} {statusText} — {project}",
                ["size"]   = "Large",
                ["weight"] = "Bolder",
                ["color"]  = statusColor,
                ["wrap"]   = true
            },
            new JsonObject
            {
                ["type"]     = "TextBlock",
                ["text"]     = $"{env} | Build {buildNumber}",
                ["isSubtle"] = true,
                ["wrap"]     = true
            },
            new JsonObject { ["type"] = "Separator" },
            new JsonObject
            {
                ["type"]  = "FactSet",
                ["facts"] = new JsonArray
                {
                    AdaptiveFact("Total",      s.Total.ToString()),
                    AdaptiveFact("✅ Passed",  s.Passed.ToString()),
                    AdaptiveFact("❌ Failed",  s.Failed.ToString()),
                    AdaptiveFact("⏭ Skipped", s.Skipped.ToString()),
                    AdaptiveFact("Duration",   duration),
                    AdaptiveFact("Started",    s.StartUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"),
                    AdaptiveFact("Finished",   s.FinishUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"),
                }
            }
        };

        if (s.Failed > 0)
        {
            var failedIds = s.ScenarioResults
                .Where(kv => !kv.Value)
                .Select(kv => kv.Key)
                .OrderBy(k => k);

            bodyItems.Add(new JsonObject { ["type"] = "Separator" });
            bodyItems.Add(new JsonObject
            {
                ["type"]  = "TextBlock",
                ["text"]  = "**Failed:** " + string.Join(", ", failedIds),
                ["color"] = "Attention",
                ["wrap"]  = true
            });
        }

        var actions = new JsonArray();
        if (!string.IsNullOrWhiteSpace(buildUrl) && buildUrl != "N/A")
            actions.Add(new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "🔗 View Build", ["url"] = buildUrl });
        if (!string.IsNullOrWhiteSpace(reportUrl))
            actions.Add(new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "📊 Allure Report", ["url"] = reportUrl });

        // Use JsonObject so we can set "$schema" key (not possible with anonymous types)
        var adaptiveCard = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"]    = "AdaptiveCard",
            ["version"] = "1.2",
            ["body"]    = bodyItems
        };
        if (actions.Count > 0)
            adaptiveCard["actions"] = actions;

        var wrapper = new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray
            {
                new JsonObject
                {
                    ["contentType"] = "application/vnd.microsoft.card.adaptive",
                    ["contentUrl"]  = JsonValue.Create<string?>(null),
                    ["content"]     = adaptiveCard
                }
            }
        };

        return wrapper.ToJsonString();
    }

    // ── JSON helpers ───────────────────────────────────────────────────────────

    private static JsonObject Fact(string name, string value) =>
        new() { ["name"] = name, ["value"] = value };

    private static JsonObject AdaptiveFact(string title, string value) =>
        new() { ["title"] = title, ["value"] = value };

    // ── URL detection ──────────────────────────────────────────────────────────

    private static bool IsIncomingWebhookConnector(string url) =>
        url.Contains("webhook.office.com", StringComparison.OrdinalIgnoreCase);

    // ── HttpClient with DNS fallback ───────────────────────────────────────────

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            ConnectCallback          = FallbackDnsConnectAsync
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static async ValueTask<Stream> FallbackDnsConnectAsync(
        SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var port = ctx.DnsEndPoint.Port;

        if (!_dnsCache.TryGetValue(host, out var ip))
        {
            ip = await TrySystemDns(host, ct) ?? await ResolveViaDoH(host, ct);
            if (ip == null) throw new SocketException((int)SocketError.HostNotFound);
            _dnsCache[host] = ip;
        }

        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(new IPEndPoint(ip, port), ct);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static async Task<IPAddress?> TrySystemDns(string host, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
        }
        catch (SocketException) { return null; }
    }

    private static async Task<IPAddress?> ResolveViaDoH(string hostname, CancellationToken ct)
    {
        try
        {
            var dohHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct2) =>
                {
                    var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    await s.ConnectAsync(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 443), ct2);
                    return new NetworkStream(s, ownsSocket: true);
                }
            };
            using var dohClient = new HttpClient(dohHandler) { Timeout = TimeSpan.FromSeconds(10) };
            var url  = $"https://dns.google/resolve?name={Uri.EscapeDataString(hostname)}&type=A";
            var json = await dohClient.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Answer", out var answers))
                foreach (var answer in answers.EnumerateArray())
                    if (answer.TryGetProperty("type", out var t) && t.GetInt32() == 1 &&
                        answer.TryGetProperty("data", out var data) &&
                        IPAddress.TryParse(data.GetString(), out var parsedIp))
                        return parsedIp;
        }
        catch (Exception ex) { Log.Debug(ex, "[Teams] DoH resolution failed for {Host}.", hostname); }
        return null;
    }

    // ── Misc helpers ───────────────────────────────────────────────────────────

    private static string FormatDuration(TimeSpan d)
    {
        var totalMins = (int)d.TotalMinutes;
        return totalMins > 0 ? $"{totalMins}m {d.Seconds}s" : $"{d.Seconds}s";
    }

    private static string ResolveBuildNumber() =>
        Environment.GetEnvironmentVariable("BUILD_NUMBER")
        ?? Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER")
        ?? Environment.GetEnvironmentVariable("BUILD_BUILDNUMBER")
        ?? "LOCAL";

    private static string ResolveBuildUrl(string configuredUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredUrl)) return configuredUrl;
        var jenkins = Environment.GetEnvironmentVariable("BUILD_URL");
        if (!string.IsNullOrEmpty(jenkins)) return jenkins;
        var ghServer = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL");
        var ghRepo   = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var ghRunId  = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        if (!string.IsNullOrEmpty(ghServer) && !string.IsNullOrEmpty(ghRepo))
            return $"{ghServer}/{ghRepo}/actions/runs/{ghRunId}";
        return "N/A";
    }

    private static string MaskUrl(string url)
    {
        var idx = url.IndexOf("/IncomingWebhook/", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? url[..idx] + "/IncomingWebhook/***" : url.Length > 60 ? url[..60] + "***" : url;
    }
}
