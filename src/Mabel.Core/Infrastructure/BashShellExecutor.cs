using System.Diagnostics;
using Mabel.Core.Ports;

namespace Mabel.Core.Infrastructure;

public sealed class BashShellExecutor : IShellExecutor
{
    public ShellResult Run(string command, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList passa cada arg como argv separado (sem re-parse do .NET, que
        // NÃO honra aspas simples) — bash -c recebe o comando inteiro como 1 arg.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        if (workingDir is not null) psi.WorkingDirectory = workingDir;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: /bin/bash -c ...");

        // Read stderr async to avoid deadlock when pipe buffer fills
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        var stderr = stderrTask.GetAwaiter().GetResult();

        return new(proc.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
    }

    public int RunPassthrough(string command, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        if (workingDir is not null) psi.WorkingDirectory = workingDir;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: /bin/bash -c ...");
        proc.WaitForExit();
        return proc.ExitCode;
    }

    public bool CommandExists(string command) => Run($"command -v {command}").Success;

    public string? GetVersion(string command, string versionFlag = "--version")
    {
        var r = Run($"{command} {versionFlag} 2>/dev/null");
        return r.Success && !string.IsNullOrWhiteSpace(r.Output) ? r.Output.Split('\n')[0].Trim() : null;
    }
}
