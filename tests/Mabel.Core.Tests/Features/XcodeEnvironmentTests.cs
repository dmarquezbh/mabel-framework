using Mabel.Core.Features.Deploy;
using Mabel.Core.Tests.Fakes;
using Xunit;

namespace Mabel.Core.Tests.Features;

public class XcodeEnvironmentTests
{
    [Fact]
    public void IsNativeXcodeMac_FalseWhenXcodebuildMissing()
    {
        var shell = new FakeShellExecutor();
        var env = new XcodeEnvironment(shell);

        Assert.False(env.IsNativeXcodeMac());
    }

    [Fact]
    public void IsNativeXcodeMac_FalseWhenOnlyCommandLineTools()
    {
        // xcode-select -p aponta pras CLT, nao pro Xcode.app completo — CLT
        // sozinho nao tem xcodebuild com suporte a device iOS nem devicectl.
        var shell = new FakeShellExecutor()
            .WithCommand("xcodebuild")
            .WithResult("xcode-select -p", 0, "/Library/Developer/CommandLineTools");

        var env = new XcodeEnvironment(shell);

        Assert.False(env.IsNativeXcodeMac());
    }

    [Fact]
    public void IsNativeXcodeMac_TrueWhenFullXcodeInstalled()
    {
        if (!OperatingSystem.IsMacOS()) return; // deteccao real de SO so faz sentido rodando num Mac

        var shell = new FakeShellExecutor()
            .WithCommand("xcodebuild")
            .WithResult("xcode-select -p", 0, "/Applications/Xcode.app/Contents/Developer");

        var env = new XcodeEnvironment(shell);

        Assert.True(env.IsNativeXcodeMac());
    }
}
