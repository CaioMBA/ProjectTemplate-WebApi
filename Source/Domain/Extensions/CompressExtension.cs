using System.IO.Compression;

namespace Domain.Extensions;

public static class CompressExtension
{
    public static byte[] Compress(this byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    public static byte[] Decompress(this byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        gzip.CopyTo(output);

        return output.ToArray();
    }

    public static async Task<byte[]> CompressAsync(this byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();

        var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);

        await using (gzip.ConfigureAwait(false))
        {
            await gzip.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    public static async Task<byte[]> DecompressAsync(this byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var input = new MemoryStream(data);
        using var output = new MemoryStream();

        var gzip = new GZipStream(input, CompressionMode.Decompress);

        await using (gzip.ConfigureAwait(false))
        {
            await gzip.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }
}
