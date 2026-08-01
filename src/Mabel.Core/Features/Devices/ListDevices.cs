using Mabel.Core.Ports;

namespace Mabel.Core.Features.Devices;

public record DeviceInfo(string Id, string? Name, string? Model, string? OsVersion, string Platform);

public sealed class ListDevices
{
    private readonly IShellExecutor _shell;
    public ListDevices(IShellExecutor shell) => _shell = shell;

    public record Result(IReadOnlyList<DeviceInfo> Devices, bool XtoolAvailable, bool IsWsl);

    public Result Execute()
    {
        var xtool = _shell.CommandExists("xtool");
        if (xtool) _shell.RunPassthrough("xtool devices");

        var devices = new List<DeviceInfo>();
        devices.AddRange(ListIos());
        devices.AddRange(ListAndroid());

        var wsl = _shell.Run("uname -r").Output.Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        return new(devices, xtool, wsl);
    }

    private IEnumerable<DeviceInfo> ListIos()
    {
        // Num Mac com Xcode.app, `xcrun devicectl` (Xcode 15+) e a fonte mais
        // moderna e confiavel — substitui idevice_id/libimobiledevice, que e o
        // caminho usado no Linux/WSL. Aditivo: so entra em jogo quando
        // devicectl de fato lista algo; senao cai no fluxo idevice_id de sempre.
        var viaDevicectl = DevicectlDeviceLister.List(_shell);
        if (viaDevicectl.Count > 0)
        {
            foreach (var d in viaDevicectl) yield return d;
            yield break;
        }

        if (!_shell.CommandExists("idevice_id")) yield break;
        var r = _shell.Run("idevice_id -l");
        if (!r.Success || string.IsNullOrWhiteSpace(r.Output)) yield break;

        foreach (var udid in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var id = udid.Trim();
            yield return new(id,
                Trim(_shell.Run($"ideviceinfo -u {id} -k DeviceName 2>/dev/null").Output),
                Trim(_shell.Run($"ideviceinfo -u {id} -k ProductType 2>/dev/null").Output),
                Trim(_shell.Run($"ideviceinfo -u {id} -k ProductVersion 2>/dev/null").Output),
                "iOS");
        }
    }

    private IEnumerable<DeviceInfo> ListAndroid()
    {
        if (!_shell.CommandExists("adb")) yield break;
        var r = _shell.Run("adb devices -l");
        if (!r.Success) yield break;

        foreach (var line in r.Output.Split('\n').Skip(1))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[1] != "device") continue;
            var id = parts[0];
            yield return new(id,
                Trim(_shell.Run($"adb -s {id} shell getprop ro.product.model 2>/dev/null").Output),
                null,
                Trim(_shell.Run($"adb -s {id} shell getprop ro.build.version.release 2>/dev/null").Output),
                "Android");
        }
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
