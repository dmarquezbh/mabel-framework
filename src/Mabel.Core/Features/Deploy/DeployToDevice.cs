using Mabel.Core.Domain;
using Mabel.Core.Ports;

namespace Mabel.Core.Features.Deploy;

public sealed class DeployToDevice
{
    private readonly IShellExecutor _shell;
    private readonly IFileSystem _fs;

    public DeployToDevice(IShellExecutor shell, IFileSystem fs) { _shell = shell; _fs = fs; }

    public record Result(bool Success, string? Error);

    public Result Execute(string projectPath, Platform platform = Platform.Ios)
    {
        projectPath = Path.GetFullPath(projectPath);

        return platform switch
        {
            Platform.Ios     => Deploy(projectPath, "ios_app", "xtool dev"),
            Platform.Desktop => Deploy(projectPath, "desktop_app", "dotnet run"),
            Platform.Android => new(false, "Deploy Android ainda nao implementado."),
            _ => new(false, $"Plataforma nao suportada: {platform.Label()}"),
        };
    }

    private Result Deploy(string projectPath, string subDir, string command)
    {
        var dir = Path.Combine(projectPath, subDir);
        if (!_fs.DirectoryExists(dir))
            return new(false, $"'{subDir}/' nao encontrado em {projectPath}");

        // iOS: o host declara resources: [.copy("Resources")]. Sem o WASM do web_app
        // embutido ali, o SwiftPM/xtool falha ("Invalid Resource 'Resources': File not found").
        // Compila o web_app (Blazor WASM) e copia o modulo pro Resources do host.
        if (subDir == "ios_app")
        {
            var prep = PrepararWasmResources(projectPath, dir);
            if (prep is not null) return new(false, prep);
        }

        var rc = _shell.RunPassthrough(command, workingDir: dir);
        return rc == 0 ? new(true, null) : new(false, $"{command} saiu com codigo {rc}.");
    }

    /// <summary>
    /// Compila o WASM do web_app e o embute em Sources/&lt;target&gt;/Resources do host iOS,
    /// garantindo que o diretorio Resources exista e nao esteja vazio (requisito do .copy).
    /// </summary>
    private string? PrepararWasmResources(string projectPath, string iosDir)
    {
        var webApp = Path.Combine(projectPath, "web_app");
        if (Directory.Exists(webApp))
        {
            var rc = _shell.RunPassthrough("dotnet build -c Release", workingDir: webApp);
            if (rc != 0)
                return "Falha ao compilar o WASM do web_app (verifique o workload wasm-tools: dotnet workload install wasm-tools).";
        }

        // Localiza o .wasm compilado (mesmos candidatos do dev server).
        string? wasm = null;
        foreach (var rel in new[]
        {
            Path.Combine("bin", "Release", "net10.0", "wwwroot", "_framework"),
            Path.Combine("bin", "Release", "net10.0"),
            Path.Combine("bin", "Debug", "net10.0", "wwwroot", "_framework"),
            Path.Combine("bin", "Debug", "net10.0"),
        })
        {
            var d = Path.Combine(webApp, rel);
            if (!Directory.Exists(d)) continue;
            wasm = Directory.GetFiles(d, "*.wasm").FirstOrDefault();
            if (wasm is not null) break;
        }

        // Resources fica sob Sources/<target>/Resources (target unico do host).
        var sourcesRoot = Path.Combine(iosDir, "Sources");
        var target = Directory.Exists(sourcesRoot)
            ? Directory.GetDirectories(sourcesRoot).FirstOrDefault()
            : null;
        var resources = Path.Combine(target ?? Path.Combine(iosDir, "Sources", "ios_app"), "Resources");
        Directory.CreateDirectory(resources);

        if (wasm is not null)
            File.Copy(wasm, Path.Combine(resources, "app.wasm"), overwrite: true);
        else
            // Sem WASM (host placeholder ainda nao carrega wasm) — placeholder p/ o .copy nao quebrar.
            File.WriteAllText(Path.Combine(resources, ".keep"), "placeholder para .copy(Resources)\n");

        return null;
    }
}
