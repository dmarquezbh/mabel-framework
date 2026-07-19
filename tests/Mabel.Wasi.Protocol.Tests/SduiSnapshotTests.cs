using Mabel.Wasi.Protocol.DevTools;
using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Testing framework — snapshot semântico cross-host. Prova: (a) determinismo
/// (duas capturas idênticas), (b) resolução de tema/i18n embutida no snapshot,
/// (c) o tipo-200 vira !placeholder isolado, e (d) casamento contra baselines
/// versionados (Snapshots/*.snap) — regressão de layout/binding falha o teste.
/// </summary>
public class SduiSnapshotTests
{
    [Fact]
    public void Capture_IsDeterministic()
    {
        var doc = Fixtures.Rich();
        var a = SduiSnapshot.Capture(doc, new SduiInspectorOptions { Locale = "pt-BR" });
        var b = SduiSnapshot.Capture(doc, new SduiInspectorOptions { Locale = "pt-BR" });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Capture_UnknownType_BecomesIsolatedPlaceholder()
    {
        var snap = SduiSnapshot.Capture(Fixtures.Rich(), new SduiInspectorOptions { Locale = "pt-BR" });
        Assert.Contains("!placeholder id=future type=200 reason=unknown-type", snap);
    }

    [Fact]
    public void Capture_ReflectsThemeAndLocale()
    {
        var ptLight = SduiSnapshot.Capture(Fixtures.Rich(),
            new SduiInspectorOptions { Locale = "pt-BR", ThemeMode = SduiThemeMode.Light });
        var enDark = SduiSnapshot.Capture(Fixtures.Rich(),
            new SduiInspectorOptions { Locale = "en", ThemeMode = SduiThemeMode.Dark });

        Assert.Contains("Olá, Daniel", ptLight);
        Assert.Contains("bg=#FFFFFFFF", ptLight);       // surface claro
        Assert.Contains("Hello, Daniel", enDark);
        Assert.Contains("bg=#1A1A2EFF", enDark);        // surface escuro
    }

    // ── baselines versionados ────────────────────────────────────────────────

    [Fact]
    public void Baseline_PtLight()
    {
        var snap = SduiSnapshot.Capture(Fixtures.Rich(),
            new SduiInspectorOptions { Locale = "pt-BR", ThemeMode = SduiThemeMode.Light });
        SnapshotAssert.Match(snap, "rich.pt-BR.light");
    }

    [Fact]
    public void Baseline_EnDark()
    {
        var snap = SduiSnapshot.Capture(Fixtures.Rich(),
            new SduiInspectorOptions { Locale = "en", ThemeMode = SduiThemeMode.Dark });
        SnapshotAssert.Match(snap, "rich.en.dark");
    }
}
