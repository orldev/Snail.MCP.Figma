using Microsoft.Extensions.Hosting;

namespace Snail.MCP.Figma.Hosting;

/// <summary>Keeps the file launchd writes this server's stderr to within a size: when it passes the limit, what it holds moves to a <c>.1.log</c> file beside it and it starts over, without a restart.</summary>
/// <remarks>
/// <para>launchd opens <c>StandardErrorPath</c> once, for appending, and hands the descriptor to the process; nobody rotates the file, and the shared server's grew to 527 MB in 11 days. A rename would not help: the descriptor follows the inode, and the new path stays empty until the next restart. Emptying the file does: a descriptor opened for appending writes at the end, whatever the end has become.</para>
/// <para>One previous file is kept, so the lines before the cut are there when a failure is looked into.</para>
/// </remarks>
public sealed class LogTrim(LogTrimOptions options, TimeProvider timeProvider, ILogger<LogTrim> logger) : BackgroundService
{
    /// <summary>Where the lines before the cut go: <c>Name.log</c> becomes <c>Name.1.log</c>.</summary>
    public static string PreviousOf(string path) => Path.ChangeExtension(path, ".1.log");

    /// <summary>Looks at the file once: moves it aside and empties it when it is over the limit.</summary>
    public void TrimOnce()
    {
        var file = new FileInfo(options.Path);

        if (!file.Exists || file.Length <= options.LimitBytes)
        {
            return;
        }

        var previous = PreviousOf(options.Path);

        try
        {
            File.Copy(options.Path, previous, overwrite: true);
            Empty(options.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.LogNotTrimmed(logger, options.Path, ex.Message);

            return;
        }

        Log.LogTrimmed(logger, options.Path, file.Length >> 20, previous);
    }

    /// <summary>Trims at start, then every cadence, until the host stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TrimOnce();

        using var timer = new PeriodicTimer(options.Cadence, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                TrimOnce();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void Empty(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        stream.SetLength(0);
    }
}
