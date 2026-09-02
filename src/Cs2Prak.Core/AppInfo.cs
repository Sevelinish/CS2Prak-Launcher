namespace Cs2Prak.Core;

public static class AppInfo
{
    public const string Version = "1.1.81";
    public const string UpdateRepo = "Sevelinish/cs2Prak";

    public const string WindowTitle = "CS2 Practice Server";
    public const string SplashTitle = "CS2 Practice Server - starting";

    public const string Host = "127.0.0.1";
    public const int Port = 5000;
    public static string HomeUrl => $"http://{Host}:{Port}/";
}
