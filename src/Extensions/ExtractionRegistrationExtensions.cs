using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Workspace;
using Snail.MCP.Figma.Application.Extraction.Codebase;

namespace Snail.MCP.Figma.Extensions;

/// <summary>The design-system extraction from a codebase: the workspace adapter and every step over it.</summary>
public static class ExtractionRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the extraction over the machine's own file system.</summary>
        public IServiceCollection AddDesignSystemExtraction()
        {
            services.AddSingleton<IWorkspace, LocalWorkspace>();
            services.AddSingleton<CodeWalker>();
            services.AddSingleton<ExtractionManifests>();
            services.AddSingleton<StackDetectors>();
            services.AddSingleton<ComponentInventory>();
            services.AddSingleton<CodeTokenExtractor>();
            services.AddSingleton<ProjectAnalyzer>();
            services.AddSingleton<TokenFileWriter>();
            services.AddSingleton<ShowcaseMdx>();
            services.AddSingleton<DesignSystemScaffolder>();
            services.AddSingleton<StorybookPreset>();
            services.AddSingleton<ExtractionVerifier>();
            services.AddSingleton<ComponentPorting>();
            services.AddSingleton<PortingProgress>();
            services.AddSingleton<DesignSystemExtraction>();

            return services;
        }
    }
}
