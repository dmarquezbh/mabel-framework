using Mabel.Core.Ports;

namespace Mabel.Core.Features.Apple;

// Manage devices registered on the Apple Developer account via xtool.
// Enable/disable requires an xtool build with the `ds devices set-status`
// subcommand (see docs/gerenciar-devices-apple-xtool.md). The Apple API has
// no device delete — DISABLED frees the provisioning slot just the same.
public sealed class AppleDevices
{
    private readonly IShellExecutor _shell;
    public AppleDevices(IShellExecutor shell) => _shell = shell;

    public record Result(bool Success, string Message);

    // Override the xtool invocation with MABEL_XTOOL (full command prefix,
    // e.g. a wrapper script that sets LD_LIBRARY_PATH for a local build).
    public static string ResolveXtool() =>
        Environment.GetEnvironmentVariable("MABEL_XTOOL") is { Length: > 0 } custom ? custom : "xtool";

    public bool XtoolAvailable() => _shell.CommandExists(ResolveXtool().Split(' ')[0]);

    // Stock xtool (<= 1.17) only ships `ds devices list`; set-status needs the
    // patched build documented in docs/gerenciar-devices-apple-xtool.md.
    public bool SupportsSetStatus()
    {
        var r = _shell.Run($"{ResolveXtool()} ds devices --help");
        return r.Success && r.Output.Contains("set-status");
    }

    public int List() => _shell.RunPassthrough($"{ResolveXtool()} ds devices list");

    public Result SetStatus(string deviceId, bool enabled)
    {
        var status = enabled ? "ENABLED" : "DISABLED";
        var r = _shell.Run($"{ResolveXtool()} ds devices set-status {deviceId} {status}");
        var output = string.IsNullOrWhiteSpace(r.Output) ? r.Error : r.Output;
        return new(r.Success && output.Contains("OK:"), output.Trim());
    }
}
