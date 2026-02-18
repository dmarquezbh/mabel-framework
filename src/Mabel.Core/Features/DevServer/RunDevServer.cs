using Mabel.Core.Ports;

namespace Mabel.Core.Features.DevServer;

/// <summary>
/// Mabel Live: hot reload server — watches files, recompiles WASM,
/// serves via HTTP and notifies connected devices via WebSocket.
///
/// Flow:
///   1. mabel live          -> starts the server
///   2. Mabel app on device -> connects to IP:port shown in terminal
///   3. Dev saves .razor    -> FileWatcher detects change
///   4. Recompiles blazor   -> generates new .wasm
///   5. WebSocket notifies  -> app downloads new .wasm and re-renders
/// </summary>
/// <remarks>
/// This is a simplified facade for the dev server workflow. For full functionality
/// (including HMR, diagnostics overlay, and multi-device support), use
/// <see cref="MabelDevServer"/> directly, as Program.cs does.
/// This class remains available as a convenient alternative for basic scenarios
/// where only a straightforward build-and-serve loop is needed.
/// </remarks>
public sealed class RunDevServer
{
    private readonly IShellExecutor _shell;
    private readonly IFileSystem _fs;

    public RunDevServer(IShellExecutor shell, IFileSystem fs)
    {
        _shell = shell;
        _fs = fs;
    }

    public record Options(
        string ProjectPath,
        int Port = 5555,
        bool Verbose = false
    );

    public record Result(bool Success, string? Error);

    public Result Execute(Options opts)
    {
        var projectPath = Path.GetFullPath(opts.ProjectPath);
        var webAppDir = Path.Combine(projectPath, "web_app");

        if (!_fs.DirectoryExists(webAppDir))
            return new(false, $"'web_app/' nao encontrado em {projectPath}. Rode 'mabel create' primeiro.");

        // Passo 1: Build inicial do WASM
        var buildResult = BuildWasm(webAppDir);
        if (buildResult != 0)
            return new(false, "Falha no build inicial do Blazor WASM.");

        // Passo 2: Inicia o dev server (HTTP + WebSocket + FileWatcher)
        // O server roda ate Ctrl+C
        var rc = _shell.RunPassthrough(
            $"dotnet run --project \"{webAppDir}\" -- --urls \"http://0.0.0.0:{opts.Port}\"",
            workingDir: projectPath);

        return rc == 0 ? new(true, null) : new(false, $"Dev server saiu com codigo {rc}.");
    }

    private int BuildWasm(string webAppDir)
    {
        return _shell.RunPassthrough("dotnet build", workingDir: webAppDir);
    }

    /// <summary>
    /// Returns the local IP address to display in the terminal.
    /// The developer points the Mabel app on the device to this IP.
    /// </summary>
    public string? GetLocalIp()
    {
        // hostname -I retorna os IPs da maquina (Linux)
        var r = _shell.Run("hostname -I 2>/dev/null || ipconfig getifaddr en0 2>/dev/null");
        if (!r.Success || string.IsNullOrWhiteSpace(r.Output)) return null;
        return r.Output.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }
}
