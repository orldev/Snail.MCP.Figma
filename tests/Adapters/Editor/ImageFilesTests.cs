using System.Buffers.Binary;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>The reader is the only place that touches the disk for a picture, so its refusals are the whole error message the model gets.</summary>
public class ImageFilesTests : IDisposable
{
    private readonly string _directory = Path.Combine(TemporaryFolder.Create(), "images");

    public ImageFilesTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ImageFiles Reader() => new(NullLogger<ImageFiles>.Instance);

    [Fact]
    public void ImageFiles_PictureOnDisk_ComesBackAsBase64OfItsBytes()
    {
        var path = Path.Combine(_directory, "pic.png");
        byte[] bytes = [1, 2, 3, 4];
        File.WriteAllBytes(path, bytes);

        Assert.Equal(Convert.ToBase64String(bytes), Reader().ReadAsBase64(path));
    }

    [Fact]
    public void ImageFiles_MissingFile_IsRefusedByItsResolvedPath()
    {
        var path = Path.Combine(_directory, "gone.png");

        var error = Assert.Throws<InvalidOperationException>(() => Reader().ReadAsBase64(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageFiles_FormatFigmaCannotFill_IsRefused()
    {
        var path = Path.Combine(_directory, "icon.svg");
        File.WriteAllText(path, "<svg/>");

        var error = Assert.Throws<InvalidOperationException>(() => Reader().ReadAsBase64(path));

        Assert.Contains("PNG", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageFiles_FileOverTheCap_IsRefusedAndTheCapIsNamed()
    {
        var path = Path.Combine(_directory, "huge.png");

        using (var stream = File.Create(path))
        {
            stream.SetLength(ImageLimits.MaxFileBytes + 1);
        }

        var error = Assert.Throws<InvalidOperationException>(() => Reader().ReadAsBase64(path));

        Assert.Contains($"{ImageLimits.MaxFileMegabytes} MB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageFiles_PngWiderThanFigmaAllows_IsRefusedWithItsRealSize()
    {
        var path = Path.Combine(_directory, "wall.png");
        File.WriteAllBytes(path, Png(5000, 3000));

        var error = Assert.Throws<InvalidOperationException>(() => Reader().ReadAsBase64(path));

        Assert.Contains("5000x3000", error.Message, StringComparison.Ordinal);
        Assert.Contains($"{ImageLimits.MaxPixelSide}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageFiles_OversizedJpeg_SizeIsReadFromItsFrameMarker()
    {
        var path = Path.Combine(_directory, "photo.jpg");
        File.WriteAllBytes(path, Jpeg(9000, 120));

        var error = Assert.Throws<InvalidOperationException>(() => Reader().ReadAsBase64(path));

        Assert.Contains("9000x120", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageFiles_PictureWithinTheCeiling_PassesTheSizeCheck()
    {
        var path = Path.Combine(_directory, "fits.png");
        var bytes = Png(ImageLimits.MaxPixelSide, 100);
        File.WriteAllBytes(path, bytes);

        Assert.Equal(Convert.ToBase64String(bytes), Reader().ReadAsBase64(path));
    }

    /// <summary>Header of a PNG that declares a size: signature, length and type of IHDR, then width and height big-endian.</summary>
    private static byte[] Png(int width, int height)
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);

        return bytes;
    }

    /// <summary>Smallest JPEG shape the reader walks: SOI, one skipped segment, then an SOF0 carrying height and width.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        byte[] bytes =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x06, 0x4A, 0x46, 0x49, 0x46,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0, 0, 0, 0,
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        ];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(15, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(17, 2), (ushort)width);

        return bytes;
    }

    [Fact]
    public void ImageFiles_PathStartsWithTilde_ResolvesAgainstTheHomeDirectory()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Reader().ReadAsBase64("~/snail-picture-that-is-not-there.png"));

        Assert.Contains(ServerPaths.Home, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("~", error.Message, StringComparison.Ordinal);
    }
}
