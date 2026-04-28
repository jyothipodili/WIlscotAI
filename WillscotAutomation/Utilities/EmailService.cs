using System.IO.Compression;
using System.Net;
using System.Net.Mail;
using System.Text;
using Serilog;
using WillscotAutomation.Config;

namespace WillscotAutomation.Utilities;

/// <summary>
/// Sends an HTML test-result summary email via Outlook / Office365 SMTP after each
/// test run.  All settings are driven by appsettings.json → "EmailSettings".
///
/// Attachments included automatically:
///   • TestResult.xml  — NUnit XML result file
///   • allure-results.zip — zipped Allure raw results for re-generating the report
///
/// Set EnableEmail = false in config to suppress email (e.g. on developer machines).
/// </summary>
public static class EmailService
{
    public static async Task SendTestResultEmailAsync(TestRunSummary summary)
    {
        var cfg = ConfigReader.EmailSettings;

        if (!cfg.EnableEmail)
        {
            Log.Information("[Email] Notifications disabled in config — skipping.");
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.SenderEmail) ||
            string.IsNullOrWhiteSpace(cfg.RecipientEmails))
        {
            Log.Warning("[Email] SenderEmail or RecipientEmails is empty — skipping.");
            return;
        }

        // ── CI environment metadata ────────────────────────────────────────────
        // Supports GitHub Actions, Azure DevOps, Jenkins, and local runs.
        var buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER")           // Jenkins
                       ?? Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER")      // GitHub Actions
                       ?? Environment.GetEnvironmentVariable("BUILD_BUILDNUMBER")      // Azure DevOps
                       ?? "LOCAL";

        var buildUrl = BuildUrl();

        var subject = $"[Automation Report] Build {buildNumber} — " +
                      $"Passed {summary.Passed}  Failed {summary.Failed}  " +
                      $"Skipped {summary.Skipped}";

        // ── Compose message ────────────────────────────────────────────────────
        using var message = new MailMessage
        {
            From       = new MailAddress(cfg.SenderEmail, "WillScot QA Automation"),
            Subject    = subject,
            IsBodyHtml = true,
            Body       = BuildHtmlBody(summary, buildNumber, buildUrl)
        };

        // To
        foreach (var addr in Split(cfg.RecipientEmails))
            message.To.Add(addr);

        // CC (optional)
        foreach (var addr in Split(cfg.Cc))
            message.CC.Add(addr);

        // ── Attachments ────────────────────────────────────────────────────────
        var tempFiles = new List<string>();
        try
        {
            AttachTestResultXml(message);
            AttachAllureResultsZip(message, tempFiles);

            // ── SMTP send ──────────────────────────────────────────────────────
            using var smtp = new SmtpClient(cfg.SmtpServer, cfg.Port)
            {
                EnableSsl             = true,
                DeliveryMethod        = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials           = new NetworkCredential(cfg.SenderEmail, cfg.SenderPassword),
                Timeout               = 30_000   // 30 s
            };

            await smtp.SendMailAsync(message);
            Log.Information("[Email] Report sent → {To}", cfg.RecipientEmails);
        }
        catch (SmtpException ex)
        {
            Log.Error(ex, "[Email] SMTP error ({Status}) — check SmtpServer / Port / credentials.",
                ex.StatusCode);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Email] Unexpected error while sending test result email.");
        }
        finally
        {
            // Dispose all Attachment streams before deleting temp zip files
            foreach (var att in message.Attachments)
                att.Dispose();

            foreach (var path in tempFiles)
            {
                try { File.Delete(path); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    // ── HTML body ──────────────────────────────────────────────────────────────

    private static string BuildHtmlBody(TestRunSummary s, string buildNumber, string buildUrl)
    {
        var passRate    = s.Total > 0 ? s.Passed  * 100.0 / s.Total : 0;
        var failRate    = s.Total > 0 ? s.Failed  * 100.0 / s.Total : 0;
        var overallColor = s.Failed == 0 ? "#27ae60" : "#e74c3c";
        var overallText  = s.Failed == 0 ? "&#10003; ALL PASSED" : $"&#10007; {s.Failed} FAILED";

        var buildUrlCell = buildUrl == "N/A"
            ? "<em style='color:#aaa;'>N/A (local run)</em>"
            : $"<a href='{buildUrl}' style='color:#2980b9;'>{buildUrl}</a>";

        var env = Environment.GetEnvironmentVariable("TEST_ENV") ?? "QA";

        return $@"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<style>
  *  {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #f0f2f5; padding: 24px; }}
  .wrapper {{ max-width: 680px; margin: auto; }}

  /* Header */
  .header {{ background: #1a3c5e; color: #fff; padding: 28px 32px; border-radius: 8px 8px 0 0; }}
  .header h1 {{ font-size: 20px; font-weight: 700; letter-spacing: .3px; }}
  .header p  {{ font-size: 13px; margin-top: 6px; opacity: .75; }}
  .badge {{ display: inline-block; margin-top: 14px; padding: 6px 20px;
            border-radius: 20px; font-size: 14px; font-weight: 700;
            background: {overallColor}; color: #fff; letter-spacing: .4px; }}

  /* Body */
  .body {{ background: #fff; padding: 28px 32px; }}
  h2 {{ font-size: 15px; color: #1a3c5e; margin: 20px 0 10px; border-bottom: 2px solid #e8edf2; padding-bottom: 6px; }}

  /* Tables */
  table  {{ width: 100%; border-collapse: collapse; font-size: 13px; margin-bottom: 6px; }}
  th {{ background: #1a3c5e; color: #fff; padding: 9px 14px; text-align: left; font-weight: 600; }}
  td {{ padding: 9px 14px; border-bottom: 1px solid #eef0f3; vertical-align: middle; }}
  tr:last-child td {{ border-bottom: none; }}
  tr:hover td {{ background: #f7f9fc; }}

  /* Metric colours */
  .pass {{ color: #27ae60; font-weight: 700; font-size: 15px; }}
  .fail {{ color: #e74c3c; font-weight: 700; font-size: 15px; }}
  .skip {{ color: #f39c12; font-weight: 700; font-size: 15px; }}
  .total{{ font-weight: 700; font-size: 15px; }}

  /* Progress bar */
  .bar-wrap {{ background: #eee; border-radius: 6px; height: 10px; width: 100%; }}
  .bar-pass {{ background: #27ae60; height: 10px; border-radius: 6px; width: {passRate:F0}%; }}

  /* Note box */
  .note {{ background: #f0f7ff; border-left: 4px solid #2980b9; padding: 12px 16px;
           font-size: 12px; color: #555; margin-top: 18px; border-radius: 0 4px 4px 0; }}

  /* Footer */
  .footer {{ background: #e8edf2; padding: 12px 32px; border-radius: 0 0 8px 8px;
             font-size: 11px; color: #888; text-align: center; }}
</style>
</head>
<body>
<div class='wrapper'>

  <!-- Header -->
  <div class='header'>
    <h1>WillScot Homepage — Automation Report</h1>
    <p>Build <strong>{buildNumber}</strong> &nbsp;&bull;&nbsp; Environment: <strong>{env}</strong> &nbsp;&bull;&nbsp; {s.StartUtc:yyyy-MM-dd HH:mm} UTC</p>
    <div class='badge'>{overallText}</div>
  </div>

  <!-- Body -->
  <div class='body'>

    <!-- Run details -->
    <h2>Run Details</h2>
    <table>
      <tr><th style='width:40%'>Property</th><th>Value</th></tr>
      <tr><td>Build Number</td>   <td><strong>{buildNumber}</strong></td></tr>
      <tr><td>Build URL</td>      <td>{buildUrlCell}</td></tr>
      <tr><td>Start Time (UTC)</td><td>{s.StartUtc:dddd, MMMM dd yyyy &nbsp; HH:mm:ss}</td></tr>
      <tr><td>End Time (UTC)</td>  <td>{s.FinishUtc:dddd, MMMM dd yyyy &nbsp; HH:mm:ss}</td></tr>
      <tr><td>Duration</td>        <td>{s.Duration.Minutes}m {s.Duration.Seconds}s</td></tr>
      <tr><td>Environment</td>     <td>{env}</td></tr>
      <tr><td>Browser</td>         <td>Chromium (Microsoft Playwright 1.44)</td></tr>
      <tr><td>Framework</td>       <td>C# .NET 8 &nbsp;|&nbsp; Reqnroll 2.2 &nbsp;|&nbsp; NUnit 4.1 &nbsp;|&nbsp; Allure 2.35</td></tr>
    </table>

    <!-- Test results -->
    <h2>Test Results</h2>
    <table>
      <tr><th>Status</th><th>Count</th><th>Rate</th></tr>
      <tr>
        <td>Total Tests</td>
        <td class='total'>{s.Total}</td>
        <td>—</td>
      </tr>
      <tr>
        <td>&#10003; Passed</td>
        <td class='pass'>{s.Passed}</td>
        <td class='pass'>{passRate:F1}%</td>
      </tr>
      <tr>
        <td>&#10007; Failed</td>
        <td class='{(s.Failed > 0 ? "fail" : "pass")}'>{s.Failed}</td>
        <td class='{(s.Failed > 0 ? "fail" : "pass")}'>{failRate:F1}%</td>
      </tr>
      <tr>
        <td>&#8212; Skipped</td>
        <td class='skip'>{s.Skipped}</td>
        <td>—</td>
      </tr>
    </table>

    <!-- Pass rate bar -->
    <p style='font-size:12px;color:#888;margin:10px 0 5px;'>Pass rate: {passRate:F1}%</p>
    <div class='bar-wrap'><div class='bar-pass'></div></div>

    <!-- Note -->
    <div class='note'>
      <strong>&#128206; Attachments:</strong> <code>TestResult.xml</code> (NUnit results) and
      <code>allure-results.zip</code> are attached to this email.<br><br>
      <strong>&#128202; View Allure Report locally:</strong><br>
      <code>allure generate allure-results --clean -o allure-report &amp;&amp; allure open allure-report</code>
    </div>

  </div><!-- /body -->

  <div class='footer'>
    Generated by WillScot QA Automation Suite &nbsp;&bull;&nbsp;
    Playwright + Reqnroll + NUnit + Allure &nbsp;&bull;&nbsp;
    {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
  </div>

</div><!-- /wrapper -->
</body>
</html>";
    }

    // ── Attachments ────────────────────────────────────────────────────────────

    /// <summary>Attaches TestResult.xml written by the NUnit adapter.</summary>
    private static void AttachTestResultXml(MailMessage message)
    {
        // NUnit adapter writes TestResult.xml relative to the test binary or CWD.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "TestResult.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "TestResult.xml"),
            Path.Combine(AppContext.BaseDirectory, "TestResults", "TestResult.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "TestResult.xml"),
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            Log.Warning("[Email] TestResult.xml not found — skipping attachment. " +
                        "Pass --results-directory to dotnet test to control the output location.");
            return;
        }

        message.Attachments.Add(new Attachment(path, "application/xml") { Name = "TestResult.xml" });
        Log.Debug("[Email] Attached TestResult.xml from {Path}", path);
    }

    /// <summary>
    /// Zips the allure-results directory and attaches it.
    /// The zip is written to the system temp folder and cleaned up after sending.
    /// </summary>
    private static void AttachAllureResultsZip(MailMessage message, List<string> tempFiles)
    {
        // Look for allure-results in the most likely locations
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "allure-results"),
            Path.Combine(AppContext.BaseDirectory, "allure-results"),
            // bin/Release or bin/Debug output directory (where dotnet test writes by default)
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "allure-results"),
        };

        var resultsDir = candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(Directory.Exists);

        if (resultsDir is null)
        {
            Log.Warning("[Email] allure-results directory not found — skipping zip attachment.");
            return;
        }

        var zipPath = Path.Combine(
            Path.GetTempPath(),
            $"allure-results-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        try
        {
            ZipFile.CreateFromDirectory(
                resultsDir, zipPath,
                CompressionLevel.Fastest,
                includeBaseDirectory: false);

            var attachment = new Attachment(zipPath, "application/zip")
            {
                Name = $"allure-results-{DateTime.Now:yyyy-MM-dd}.zip"
            };

            message.Attachments.Add(attachment);
            tempFiles.Add(zipPath);   // mark for cleanup after send

            Log.Debug("[Email] Attached allure-results.zip ({SizeKb} KB)",
                new FileInfo(zipPath).Length / 1024);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Email] Failed to create allure-results zip — skipping attachment.");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildUrl()
    {
        // Jenkins
        var jenkins = Environment.GetEnvironmentVariable("BUILD_URL");
        if (!string.IsNullOrEmpty(jenkins)) return jenkins;

        // GitHub Actions
        var ghServer = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL");
        var ghRepo   = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var ghRunId  = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        if (!string.IsNullOrEmpty(ghServer) && !string.IsNullOrEmpty(ghRepo))
            return $"{ghServer}/{ghRepo}/actions/runs/{ghRunId}";

        // Azure DevOps
        var adoCollection = Environment.GetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI");
        var adoProject    = Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECT");
        var adoBuildId    = Environment.GetEnvironmentVariable("BUILD_BUILDID");
        if (!string.IsNullOrEmpty(adoCollection) && !string.IsNullOrEmpty(adoProject))
            return $"{adoCollection}{adoProject}/_build/results?buildId={adoBuildId}";

        return "N/A";
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
