using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Snail.MCP.Figma.Hosting;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Host housekeeping: the log file launchd appends to, kept within a size.</summary>
public static class LogTrimRegistrationExtensions
{
    /// <summary>How large the log may grow before it is trimmed: a working day of a busy server, small enough to open in an editor.</summary>
    public static readonly long LogLimitBytes = 64L << 20;

    /// <summary>How often the log is looked at; it is also looked at when the server starts.</summary>
    public static readonly TimeSpan LogTrimCadence = TimeSpan.FromHours(1);

    extension(IServiceCollection services)
    {
        /// <summary>Registers the trim of <paramref name="logFile"/> when one is named; a server whose stderr goes to its client has none.</summary>
        public IServiceCollection AddLogTrim(string? logFile)
        {
            if (string.IsNullOrWhiteSpace(logFile))
            {
                return services;
            }

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(new LogTrimOptions(logFile, LogLimitBytes, LogTrimCadence));
            services.AddHostedService<LogTrim>();

            return services;
        }
    }
}
