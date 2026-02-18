using Mabel.Core.Domain;
using Mabel.Core.Features.Deploy;
using Mabel.Core.Features.Devices;
using Mabel.Core.Features.Doctor;
using Mabel.Core.Features.Scaffold;
using Mabel.Core.Features.Setup;
using Mabel.Core.Features.DevServer;
using Mabel.Core.Features.UsbHelp;
using Mabel.Core.Infrastructure;
using Mabel.Core.Ports;

// ── Mabel CLI ──────────────────────────────────────────────────────────
// Thin entrypoint: parse args, wire dependencies, delegate to features.

var shell = new BashShellExecutor();
var fs = new LocalFileSystem();

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

return args[0].ToLowerInvariant() switch
{
    "doctor"  => RunDoctor(args),
    "setup"   => RunSetup(args),
    "create"  => RunCreate(args),
    "deploy"  => RunDeploy(args),
    "live"     => RunLive(args),
    "devices"  => RunDevices(),
    "usb-help" => RunUsbHelp(args),
    "version" or "--version" or "-v" => PrintVersion(),
    "help" or "--help" or "-h"      => PrintUsage(),
    _ => UnknownCommand(args[0]),
};

// ── Commands ───────────────────────────────────────────────────────────

int RunDoctor(string[] args)
{
    var platformArg = GetArg(args, "--platform", "-p");
    Platform platform;
    try
    {
        platform = PlatformExtensions.Parse(platformArg);
    }
    catch (ArgumentException ex)
    {
        Ansi.Error(ex.Message);
        return 1;
    }

    var diag = new DiagnoseEnvironment(shell, fs);
    var result = diag.Execute(platform);

    Ansi.Header("mabel doctor");
    Ansi.Info($"Platforms: {platform.Label()}");
    if (result.IsWsl) Ansi.Info("WSL detected");
    Console.WriteLine();

    foreach (var t in result.Tools)
    {
        var icon = t.Found ? Ansi.Check : Ansi.Cross;
        var version = t.Found ? Ansi.Dim($" ({t.Version ?? "ok"})") : "";
        Console.WriteLine($"  {icon} {t.Name,-16} {t.Description}{version}");
        if (!t.Found && t.Hint is not null)
            Console.WriteLine($"     {Ansi.Dim($"fix: {t.Hint}")}");
    }

    Console.WriteLine();
    var ok = result.Tools.Count(t => t.Found);
    var total = result.Tools.Count;
    if (ok == total)
        Ansi.Success($"All {total} tools found.");
    else
        Ansi.Warn($"{ok}/{total} tools found. Run 'mabel setup' to install missing dependencies.");

    var pathIcon = result.PathConfigured ? Ansi.Check : Ansi.Cross;
    Console.WriteLine($"  {pathIcon} PATH configured in .bashrc");

    // USB help hint when device tools are missing
    var missingUsb = result.Tools.Where(t => !t.Found && t.Name is "usbmuxd" or "ideviceinfo" or "adb").ToList();
    if (missingUsb.Count > 0)
    {
        Console.WriteLine();
        Ansi.Info("Having trouble connecting a physical device?");
        Ansi.Info("Run 'mabel usb-help' for step-by-step USB setup instructions.");
    }

    return ok == total ? 0 : 1;
}

int RunSetup(string[] args)
{
    var uninstall = HasFlag(args, "--uninstall");

    Ansi.Header(uninstall ? "mabel setup --uninstall" : "mabel setup");

    var setup = new RunSetup(shell, fs);
    var script = setup.FindSetupScript();
    if (script is null)
    {
        Ansi.Error("setup.sh not found. Run from the mabel-framework directory.");
        return 1;
    }

    Ansi.Info($"Running {script}");
    return setup.Execute(uninstall);
}

int RunCreate(string[] args)
{
    var appName = GetPositional(args, 1);
    if (appName is null)
    {
        Ansi.Error("Usage: mabel create <app-name> [--bundle-id <id>] [--platform <ios,android,desktop>]");
        return 1;
    }

    var bundleId = GetArg(args, "--bundle-id", "-b") ?? $"com.example.{appName.ToLowerInvariant()}";
    var platformArg = GetArg(args, "--platform", "-p");
    Platform platform;
    try
    {
        platform = PlatformExtensions.Parse(platformArg);
    }
    catch (ArgumentException ex)
    {
        Ansi.Error(ex.Message);
        return 1;
    }

    Ansi.Header("mabel create");
    Ansi.Info($"App:       {appName}");
    Ansi.Info($"Bundle ID: {bundleId}");
    Ansi.Info($"Platforms: {platform.Label()}");
    Console.WriteLine();

    var creator = new CreateProject(shell, fs);
    var result = creator.Execute(new ScaffoldRequest(appName, bundleId, platform));

    if (result.Success)
        Ansi.Success($"Project '{appName}' created. cd {appName} && mabel deploy");
    else
        Ansi.Error(result.Error ?? "Unknown error.");

    return result.Success ? 0 : 1;
}

int RunDeploy(string[] args)
{
    var projectPath = GetPositional(args, 1) ?? ".";
    var platformArg = GetArg(args, "--platform", "-p") ?? "ios";
    Platform platform;
    try
    {
        platform = PlatformExtensions.Parse(platformArg);
    }
    catch (ArgumentException ex)
    {
        Ansi.Error(ex.Message);
        return 1;
    }

    // Deploy only supports a single platform at a time
    var single = platform.Each().First();

    Ansi.Header("mabel deploy");
    Ansi.Info($"Project:  {Path.GetFullPath(projectPath)}");
    Ansi.Info($"Platform: {single.Label()}");
    Console.WriteLine();

    var deployer = new DeployToDevice(shell, fs);
    var result = deployer.Execute(projectPath, single);

    if (result.Success)
        Ansi.Success("Deploy complete.");
    else
        Ansi.Error(result.Error ?? "Deploy failed.");

    return result.Success ? 0 : 1;
}

int RunLive(string[] args)
{
    var projectPath = GetPositional(args, 1) ?? ".";
    var portStr = GetArg(args, "--port", "-P");
    var port = portStr is not null && int.TryParse(portStr, out var p) ? p : 5555;
    var verbose = HasFlag(args, "--verbose");

    Ansi.Header("mabel live");
    Ansi.Info("Hot reload dev server — edit, save, see changes instantly");
    Console.WriteLine();

    using var server = new MabelDevServer(shell, projectPath, port, verbose);
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    return server.RunAsync(cts.Token).GetAwaiter().GetResult();
}

int RunDevices()
{
    Ansi.Header("mabel devices");

    var lister = new ListDevices(shell);
    var result = lister.Execute();

    if (result.IsWsl) Ansi.Info("WSL detected — USB passthrough required for physical devices.");

    if (result.Devices.Count == 0)
    {
        Ansi.Warn("No devices found.");
        Console.WriteLine();
        var env = UsbGuide.DetectEnvironment(result.IsWsl);
        Console.Write(UsbGuide.GetHelp(env));
    }
    else
    {
        Console.WriteLine();
        foreach (var d in result.Devices)
        {
            var name = d.Name ?? "Unknown";
            var model = d.Model is not null ? $" ({d.Model})" : "";
            var os = d.OsVersion is not null ? $" - {d.Platform} {d.OsVersion}" : $" - {d.Platform}";
            Console.WriteLine($"  {Ansi.Bullet} {name}{model}{os}");
            Console.WriteLine($"    {Ansi.Dim(d.Id)}");
        }
    }

    if (!result.XtoolAvailable)
        Ansi.Warn("xtool not found. Install with: mabel setup");

    return 0;
}

int PrintVersion()
{
    Console.WriteLine("mabel 0.1.0-dev");
    return 0;
}

int RunUsbHelp(string[] args)
{
    var platformArg = GetArg(args, "--platform", "-p");

    // Detect environment
    var isWsl = shell.Run("uname -r").Output.Contains("microsoft", StringComparison.OrdinalIgnoreCase);
    var env = UsbGuide.DetectEnvironment(isWsl);

    var showIos = true;
    var showAndroid = true;
    if (platformArg is not null)
    {
        try
        {
            var plat = PlatformExtensions.Parse(platformArg);
            showIos = plat.HasFlag(Platform.Ios);
            showAndroid = plat.HasFlag(Platform.Android);
        }
        catch (ArgumentException ex)
        {
            Ansi.Error(ex.Message);
            return 1;
        }
    }

    Ansi.Header("mabel usb-help");
    var envLabel = env switch
    {
        UsbGuide.Environment.Wsl   => "WSL (Windows Subsystem for Linux)",
        UsbGuide.Environment.Mac   => "macOS",
        _                          => "Linux",
    };
    Ansi.Info($"Detected environment: {envLabel}");
    Console.WriteLine();
    Console.Write(UsbGuide.GetHelp(env, showIos, showAndroid));

    return 0;
}

int PrintUsage()
{
    Ansi.Header("mabel");
    Console.WriteLine("  Cross-platform app framework — Blazor + WASI + native canvas rendering");
    Console.WriteLine();
    Console.WriteLine("  Usage: mabel <command> [options]");
    Console.WriteLine();
    Console.WriteLine("  Commands:");
    Console.WriteLine($"    {"doctor",-12} Check environment (tools, PATH, WSL)");
    Console.WriteLine($"    {"setup",-12}  Install dependencies (.NET, Swift, xtool, wasmtime)");
    Console.WriteLine($"    {"create",-12} Scaffold a new Mabel project");
    Console.WriteLine($"    {"deploy",-12} Build and run on a device/emulator");
    Console.WriteLine($"    {"live",-12}   Start hot reload dev server (Mabel Live)");
    Console.WriteLine($"    {"devices",-12}List connected devices");
    Console.WriteLine($"    {"usb-help",-12}USB setup guide for physical devices");
    Console.WriteLine($"    {"version",-12}Show version");
    Console.WriteLine();
    Console.WriteLine("  Options:");
    Console.WriteLine("    --platform, -p   Target platform (ios, android, desktop, all)");
    Console.WriteLine("    --bundle-id, -b  Bundle ID for create (default: com.example.<name>)");
    Console.WriteLine("    --uninstall      Remove installed dependencies (setup only)");
    Console.WriteLine();
    return 0;
}

int UnknownCommand(string cmd)
{
    Ansi.Error($"Unknown command: '{cmd}'. Run 'mabel help' for usage.");
    return 1;
}

// ── Arg helpers ────────────────────────────────────────────────────────

static string? GetArg(string[] args, string longName, string shortName)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == longName || args[i] == shortName)
            return args[i + 1];
    return null;
}

/// <summary>
/// Gets a positional argument by its index, skipping any flags (--foo, -f)
/// and their values. This correctly handles: mabel create --platform ios myapp
/// </summary>
static string? GetPositional(string[] args, int positionalIndex)
{
    var currentPositional = 0;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith('-'))
        {
            // Skip the flag and its value (e.g., --platform ios)
            i++;
            continue;
        }

        if (currentPositional == positionalIndex)
            return args[i];

        currentPositional++;
    }

    return null;
}

static bool HasFlag(string[] args, string flag)
    => args.Any(a => a == flag);

// ── ANSI helpers ───────────────────────────────────────────────────────

static class Ansi
{
    private static readonly bool NoColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;

    public const string Check  = "\u2714";  // checkmark
    public const string Cross  = "\u2718";  // x-mark
    public const string Bullet = "\u2022";  // bullet

    public static void Header(string text)
    {
        Console.WriteLine();
        Console.WriteLine(Bold($"  {text}"));
        Console.WriteLine($"  {new string('\u2500', text.Length)}");  // horizontal line
    }

    public static void Info(string text)    => Console.WriteLine($"  {Dim(text)}");
    public static void Success(string text) => Console.WriteLine($"  {Green(Check)} {text}");
    public static void Warn(string text)    => Console.WriteLine($"  {Yellow("!")} {text}");
    public static void Error(string text)   => Console.Error.WriteLine($"  {Red(Cross)} {text}");

    public static string Dim(string s)    => Wrap(s, "2");
    public static string Bold(string s)   => Wrap(s, "1");
    public static string Green(string s)  => Wrap(s, "32");
    public static string Yellow(string s) => Wrap(s, "33");
    public static string Red(string s)    => Wrap(s, "31");

    private static string Wrap(string s, string code) => NoColor ? s : $"\x1b[{code}m{s}\x1b[0m";
}
