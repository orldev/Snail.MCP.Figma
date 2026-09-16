using System.Buffers.Binary;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Reads local pictures for the bridge.</summary>
/// <remarks>Expands <c>~</c>, refuses what Figma will not take, and answers with the base64 the plugin window decodes.</remarks>
public sealed class ImageFiles(ILogger<ImageFiles> logger) : IImageFiles
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the file and encodes it for the bridge.</summary>
    /// <remarks>Every refusal names the resolved path: the model sees only the string it passed, and <c>~</c> or a relative path resolves to something it cannot guess.</remarks>
    public string ReadAsBase64(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The picture path is empty.");
        }

        var resolved = Resolve(path);

        if (!File.Exists(resolved))
        {
            throw new InvalidOperationException($"No file at {resolved}.");
        }

        var extension = Path.GetExtension(resolved).ToLowerInvariant();

        if (!ImageLimits.RasterExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                $"Figma takes PNG, JPG, GIF and WebP as an image fill, and {resolved} is none of them.");
        }

        var length = new FileInfo(resolved).Length;

        if (length > ImageLimits.MaxFileBytes)
        {
            throw new InvalidOperationException(
                $"{resolved} is {length / 1024d / 1024d:F1} MB and the bridge carries up to {ImageLimits.MaxFileMegabytes} MB; export it at a smaller scale.");
        }

        if (Dimensions(resolved) is { } size && Math.Max(size.Width, size.Height) > ImageLimits.MaxPixelSide)
        {
            throw new InvalidOperationException(
                $"{resolved} is {size.Width}x{size.Height} and Figma takes up to {ImageLimits.MaxPixelSide} px on a side; crop it or export it smaller.");
        }

        logger.LogInformation("Reading {Bytes} bytes of {Path} for the editor", length, resolved);

        return Convert.ToBase64String(File.ReadAllBytes(resolved));
    }

    /// <summary>Pixel size read straight from the file header, or <c>null</c> for a format not read here.</summary>
    /// <remarks>PNG keeps it in IHDR, GIF in the logical screen descriptor, JPEG in the first frame marker; anything else is left for Figma to judge.</remarks>
    private static (int Width, int Height)? Dimensions(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];

        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
        {
            return null;
        }

        if (header[..8].SequenceEqual(PngSignature))
        {
            return (BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
        }

        if (header[..3].SequenceEqual("GIF"u8))
        {
            return (header[6] | (header[7] << 8), header[8] | (header[9] << 8));
        }

        return header[0] == 0xFF && header[1] == 0xD8 ? JpegDimensions(stream) : null;
    }

    /// <summary>Walks the JPEG marker chain to the first frame header.</summary>
    /// <remarks>That segment is the only one carrying the size, and anything unexpected gives up rather than guessing.</remarks>
    private static (int Width, int Height)? JpegDimensions(FileStream stream)
    {
        stream.Position = 2;
        Span<byte> segment = stackalloc byte[4];

        while (stream.ReadAtLeast(segment, segment.Length, throwOnEndOfStream: false) == segment.Length)
        {
            if (segment[0] != 0xFF)
            {
                return null;
            }

            var length = (segment[2] << 8) | segment[3];

            if (segment[1] is >= 0xC0 and <= 0xCF && segment[1] is not (0xC4 or 0xC8 or 0xCC))
            {
                Span<byte> frame = stackalloc byte[5];

                return stream.ReadAtLeast(frame, frame.Length, throwOnEndOfStream: false) == frame.Length
                    ? ((frame[3] << 8) | frame[4], (frame[1] << 8) | frame[2])
                    : null;
            }

            if (length < 2)
            {
                return null;
            }

            stream.Position += length - 2;
        }

        return null;
    }

    /// <summary>Expands <c>~</c> and makes the path absolute.</summary>
    /// <remarks>A relative path resolves against the working directory of the server process, not of the caller — which is why the tool asks for an absolute one.</remarks>
    private static string Resolve(string path)
    {
        var trimmed = path.Trim();

        if (trimmed == "~")
        {
            return ServerPaths.Home;
        }

        if (trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return Path.Combine(ServerPaths.Home, trimmed[2..]);
        }

        return Path.GetFullPath(trimmed);
    }
}
