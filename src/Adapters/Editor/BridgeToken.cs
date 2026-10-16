using System.Security.Cryptography;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>The secret a panel proves it is this machine's installed plugin with: written once into the data folder, stamped into the installed <c>ui.html</c>, sent back in <c>FILE_INFO</c>.</summary>
/// <remarks>
/// Figma runs a plugin's window with Origin <c>null</c>, so the bridge cannot refuse that origin — and any web page can take it in a
/// sandboxed iframe, connect, name a file it read from <c>/health</c>, take that file's commands and hand the agents answers and
/// "remarks" of its own. A page cannot read a file on disk; the installed plugin can, because the installer wrote the secret into it.
/// Every server of the machine shares the data folder, so all of them know the same secret, and one panel serves them all.
/// </remarks>
public sealed class BridgeToken(string value)
{
    /// <summary>What the installer replaces in <c>ui.html</c>; a panel still carrying it was not installed by a server.</summary>
    public const string Placeholder = "__SNAIL_BRIDGE_TOKEN__";

    /// <summary>The name of the file in the data folder.</summary>
    public const string FileName = "bridge.token";

    /// <summary>The secret itself.</summary>
    public string Value { get; } = value;

    /// <summary>Whether the panel sent this machine's secret, compared in constant time.</summary>
    public bool IsMatchedBy(string? offered) =>
        offered is not null &&
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(offered), System.Text.Encoding.UTF8.GetBytes(Value));

    /// <summary>The machine's secret from the data folder, made the first time a server starts there.</summary>
    /// <remarks>Readable by its owner alone. Two servers starting at once may both try to make it: the file is created exclusively, so one of them writes it and the other reads what was written. A move "without overwrite" was tried first and is not atomic on Unix — two servers ended up with two tokens one run in three.</remarks>
    public static BridgeToken LoadOrCreate(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);

        if (Read(path) is { } existing)
        {
            return new BridgeToken(existing);
        }

        Directory.CreateDirectory(dataDirectory);

        if (TryCreate(path) is { } created)
        {
            return new BridgeToken(created);
        }

        return new BridgeToken(ReadWhenWritten(path));
    }

    /// <summary>Creates the file with a new secret, or <c>null</c> when another server created it first.</summary>
    private static string? TryCreate(string path)
    {
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            using var writer = new StreamWriter(path, System.Text.Encoding.ASCII, options);
            writer.Write(secret);

            return secret;
        }
        catch (IOException) when (File.Exists(path))
        {
            return null;
        }
    }

    /// <summary>The secret another server is writing at this very moment, once it is all there.</summary>
    private static string ReadWhenWritten(string path)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (Read(path) is { } written)
            {
                return written;
            }

            Thread.Sleep(10);
        }

        throw new IOException($"The bridge token in {path} is still incomplete after a second.");
    }

    /// <summary>The secret in the file once it is whole: 32 bytes written as 64 hex digits, never a part of them; <c>null</c> while another server still holds the file to write it.</summary>
    private static string? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path).Trim();

            return text.Length == SecretLength ? text : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private const int SecretLength = 64;
}
