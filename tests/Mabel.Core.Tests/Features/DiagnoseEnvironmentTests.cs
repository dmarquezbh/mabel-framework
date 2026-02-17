using Mabel.Core.Domain;
using Mabel.Core.Features.Doctor;
using Mabel.Core.Tests.Fakes;
using Xunit;

namespace Mabel.Core.Tests.Features;

public class DiagnoseEnvironmentTests
{
    [Fact]
    public void AllToolsFound_ReturnsFullyHealthyResult()
    {
        var shell = new FakeShellExecutor()
            .WithCommand("dotnet")
            .WithCommand("git")
            .WithCommand("curl")
            .WithCommand("wasmtime")
            .WithCommand("swift")
            .WithCommand("xtool")
            .WithCommand("usbmuxd")
            .WithCommand("ideviceinfo")
            .WithCommand("adb")
            .WithResult("dotnet --version", 0, "10.0.100")
            .WithResult("git --version", 0, "git version 2.43.0")
            .WithResult("curl --version", 0, "curl 8.5.0")
            .WithResult("wasmtime --version", 0, "wasmtime-cli 17.0.0")
            .WithResult("swift --version", 0, "Swift version 6.0")
            .WithResult("xtool --version", 0, "xtool 0.5.0")
            .WithResult("usbmuxd --version", 0, "usbmuxd 1.1.1")
            .WithResult("ideviceinfo --version", 0, "ideviceinfo 1.3.0")
            .WithResult("adb --version", 0, "Android Debug Bridge version 1.0.41")
            .WithResult("uname -r", 0, "6.5.0-generic");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fs = new FakeFileSystem()
            .WithFile($"{home}/.bashrc", "# >>> mabel-framework >>>\nexport PATH\n# <<< mabel-framework <<<\n");

        var diag = new DiagnoseEnvironment(shell, fs);
        var result = diag.Execute(Platform.All);

        Assert.True(result.PathConfigured);
        Assert.False(result.IsWsl);
        Assert.All(result.Tools, t => Assert.True(t.Found, $"{t.Name} should be found"));
    }

    [Fact]
    public void MissingTools_ReturnsHints()
    {
        var shell = new FakeShellExecutor()
            .WithResult("uname -r", 0, "6.5.0-generic");

        var fs = new FakeFileSystem();

        var diag = new DiagnoseEnvironment(shell, fs);
        var result = diag.Execute(Platform.All);

        Assert.False(result.PathConfigured);
        Assert.True(result.Tools.Count > 0);

        var missing = result.Tools.Where(t => !t.Found).ToList();
        Assert.True(missing.Count > 0, "Some tools should be missing");
        Assert.All(missing, t => Assert.NotNull(t.Hint));
    }

    [Fact]
    public void WslDetected_WhenKernelContainsMicrosoft()
    {
        var shell = new FakeShellExecutor()
            .WithResult("uname -r", 0, "5.15.146.1-microsoft-standard-WSL2");

        var fs = new FakeFileSystem();

        var diag = new DiagnoseEnvironment(shell, fs);
        var result = diag.Execute(Platform.Desktop);

        Assert.True(result.IsWsl);
    }

    [Fact]
    public void IosOnly_SkipsDesktopAndAndroidTools()
    {
        var shell = new FakeShellExecutor()
            .WithResult("uname -r", 0, "6.5.0-generic");

        var fs = new FakeFileSystem();

        var diag = new DiagnoseEnvironment(shell, fs);
        var result = diag.Execute(Platform.Ios);

        // iOS-only should not include adb (Android)
        var names = result.Tools.Select(t => t.Name).ToList();
        Assert.DoesNotContain("adb", names);
        // Should include iOS-specific tools
        Assert.Contains("swift", names);
        Assert.Contains("xtool", names);
        Assert.Contains("usbmuxd", names);
    }

    [Fact]
    public void DotnetFoundViaKnownPath_WhenNotOnPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var shell = new FakeShellExecutor()
            .WithResult("uname -r", 0, "6.5.0-generic")
            .WithResult($"{home}/.dotnet/dotnet --version", 0, "10.0.100");

        var fs = new FakeFileSystem()
            .WithFile($"{home}/.dotnet/dotnet");

        var diag = new DiagnoseEnvironment(shell, fs);
        var result = diag.Execute(Platform.Desktop);

        var dotnet = result.Tools.First(t => t.Name == "dotnet");
        Assert.True(dotnet.Found);
        Assert.Equal("10.0.100", dotnet.Version);
    }
}
