using Domain.Interfaces.Platform;

namespace CrossCutting.Services;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public string ReadAllText(string path) => File.ReadAllText(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllBytesAsync(path, cancellationToken);

    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllLinesAsync(path, cancellationToken);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false) =>
        Directory.EnumerateFiles(path, searchPattern, SearchOptionFor(recursive));

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, bool recursive = false) =>
        Directory.EnumerateDirectories(path, searchPattern, SearchOptionFor(recursive));

    public Task WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, contents, cancellationToken);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string CombinePath(params string[] segments) => Path.Combine(segments);

    public string GetFullPath(string path) => Path.GetFullPath(path);

    private static SearchOption SearchOptionFor(bool recursive) =>
        recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
}
