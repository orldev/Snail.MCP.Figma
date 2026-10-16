using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Snail.MCP.Figma.Hosting;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Host lifetime: the idle watchdog that exits an orphaned server process.</summary>
public static class IdleShutdownRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the activity tracker and, when <paramref name="idleTimeoutMinutes"/> is above zero, the watchdog that stops an idle host.</summary>
        public IServiceCollection AddIdleShutdownWatcher(int idleTimeoutMinutes)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IActivityTracker, ActivityTracker>();

            if (idleTimeoutMinutes <= 0)
            {
                return services;
            }

            services.AddSingleton(new IdleShutdownOptions(
                TimeSpan.FromMinutes(idleTimeoutMinutes),
                TimeSpan.FromMinutes(Math.Min(5, idleTimeoutMinutes))));
            services.AddHostedService<IdleShutdownWatcher>();

            return services;
        }
    }
}
