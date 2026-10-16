using Snail.MCP.Figma.Application.Extraction.Codebase;

namespace Snail.MCP.Figma.Tests.Chaos;

/// <summary>Agents of one shared server porting a design system side by side, each reporting its own component.</summary>
public class ExtractionConcurrencyChaosTests
{
    /// <summary>Breaks figma_ds_status: every report reads the manifest, changes one entry and writes the whole file back, so reports made at once overwrite each other and finished components fall back to pending.</summary>
    [Fact]
    public async Task DesignSystemExtraction_ParallelProgressReports_KeepsEveryReport()
    {
        var outDir = Path.Combine(TemporaryFolder.Create(), "design-system");
        var names = Enumerable.Range(1, 24).Select(index => $"Component{index}").ToArray();
        var analysis = new DsAnalysis
        {
            OutDir = outDir,
            Components = [.. names.Select(name => new DsComponentEntry { Name = name, Framework = "react", Classification = DsClassifications.Bespoke })],
        };
        RealExtraction.Manifests.Write(ExtractionManifests.AnalysisPath(outDir), analysis);
        var tools = RealExtraction.Tools();

        await Task.WhenAll(names.Select(name => Task.Run(() => tools.StatusAsync(outDir, name, DsPortStatuses.Ported))));

        var progress = RealExtraction.Manifests.Read<DsStatusManifest>(ExtractionManifests.StatusPath(outDir))!;
        Assert.All(names, name => Assert.Equal(DsPortStatuses.Ported, progress.Components[name].Status));
    }
}
