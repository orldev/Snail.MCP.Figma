namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Source of picture files named by path.</summary>
/// <remarks>Bytes read here travel to the editor over the bridge, so a picture placed from disk never passes through the model's context the way a base64 argument does.</remarks>
public interface IImageFiles
{
    /// <summary>Reads the picture at <paramref name="path"/> and returns its bytes as base64 without the <c>data:</c> prefix.</summary>
    /// <exception cref="InvalidOperationException">The file is missing, is not a raster Figma accepts, or is larger than <see cref="Editor.ImageLimits.MaxFileMegabytes"/> megabytes.</exception>
    string ReadAsBase64(string path);
}
