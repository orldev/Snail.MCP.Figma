using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Snail.MCP.Figma.Hosting;

namespace Snail.MCP.Figma.Tests.Hosting;

/// <summary>The log launchd appends to, kept within a size by the server itself.</summary>
public class LogTrimTests
{
    private static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    private const long Limit = 1024;

    [Fact]
    public void TrimOnce_FileOverTheLimit_MovesItAsideAndStartsOver()
    {
        var path = LogFile();
        File.WriteAllText(path, new string('x', 3000));
        var trim = Trim(path, new FakeTimeProvider());

        trim.TrimOnce();

        Assert.Equal(0, new FileInfo(path).Length);
        Assert.Equal(3000, new FileInfo(LogTrim.PreviousOf(path)).Length);
    }

    [Fact]
    public void TrimOnce_FileWithinTheLimit_IsLeftAlone()
    {
        var path = LogFile();
        File.WriteAllText(path, new string('x', 100));
        var trim = Trim(path, new FakeTimeProvider());

        trim.TrimOnce();

        Assert.Equal(100, new FileInfo(path).Length);
        Assert.False(File.Exists(LogTrim.PreviousOf(path)));
    }

    [Fact]
    public void TrimOnce_NoFileYet_IsNothingToDo()
    {
        var path = LogFile();
        var trim = Trim(path, new FakeTimeProvider());

        trim.TrimOnce();

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(LogTrim.PreviousOf(path)));
    }

    [Fact]
    public void PreviousOf_LogFile_KeepsTheExtension()
    {
        Assert.Equal("/Users/me/Library/Logs/Snail.MCP.Figma.1.log", LogTrim.PreviousOf("/Users/me/Library/Logs/Snail.MCP.Figma.log"));
    }

    /// <summary>launchd's descriptor is opened for appending and outlives the trim: what the server writes next lands at the start of the emptied file, not at the old offset behind a hole of zeros.</summary>
    /// <remarks>Runs where the server is installed and where CI runs: macOS and Linux.</remarks>
    [Fact]
    public void TrimOnce_WriterAppendingThroughAnOpenDescriptor_ContinuesAtTheStart()
    {
        var path = LogFile();
        using var launchd = AppendingDescriptor.Open(path);
        launchd.Write(new string('x', 2000));
        var trim = Trim(path, new FakeTimeProvider());

        trim.TrimOnce();
        launchd.Write("after the cut\n");

        Assert.Equal("after the cut\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task LogTrim_FileGrowsPastTheLimitAfterStart_IsTrimmedOnTheNextTick()
    {
        var path = LogFile();
        var time = new FakeTimeProvider();
        var trim = Trim(path, time);

        await trim.StartAsync(CancellationToken.None);
        File.WriteAllText(path, new string('x', 3000));
        await TickUntilAsync(time, () => new FileInfo(path).Length == 0);

        Assert.Equal(3000, new FileInfo(LogTrim.PreviousOf(path)).Length);

        await trim.StopAsync(CancellationToken.None);
    }

    private static string LogFile() => Path.Combine(TemporaryFolder.Create(), "Snail.MCP.Figma.log");

    private static LogTrim Trim(string path, TimeProvider time) =>
        new(new LogTrimOptions(path, Limit, Cadence), time, NullLogger<LogTrim>.Instance);

    /// <summary>Moves the clock a cadence at a time until the condition holds.</summary>
    /// <remarks>The service runs on the thread pool, so its timer may not exist yet when the test first moves the clock; a single move could fall before it and be lost.</remarks>
    private static async Task TickUntilAsync(FakeTimeProvider time, Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            time.Advance(Cadence);
            await Task.Delay(15);
        }

        Assert.True(condition(), "the file was not trimmed on the tick");
    }

    /// <summary>A file opened the way launchd opens StandardErrorPath: write-only and appending.</summary>
    /// <remarks>The file is created first: <c>open</c> is variadic, and on Apple silicon a mode passed as a fixed argument arrives as garbage.</remarks>
    private sealed class AppendingDescriptor(int descriptor) : IDisposable
    {
        public static AppendingDescriptor Open(string path)
        {
            File.WriteAllText(path, string.Empty);

            var flags = OperatingSystem.IsMacOS() ? WriteOnly | MacAppend : WriteOnly | LinuxAppend;
            var descriptor = open(path, flags);

            Assert.True(descriptor >= 0, $"open failed with errno {Marshal.GetLastWin32Error()}");

            return new AppendingDescriptor(descriptor);
        }

        public void Write(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var written = write(descriptor, bytes, bytes.Length);

            Assert.Equal(bytes.Length, written);
        }

        public void Dispose() => close(descriptor);

        private const int WriteOnly = 0x0001;

        private const int MacAppend = 0x0008;

        private const int LinuxAppend = 0x0400;

        [DllImport("libc", SetLastError = true)]
        private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc")]
        private static extern nint write(int descriptor, byte[] buffer, nint count);

        [DllImport("libc")]
        private static extern int close(int descriptor);
    }
}
