using System.Collections.Concurrent;
using System.Diagnostics;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>A test's temporary folder, named by the standard's convention <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c> and deleted when the test process exits.</summary>
/// <remarks>
/// A folder named at random proves nothing to a cleaner: 76 of them were found under <c>snail-bridge-tests</c> on 2026-10-16, left by runs
/// that never deleted them. This name carries the process and the second it started, so a folder whose process is gone is a leftover.
/// <c>TestFolder</c> of Snail.Sdk.Mcp.Testing does the same, but is missing from 0.1.0-local.33, which this repository builds on; once
/// a release carries it, this type goes.
/// </remarks>
internal static class TemporaryFolder
{
    private static readonly ConcurrentBag<string> Created = [];

    private static readonly string Prefix = BuildPrefix();

    static TemporaryFolder() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAll();

    /// <summary>A new empty folder directly in the temporary folder.</summary>
    public static string Create()
    {
        var random = Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(Path.GetTempPath(), $"{Prefix}-{random}");

        Directory.CreateDirectory(folder);
        Created.Add(folder);

        return folder;
    }

    private static string BuildPrefix()
    {
        using var process = Process.GetCurrentProcess();
        var started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();

        return $"s3l-test-{Environment.ProcessId}-{started}";
    }

    private static void DeleteAll()
    {
        foreach (var folder in Created)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
