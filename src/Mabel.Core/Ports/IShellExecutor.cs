namespace Mabel.Core.Ports;

public record ShellResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

public interface IShellExecutor
{
    ShellResult Run(string command, string? workingDir = null);
    int RunPassthrough(string command, string? workingDir = null);
    bool CommandExists(string command);
    string? GetVersion(string command, string versionFlag = "--version");
}
