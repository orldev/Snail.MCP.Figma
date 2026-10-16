using Snail.MCP.Figma.Adapters.Workspace;
using Snail.MCP.Figma.Application.Extraction.Codebase;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>The extraction wired over the machine's own file system, as the server wires it: these tests write real files and read them back.</summary>
internal static class RealExtraction
{
    public static IWorkspace Workspace { get; } = new LocalWorkspace();

    public static CodeWalker Sources { get; } = new(Workspace);

    public static ExtractionManifests Manifests { get; } = new(Workspace);

    public static StackDetectors Stacks { get; } = new(Sources, Workspace);

    public static ComponentInventory Inventory { get; } = new(Sources);

    public static CodeTokenExtractor Tokens { get; } = new(Sources);

    public static ProjectAnalyzer Analyzer { get; } = new(Sources, Stacks, Inventory);

    public static TokenFileWriter TokenFiles { get; } = new(Workspace);

    public static ShowcaseMdx Showcase { get; } = new(Sources);

    public static DesignSystemScaffolder Scaffolder { get; } = new(Workspace, TokenFiles, Showcase);

    public static StorybookPreset Storybook { get; } = new(Workspace, Sources);

    public static ExtractionVerifier Verifier { get; } = new(Workspace);

    public static ComponentPorting Porting { get; } = new(Sources, Manifests);

    public static PortingProgress Progress { get; } = new(Manifests);

    public static DesignSystemExtraction Extraction { get; } =
        new(Workspace, Sources, Analyzer, Manifests, Tokens, TokenFiles, Scaffolder, Storybook, Verifier, Porting, Progress);

    /// <summary>The tools over the real extraction.</summary>
    public static DesignSystemExtractionTools Tools() => new(Extraction, NullLogger<DesignSystemExtractionTools>.Instance);
}
