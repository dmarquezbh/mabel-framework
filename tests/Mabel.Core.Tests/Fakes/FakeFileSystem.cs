using Mabel.Core.Ports;

namespace Mabel.Core.Tests.Fakes;

public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new();
    private readonly HashSet<string> _directories = new();

    /// <summary>All files written during the test.</summary>
    public IReadOnlyDictionary<string, string> Files => _files;

    /// <summary>All directories created during the test.</summary>
    public IReadOnlyCollection<string> Directories => _directories;

    /// <summary>Seed a file that already exists.</summary>
    public FakeFileSystem WithFile(string path, string content = "")
    {
        _files[Normalize(path)] = content;
        return this;
    }

    /// <summary>Seed a directory that already exists.</summary>
    public FakeFileSystem WithDirectory(string path)
    {
        _directories.Add(Normalize(path));
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string ReadAllText(string path)
    {
        var key = Normalize(path);
        return _files.TryGetValue(key, out var content)
            ? content
            : throw new FileNotFoundException($"Fake file not found: {path}", path);
    }

    public void WriteAllText(string path, string content)
    {
        _files[Normalize(path)] = content;
    }

    public void CreateDirectory(string path)
    {
        _directories.Add(Normalize(path));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
