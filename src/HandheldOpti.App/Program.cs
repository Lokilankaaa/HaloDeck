using HandheldOpti.App.Native;

namespace HandheldOpti.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (HasArg(args, "--hw-service"))
        {
            PawnIoHardwareDaemon.RunService();
            return 0;
        }

        if (HasArg(args, "--install-hw-service"))
        {
            return PawnIoHardwareDaemon.InstallAndStartService();
        }

        if (HasArg(args, "--uninstall-hw-service"))
        {
            return PawnIoHardwareDaemon.UninstallService();
        }

        if (HasArg(args, "--pause-gpd-service"))
        {
            return PawnIoHardwareDaemon.SetGpdServicePaused(true);
        }

        if (HasArg(args, "--resume-gpd-service"))
        {
            return PawnIoHardwareDaemon.SetGpdServicePaused(false);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static bool HasArg(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
