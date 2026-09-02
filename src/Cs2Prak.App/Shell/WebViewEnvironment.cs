using Cs2Prak.Core;
using Microsoft.Web.WebView2.Core;

namespace Cs2Prak.App.Shell;

internal static class WebViewEnvironment
{
    private static CoreWebView2Environment? _env;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<CoreWebView2Environment> GetAsync()
    {
        if (_env is not null) return _env;
        await Gate.WaitAsync();
        try
        {
            if (_env is null)
            {
                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--disable-logging --log-level=3",
                };
                _env = await CoreWebView2Environment.CreateAsync(null, StoragePath(), options);
            }
            return _env;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static string StoragePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(baseDir, "cs2prak", "webview");
        AppPaths.EnsureDir(path);
        return path;
    }
}
