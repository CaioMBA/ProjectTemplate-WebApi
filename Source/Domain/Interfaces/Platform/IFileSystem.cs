namespace Domain.Interfaces.Platform;

public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    long GetFileSize(string path);

    string ReadAllText(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

    Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);

    Stream OpenRead(string path);

    IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false);

    IEnumerable<string> EnumerateDirectories(string path, string searchPattern, bool recursive = false);

    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

    void CreateDirectory(string path);

    string CombinePath(params string[] segments);

    string GetFullPath(string path);
}
