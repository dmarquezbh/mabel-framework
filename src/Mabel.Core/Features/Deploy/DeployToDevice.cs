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

        var rc = _shell.RunPassthrough(command, workingDir: dir);
        return rc == 0 ? new(true, null) : new(false, $"{command} saiu com codigo {rc}.");
    }
}
