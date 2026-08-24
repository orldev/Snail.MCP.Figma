namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Store for captures taken through the editor.</summary>
public interface ICaptureStore
{
    /// <summary>Saves the picture from an editor reply and replaces its data with a file path.</summary>
    JsonObject SaveAndReplace(JsonObject image);
}
