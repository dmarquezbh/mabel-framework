using System.Runtime.CompilerServices;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

// =============================================================================
// Mecânica de baseline do harness de snapshot (Onda 🟢).
//
// Localiza o baseline versionado em Snapshots/<nome>.snap (ao lado deste arquivo,
// via CallerFilePath) e compara com a captura atual. Fluxo:
//   • normal:                  compara; falha com diff se divergir.
//   • MABEL_UPDATE_SNAPSHOTS=1: (re)grava o baseline e passa — pra revisar/commitar.
//   • baseline ausente:        grava e FALHA pedindo commit (evita verde falso).
//
// O serializador (SduiSnapshot.Capture) é puro e vive no Protocol; aqui só a
// parte de arquivo — separação que mantém o Protocol livre de xUnit/IO.
// =============================================================================
internal static class SnapshotAssert
{
    private const string UpdateEnv = "MABEL_UPDATE_SNAPSHOTS";

    public static void Match(string actual, string name, [CallerFilePath] string sourceFile = "")
    {
        var dir = Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".snap");

        // Normaliza CRLF pra o baseline ser estável entre WSL e Windows.
        actual = actual.Replace("\r\n", "\n");

        bool update = Environment.GetEnvironmentVariable(UpdateEnv) is "1" or "true";

        if (update)
        {
            File.WriteAllText(path, actual);
            return;
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(path, actual);
            Assert.Fail($"Baseline de snapshot ausente: {path}\nFoi criado agora — revise e commite (ou rode com {UpdateEnv}=1).");
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (expected != actual)
            Assert.Fail(
                $"Snapshot '{name}' divergiu do baseline ({path}).\n" +
                $"Rode com {UpdateEnv}=1 pra atualizar se a mudança é esperada.\n\n" +
                $"--- baseline ---\n{expected}\n--- atual ---\n{actual}");
    }
}
