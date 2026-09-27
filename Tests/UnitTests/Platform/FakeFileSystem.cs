using System.Text;
using Domain.Interfaces.Platform;

namespace UnitTests.Platform;

public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem AddFile(string path, string contents)
    {
        _files[Normalize(path)] = contents;

        return this;
    }

    public FakeFileSystem AddDirectory(string path)
    {
        _directories.Add(Normalize(path));

        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public long GetFileSize(string path) => Encoding.UTF8.GetByteCount(_files[Normalize(path)]);

    public string ReadAllText(string path) => _files[Normalize(path)];

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files[Normalize(path)]);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(Encoding.UTF8.GetBytes(_files[Normalize(path)]));

    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files[Normalize(path)].Split('\n'));

    public Stream OpenRead(string path) =>
        new MemoryStream(Encoding.UTF8.GetBytes(_files[Normalize(path)]));

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false) =>
        _files.Keys.Where(key => key.StartsWith(Normalize(path), StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, bool recursive = false) =>
        _directories.Where(key => key.StartsWith(Normalize(path), StringComparison.OrdinalIgnoreCase));

    public Task WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);

        return Task.CompletedTask;
    }

    public void CreateDirectory(string path) => AddDirectory(path);

    public string CombinePath(params string[] segments) => string.Join('/', segments);

    public string GetFullPath(string path) => Normalize(path);

    private static string Normalize(string path) => path.Replace('\\', '/');
}
