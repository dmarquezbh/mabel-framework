using Mabel.Core.Ports;

namespace Mabel.Core.Features.Setup;

public sealed class RunSetup
{
    private readonly IShellExecutor _shell;
    private readonly IFileSystem _fs;

    public RunSetup(IShellExecutor shell, IFileSystem fs) { _shell = shell; _fs = fs; }

    public int Execute(bool uninstall = false)
    {
        var script = FindSetupScript();
        if (script is null) return -1;
        return _shell.RunPassthrough(uninstall ? $"bash \"{script}\" --uninstall" : $"bash \"{script}\"");
    }

    public string? FindSetupScript()
    {
        foreach (var rel in new[] { ".", "..", "../.." })
        {
            var p = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel, "setup.sh"));
            if (_fs.FileExists(p)) return p;
        }

        var git = _shell.Run("git rev-parse --show-toplevel");
        if (git.Success)
        {
            var p = Path.Combine(git.Output, "setup.sh");
            if (_fs.FileExists(p)) return p;
        }
        return null;
    }
}
