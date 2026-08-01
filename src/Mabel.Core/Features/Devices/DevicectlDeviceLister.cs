using System.Text.Json;
using Mabel.Core.Ports;

namespace Mabel.Core.Features.Devices;

/// <summary>
/// Lista devices iOS via `xcrun devicectl list devices` (API moderna do
/// Xcode 15+, disponivel so num Mac com Xcode.app instalado — substitui
/// `xtool devices`/`idevice_id` no fluxo Xcode-nativo). Usa --json-output
/// porque e a UNICA interface suportada pra consumo programatico (o proprio
/// --help do devicectl e explicito sobre isso) — a saida em tabela tem
/// colunas com espaco interno (ex.: "connected (no DDI)") e nao da pra
/// parsear com seguranca via split de texto.
/// </summary>
public static class DevicectlDeviceLister
{
    public static IReadOnlyList<DeviceInfo> List(IShellExecutor shell)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mabel-devicectl-{Guid.NewGuid():N}.json");
        try
        {
            var r = shell.Run($"xcrun devicectl list devices --json-output \"{tmp}\" -q");
            if (!r.Success || !File.Exists(tmp)) return [];

            return ParseDevicesJson(File.ReadAllText(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// Parseia o JSON de `devicectl list devices --json-output`, isolado do
    /// disparo real do processo pra dar pra testar com um fixture de texto.
    /// </summary>
    public static IReadOnlyList<DeviceInfo> ParseDevicesJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var devices = new List<DeviceInfo>();

        if (!doc.RootElement.TryGetProperty("result", out var result)) return devices;
        if (!result.TryGetProperty("devices", out var arr)) return devices;

        foreach (var d in arr.EnumerateArray())
        {
            var hw = d.TryGetProperty("hardwareProperties", out var h) ? h : default;
            var dp = d.TryGetProperty("deviceProperties", out var p) ? p : default;

            var platform = TryGetString(hw, "platform");
            if (!string.Equals(platform, "iOS", StringComparison.OrdinalIgnoreCase)) continue;

            // udid e o identificador classico (o mesmo que xctrace/xtool usam);
            // e diferente do "identifier" (GUID interno do CoreDevice) — mas
            // devicectl aceita udid tambem em --device, entao usamos so esse
            // formato em toda a stack (build + install + launch).
            var udid = TryGetString(hw, "udid");
            if (udid is null) continue;

            devices.Add(new DeviceInfo(
                udid,
                TryGetString(dp, "name") ?? udid,
                TryGetString(hw, "marketingName"),
                TryGetString(dp, "osVersionNumber"),
                "iOS"));
        }

        return devices;
    }

    private static string? TryGetString(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
