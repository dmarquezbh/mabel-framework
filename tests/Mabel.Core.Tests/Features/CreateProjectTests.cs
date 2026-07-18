using Mabel.Core.Domain;
using Mabel.Core.Features.Scaffold;
using Mabel.Core.Tests.Fakes;
using Xunit;

namespace Mabel.Core.Tests.Features;

public class CreateProjectTests
{
    // O scaffolder deve emitir um App.swift com @main. Sem ele o xtool gera o
    // executavel <target>-App mas o link falha: "ld64.lld: undefined symbol: main".
    private static (ScaffoldResult Result, FakeFileSystem Fs) ScaffoldIos(string appName)
    {
        var shell = new FakeShellExecutor()
            .WithResult("dotnet new blazorwasm", 0);
        var fs = new FakeFileSystem();

        var result = new CreateProject(shell, fs)
            .Execute(new ScaffoldRequest(appName, "com.example.app", Platform.Ios));

        return (result, fs);
    }

    private static string AppSwift(FakeFileSystem fs) =>
        fs.Files.Single(f => f.Key.Replace('\\', '/').EndsWith("ios_app/Sources/ios_app/App.swift")).Value;

    [Fact]
    public void ScaffoldIos_GeneratesAppSwiftWithMainEntryPoint()
    {
        var (result, fs) = ScaffoldIos("rui-native");

        Assert.True(result.Success, result.Error);

        var app = AppSwift(fs);
        Assert.Contains("@main", app);
        Assert.Contains(": App", app);
        Assert.Contains("WindowGroup", app);
        Assert.Contains("ContentView()", app);
    }

    [Theory]
    [InlineData("rui-native", "RuiNativeApp")]      // hifen -> PascalCase
    [InlineData("MyApp", "MyAppApp")]               // ja PascalCase, sufixo App
    [InlineData("hello_world app", "HelloWorldAppApp")]
    [InlineData("3d-viewer", "Mabel3dViewerApp")]   // comeca com digito -> prefixo Mabel
    public void ScaffoldIos_DerivesValidSwiftStructName(string appName, string expectedStruct)
    {
        var (result, fs) = ScaffoldIos(appName);

        Assert.True(result.Success, result.Error);
        Assert.Contains($"struct {expectedStruct}: App", AppSwift(fs));
    }
}
