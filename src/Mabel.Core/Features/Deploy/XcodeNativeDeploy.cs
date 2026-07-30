using System.Text.Json;
using Mabel.Core.Features.Devices;
using Mabel.Core.Ports;

namespace Mabel.Core.Features.Deploy;

/// <summary>
/// Build + deploy nativo via Xcode, pra quando o mabel roda num Mac de
/// verdade com Xcode.app instalado (ver <see cref="XcodeEnvironment.IsNativeXcodeMac"/>).
/// Alternativa ao <see cref="DeployToDevice"/> (que usa `xtool dev`) — o xtool
/// continua sendo o caminho certo pra Linux/WSL, onde nao ha Xcode. Aqui:
///
///   - build:   xcodebuild (CODE_SIGN_STYLE=Automatic, -allowProvisioningUpdates)
///   - listar:  xcrun devicectl list devices (substitui `xtool devices`)
///   - deploy:  xcrun devicectl device install app + device process launch
///              (substitui `xtool dev`)
///
/// Requer login com Apple ID em Xcode > Settings > Accounts pra o signing
/// automatico funcionar — isso nao da pra automatizar sem UI, entao so
/// verificamos e reportamos o erro (nao inventamos workaround).
/// </summary>
public sealed class XcodeNativeDeploy
{
    private readonly IShellExecutor _shell;
    private readonly IFileSystem _fs;

    public XcodeNativeDeploy(IShellExecutor shell, IFileSystem fs) { _shell = shell; _fs = fs; }

    public record Result(bool Success, string? Error);

    /// <summary>Lista devices iOS via <see cref="DevicectlDeviceLister"/>.</summary>
    public IReadOnlyList<DeviceInfo> ListDevices() => DevicectlDeviceLister.List(_shell);

    /// <summary>
    /// Build (xcodebuild) + install (devicectl device install app) + launch
    /// (devicectl device process launch) no device indicado por
    /// <paramref name="deviceUdid"/>, ou no primeiro device iOS encontrado
    /// (<see cref="ListDevices"/>) se for null.
    /// </summary>
    public Result Execute(string projectPath, string? deviceUdid = null, string configuration = "Debug")
    {
        projectPath = Path.GetFullPath(projectPath);
        var iosDir = Path.Combine(projectPath, "ios_app");
        if (!_fs.DirectoryExists(iosDir))
            return new(false, $"'ios_app/' nao encontrado em {projectPath}");

        var prep = WasmResourceBundler.Prepare(_shell, projectPath, iosDir);
        if (prep is not null) return new(false, prep);

        if (deviceUdid is null)
        {
            var devices = ListDevices();
            if (devices.Count == 0)
                return new(false, "Nenhum device iOS encontrado (xcrun devicectl list devices). Conecte um iPhone via USB e confie neste Mac.");
            deviceUdid = devices[0].Id;
        }

        var scheme = ResolveScheme(iosDir);
        if (scheme is null)
            return new(false, "Nao foi possivel resolver o scheme do Xcode (xcodebuild -list -json). Verifique o Package.swift em ios_app/.");

        var derivedData = Path.Combine(iosDir, ".xcode-build");
        var buildCmd = $"xcodebuild build -scheme \"{scheme}\" -destination \"id={deviceUdid}\" " +
                       $"-configuration {configuration} -derivedDataPath \"{derivedData}\" " +
                       "-allowProvisioningUpdates CODE_SIGN_STYLE=Automatic";

        var buildRc = _shell.RunPassthrough(buildCmd, workingDir: iosDir);
        if (buildRc != 0)
            return new(false,
                $"xcodebuild saiu com codigo {buildRc}. Causas comuns: nenhuma conta Apple ID logada " +
                "(Xcode > Settings > Accounts — nao da pra automatizar sem UI), plataforma iOS nao " +
                "instalada (xcodebuild -downloadPlatform iOS / Xcode > Settings > Components), ou " +
                "provisioning profile. Rode o comando acima direto em ios_app/ pra ver o erro completo.");

        var appPath = FindBuiltApp(derivedData, configuration);
        if (appPath is null)
            return new(false, $"Build ok mas o .app nao foi encontrado em {derivedData}/Build/Products/{configuration}-iphoneos*/.");

        var installRc = _shell.RunPassthrough($"xcrun devicectl device install app --device {deviceUdid} \"{appPath}\"");
        if (installRc != 0)
            return new(false, $"'xcrun devicectl device install app' saiu com codigo {installRc}.");

        var bundleId = ReadBundleId(iosDir) ?? Path.GetFileNameWithoutExtension(appPath);
        var launchRc = _shell.RunPassthrough($"xcrun devicectl device process launch --device {deviceUdid} {bundleId}");
        if (launchRc != 0)
            return new(false,
                $"App instalado no device, mas 'xcrun devicectl device process launch' saiu com codigo {launchRc}. " +
                "Abra o app manualmente na tela do device.");

        return new(true, null);
    }

    private string? ResolveScheme(string iosDir)
    {
        var r = _shell.Run("xcodebuild -list -json", workingDir: iosDir);
        return r.Success ? ParseScheme(r.Output) : null;
    }

    /// <summary>
    /// Extrai o primeiro scheme do JSON de `xcodebuild -list -json`, isolado
    /// do disparo real do processo pra dar pra testar com um fixture de texto.
    /// </summary>
    public static string? ParseScheme(string listJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(listJson);
            if (doc.RootElement.TryGetProperty("workspace", out var ws) &&
                ws.TryGetProperty("schemes", out var schemes) &&
                schemes.GetArrayLength() > 0)
                return schemes[0].GetString();

            if (doc.RootElement.TryGetProperty("project", out var proj) &&
                proj.TryGetProperty("schemes", out var pschemes) &&
                pschemes.GetArrayLength() > 0)
                return pschemes[0].GetString();
        }
        catch (JsonException)
        {
            // xcodebuild -list -json as vezes escreve avisos no stdout em builds
            // antigos; sem JSON valido, deixa o chamador reportar a falha.
        }

        return null;
    }

    private static string? FindBuiltApp(string derivedData, string configuration)
    {
        var productsDir = Path.Combine(derivedData, "Build", "Products");
        if (!Directory.Exists(productsDir)) return null;

        foreach (var candidate in Directory.GetDirectories(productsDir, $"{configuration}-iphoneos*"))
        {
            var app = Directory.GetDirectories(candidate, "*.app").FirstOrDefault();
            if (app is not null) return app;
        }

        return null;
    }

    private string? ReadBundleId(string iosDir)
    {
        var xtoolYml = Path.Combine(iosDir, "xtool.yml");
        if (!_fs.FileExists(xtoolYml)) return null;

        foreach (var line in _fs.ReadAllText(xtoolYml).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("bundleID:", StringComparison.OrdinalIgnoreCase))
                return trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
        }

        return null;
    }
}
