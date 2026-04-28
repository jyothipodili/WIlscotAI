using Microsoft.Playwright;

namespace WillscotAutomation.Utilities;

// Uses Playwright's APIRequestContext so requests share the same browser session cookies.
public static class HttpHelper
{
    // Returns true if HTTP 200, false on any other status or network error.
    public static async Task<bool> ValidateHttpStatus200(
        IAPIRequestContext apiContext, string url)
    {
        try
        {
            var response = await apiContext.GetAsync(url, new APIRequestContextOptions
            {
                Timeout = 15_000
            });
            return response.Status == 200;
        }
        catch
        {
            return false;
        }
    }

    public static string ToAbsoluteUrl(string src, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(src)) return string.Empty;
        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return src;
        if (src.StartsWith("//")) return "https:" + src;

        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedSrc  = src.TrimStart('/');
        return $"{trimmedBase}/{trimmedSrc}";
    }
}
