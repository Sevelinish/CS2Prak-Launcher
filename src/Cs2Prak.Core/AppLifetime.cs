using Cs2Prak.Core.Update;

namespace Cs2Prak.Core;

public static class AppLifetime
{
    public static Action? BeforeExit;

    public static void Shutdown()
    {
        try { Cs2ServerProcess.Kill(); } catch (Exception) { }

        try { if (Updater.IsStaged) Updater.ApplyStaged(); } catch (Exception) { }

        try { BeforeExit?.Invoke(); } catch (Exception) { }
        Environment.Exit(0);
    }
}
