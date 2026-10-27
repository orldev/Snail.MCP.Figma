using System.Text.Json;
using System.Text.RegularExpressions;
using Snail.MCP.Figma.Application.Extraction.Codebase;
using Snail.MCP.Figma.Application.Tokens;

namespace Snail.MCP.Figma.Tests.Application.Extraction.Codebase;

/// <summary>Synthetic app on disk: React with Tailwind v4, a vendored shadcn layer, light and dark themes and duplicate component names.</summary>
public sealed class SampleAppFixture : IDisposable
{
    public SampleAppFixture()
    {
        Root = Path.Combine(TestFolder.Create(), "app");
        OutDir = Path.Combine(Root, "design-system");

        Write("package.json", """
            {
              "name": "shop-front",
              "dependencies": { "react": "^18.3.0", "react-dom": "^18.3.0", "tailwindcss": "^4.0.1" },
              "devDependencies": { "@radix-ui/react-dialog": "^1.0.0" }
            }
            """);

        Write("components.json", "{}");

        Write("src/styles/globals.css", """
            @import "tailwindcss";

            :root {
              --color-brand-500: #3b82f6;
              --color-brand-600: rgb(37, 99, 235);
              --surface: var(--color-brand-500);
              --radius-md: 8px;
              --font-body: Inter, system-ui, sans-serif;
              --duration-fast: 150ms;
            }

            .dark {
              --surface: #0b1120;
            }

            h1 { font-size: 32px; font-weight: 700; line-height: 40px; }
            .lead { font-size: 20px; line-height: 28px; }

            .panel { border: 1px solid #3b82f6; padding: 8px; }
            .panel-tight { padding: 8px; }
            .panel-wide { padding: 8px; }
            .panel-extra { padding: 8px; border-color: #3b82f6; }
            """);

        Write("src/components/Button.tsx", """
            import * as React from "react";

            interface ButtonProps {
              variant?: "primary" | "ghost";
              size?: string;
              onClick: () => void;
            }

            export function Button({ variant = "primary", onClick }: ButtonProps) {
              return <button className="bg-zinc-900 rounded-lg px-4" onClick={onClick}>go</button>;
            }
            """);

        Write("src/components/legacy/Button.tsx", """
            export function Button() {
              return <button className="bg-zinc-900">legacy</button>;
            }
            """);

        Write("src/components/FollowButton.tsx", """
            import { Button } from "./Button";

            export function FollowButton() {
              return <Button variant="ghost" onClick={() => {}} />;
            }
            """);

        Write("src/components/ui/dialog.tsx", """
            import * as React from "react";
            import * as DialogPrimitive from "@radix-ui/react-dialog";

            const DialogContent = React.forwardRef((props, ref) => (
              <DialogPrimitive.Content ref={ref} className="rounded-lg" {...props} />
            ));

            export { DialogContent };
            """);

        Write("src/pages/Home.tsx", """
            import { Button } from "../components/Button";
            import { FollowButton } from "../components/FollowButton";

            export function Home() {
              return (
                <div className="bg-zinc-900 rounded-lg px-4">
                  <Button variant="primary" size="2xl" onClick={() => {}} />
                  <Button variant="ghost" size="default" onClick={() => {}} />
                  <FollowButton />
                </div>
              );
            }
            """);

        Write("src/app/page.tsx", """
            export default function Page() {
              return <main className="bg-zinc-900" />;
            }
            """);

        Write("src/styles/marks.css", """
            .mark { color: #ff8800; }
            .mark-strong { color: #ff8800; }
            """);

        Write("node_modules/junk/index.tsx", "export function Junk() { return <b>x</b>; }");
        Write("dist/bundle.css", ":root { --should-not-be-mined: #ff0000; }");
    }

    public string Root { get; }

    public string OutDir { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Write(string relativePath, string content)
    {
        var absolute = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);
    }
}

public class CodeWalkerTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app = app;

    [Fact]
    public void CodeWalker_IgnoredDirectories_NeverReachTheScan()
    {
        var walk = RealExtraction.Sources.Walk(_app.Root);

        Assert.DoesNotContain(walk.Files, file => file.RelativePath.Contains("node_modules", StringComparison.Ordinal));
        Assert.DoesNotContain(walk.Files, file => file.RelativePath.StartsWith("dist/", StringComparison.Ordinal));
        Assert.Contains(walk.Files, file => file.RelativePath == "src/components/Button.tsx");
    }

    [Fact]
    public void CodeWalker_FileCapReached_StopsTheWalkAndSaysSo()
    {
        var walk = RealExtraction.Sources.Walk(_app.Root, new WalkOptions { MaxFiles = 2 });

        Assert.True(walk.Files.Count <= 2);
        Assert.True(walk.Skipped.IsOverMaxFiles);
    }

    [Fact]
    public void CodeWalker_ExtensionFilter_KeepsOnlyRequestedExtensions()
    {
        var walk = RealExtraction.Sources.Walk(_app.Root, new WalkOptions { Extensions = [".css"] });

        Assert.All(walk.Files, file => Assert.Equal(".css", file.Extension));
    }
}

public class StackDetectorTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app = app;

    [Fact]
    public void StackDetectors_SampleApp_RecognizesFrameworkStylingAndVendorLayers()
    {
        var walk = RealExtraction.Sources.Walk(_app.Root);
        var package = RealExtraction.Stacks.ReadPackageInfo(_app.Root);

        Assert.Equal("shop-front", package.Name);
        Assert.Contains(DsFrameworks.React, StackDetectors.DetectFrameworks(package, walk.Files));

        var styling = RealExtraction.Stacks.DetectStylingMethods(package, walk.Files);
        Assert.Contains(DsStylingMethods.TailwindV4, styling);

        var libraries = RealExtraction.Stacks.DetectComponentLibraries(_app.Root, package);
        Assert.Contains(libraries, library => library.Name == "shadcn/ui" && library.Kind == "vendored");
        Assert.Contains(libraries, library => library.Name == "Radix UI");
    }
}

public class ComponentInventoryTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app = app;

    private InventoryResult Build() => RealExtraction.Inventory.Build(RealExtraction.Sources.Walk(_app.Root).Files, targetIndex: 0);

    [Fact]
    public void ComponentInventory_ComponentUsed_CountsRealCallSites()
    {
        var inventory = Build();
        var button = inventory.Components.First(c =>
            c.Name == "Button" && c.File == "src/components/Button.tsx");

        Assert.True(button.Usage.TagCount >= 2);
        Assert.True(button.Usage.ImportCount >= 2);
        Assert.Equal(DsClassifications.Bespoke, button.Classification);
    }

    [Fact]
    public void ComponentInventory_PropsPassedAtCallSites_RecordsObservedValues()
    {
        var button = Build().Components.First(c =>
            c.Name == "Button" && c.File == "src/components/Button.tsx");

        Assert.NotNull(button.ObservedProps);
        Assert.Contains("primary", button.ObservedProps!["variant"]);
        Assert.Contains("ghost", button.ObservedProps["variant"]);
    }

    [Fact]
    public void ComponentInventory_PropsInterfaceWithDefaults_KeepsRequiredFlagsAndDefaults()
    {
        var button = Build().Components.First(c =>
            c.Name == "Button" && c.File == "src/components/Button.tsx");

        var variant = button.Props.First(p => p.Name == "variant");
        Assert.False(variant.Required);
        Assert.Equal("\"primary\"", variant.DefaultValue);
        Assert.True(button.Props.First(p => p.Name == "onClick").Required);
    }

    [Fact]
    public void ComponentInventory_ShadcnPath_MarksTheComponentVendored()
    {
        var dialog = Build().Components.First(c => c.Name == "DialogContent");

        Assert.Equal(DsClassifications.Vendored, dialog.Classification);
        Assert.Equal("shadcn/ui", dialog.VendorModule);
    }

    [Fact]
    public void ComponentInventory_SameNameInTwoPlaces_BecomesADuplicateGroup()
    {
        var inventory = Build();

        var group = inventory.DuplicateGroups.First(g => g.Name == "Button");
        Assert.Equal(2, group.Files.Count);
    }

    [Fact]
    public void ComponentInventory_RouteEntryFiles_AreNotPortableComponents()
    {
        var components = Build().Components;

        Assert.DoesNotContain(components, c => c.File == "src/app/page.tsx");

        Assert.Contains(components, c => c.Name == "Home");
    }
}

public class ArchitectureClassifierTests
{
    private static DsComponentEntry Component(string name, string classification = DsClassifications.Bespoke) =>
        new()
        {
            Name = name,
            File = $"src/{name}.tsx",
            Framework = DsFrameworks.React,
            Classification = classification,
        };

    [Fact]
    public void ArchitectureClassifier_DomainNamedComponent_IsReportedAsAUsageOfAGeneric()
    {
        var architecture = ArchitectureClassifier.Classify([Component("FollowButton"), Component("NodeCard")]);

        var specialization = architecture.Specializations.First(s => s.Component == "FollowButton");
        Assert.Equal("Button", specialization.GenericOf);
        Assert.Equal("follow", specialization.Qualifier);

        Assert.Contains("Button", architecture.MissingPrimitives);
        Assert.Contains("Card", architecture.MissingPrimitives);
    }

    [Fact]
    public void ArchitectureClassifier_GenericExists_IsNotReportedAsMissing()
    {
        var architecture = ArchitectureClassifier.Classify([Component("Button"), Component("FollowButton")]);

        Assert.DoesNotContain("Button", architecture.MissingPrimitives);
        Assert.Equal(ArchitectureClassifier.Atom, architecture.Levels["Button"]);
    }

    [Fact]
    public void ArchitectureClassifier_SeveralSuffixesMatch_LongestCanonicalSuffixWins()
    {
        var architecture = ArchitectureClassifier.Classify([Component("ActionBar")]);

        Assert.Equal(ArchitectureClassifier.Molecule, architecture.Levels["ActionBar"]);
        Assert.Empty(architecture.Specializations);
    }

    [Fact]
    public void ArchitectureClassifier_PageShapedName_ReadsAsTemplate()
    {
        var architecture = ArchitectureClassifier.Classify([Component("CheckoutScreen")]);

        Assert.Equal(ArchitectureClassifier.Template, architecture.Levels["CheckoutScreen"]);
    }
}

public class CodeStyleMinerTests
{
    [Theory]
    [InlineData("#ABC", "#aabbcc")]
    [InlineData("rgb(37, 99, 235)", "#2563eb")]
    [InlineData("hsl(217, 91%, 60%)", "#3c83f6")]
    [InlineData("222.2 47.4% 11.2%", "#0f172a")]
    public void CodeStyleMiner_ColorLiterals_CollapseToCanonicalHex(string input, string expected)
    {
        Assert.Equal(expected, CodeStyleMiner.NormalizeColor(input));
    }

    [Fact]
    public void CodeStyleMiner_ModernColorFunctions_PassThroughUnquantized()
    {
        const string value = "oklch(0.7 0.15 250)";
        Assert.Equal(value, CodeStyleMiner.NormalizeColor(value));
    }

    [Theory]
    [InlineData(":root", "light")]
    [InlineData(".dark", "dark")]
    [InlineData("[data-theme='high-contrast']", "high-contrast")]
    [InlineData("@media (prefers-color-scheme: dark)", "dark")]
    [InlineData(".theme-acme", "acme")]
    public void CodeStyleMiner_ThemeSelectors_MapToModeNames(string header, string expected)
    {
        Assert.Equal(expected, CodeStyleMiner.ClassifyThemeHeader(header));
    }

    [Fact]
    public void CodeStyleMiner_RootInsideDarkMediaQuery_IsNotMinedAsLight()
    {
        const string css = """
            @media (prefers-color-scheme: dark) {
              :root { --surface: #000000; }
            }
            """;

        var mined = CodeStyleMiner.MineCssCustomProperties(css, "app.css");

        Assert.All(mined, variable => Assert.Equal("dark", variable.Mode));
    }

    [Fact]
    public void CodeStyleMiner_ScssValueWithParens_StopsAtSemicolon()
    {
        var mined = CodeStyleMiner.MineScssVariables("$shadow: rgba(0, 0, 0, 0.2);", "_vars.scss");

        Assert.Equal("rgba(0, 0, 0, 0.2)", mined.Single().Value);
    }

    [Fact]
    public void CodeStyleMiner_TailwindConfig_IsMinedWithoutExecutingIt()
    {
        const string config = """
            module.exports = {
              theme: { extend: { colors: { "brand-dark": "#101010", brand: '#f05735' } } },
            };
            """;

        var mined = CodeStyleMiner.MineTailwindConfig(config, "tailwind.config.js");

        Assert.Contains(mined, v => v.Name == "brand-dark" && v.Value == "#101010");
        Assert.Contains(mined, v => v.Name == "brand" && v.Value == "#f05735");
    }
}

public class CodeTokenExtractorTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app = app;

    private ExtractTokensResult Extract(int minFrequency = 4) =>
        RealExtraction.Tokens.Extract(
            RealExtraction.Sources.Walk(_app.Root).Files,
            new ExtractTokensOptions { MinFrequency = minFrequency, TargetRoots = [_app.Root] });

    [Fact]
    public void CodeTokenExtractor_LightAndDarkDeclarations_BecomeOneMultiModeToken()
    {
        var result = Extract();
        var surface = result.Document.AllTokens.First(t => t.SlashPath == "surface");

        Assert.Contains("Light", surface.Values.Keys);
        Assert.Contains("Dark", surface.Values.Keys);
        Assert.NotEqual(surface.Values["Light"].AsText(), surface.Values["Dark"].AsText());
    }

    [Fact]
    public void CodeTokenExtractor_VarReferenceToMinedVariable_BecomesAnAlias()
    {
        var surface = Extract().Document.AllTokens.First(t => t.SlashPath == "surface");

        Assert.True(surface.Values["Light"].IsReference);
        Assert.Equal("color.brand.500", surface.Values["Light"].Reference);
    }

    [Fact]
    public void CodeTokenExtractor_Alias_TakesTheTypeOfItsTarget()
    {
        var document = Extract().Document;

        Assert.Equal(TokenTypes.Color, document.AllTokens.First(t => t.SlashPath == "surface").Type);
        Assert.Equal(TokenTypes.Dimension, document.AllTokens.First(t => t.SlashPath == "radius/md").Type);
        Assert.Equal(TokenTypes.Duration, document.AllTokens.First(t => t.SlashPath == "duration/fast").Type);
        Assert.Equal(TokenTypes.FontFamily, document.AllTokens.First(t => t.SlashPath == "font/body").Type);
    }

    [Fact]
    public void CodeTokenExtractor_RoleAndScaleNames_LandInSemanticAndPrimitiveSets()
    {
        var document = Extract().Document;

        Assert.Contains(document.Sets.First(s => s.Name == "Semantic").Tokens, t => t.SlashPath == "surface");
        Assert.Contains(document.Sets.First(s => s.Name == "Primitives").Tokens, t => t.SlashPath == "radius/md");
    }

    [Fact]
    public void CodeTokenExtractor_AnyToken_CarriesItsProvenance()
    {
        var brand = Extract().Document.AllTokens.First(t => t.SlashPath == "color/brand/500");
        var provenance = CodeTokenExtractor.ReadProvenance(brand);

        Assert.NotNull(provenance);
        Assert.Equal("declared", provenance!.Confidence);
        Assert.Contains(provenance.Sources, source => source.Contains("globals.css:", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeTokenExtractor_RawValueBelowThreshold_IsReportedInsteadOfPromoted()
    {
        var result = Extract(minFrequency: 3);

        Assert.Contains(result.Document.AllTokens, t => t.SlashPath == "spacing/8px");
        Assert.Contains(result.BelowThreshold, entry => entry.Value == "#ff8800");
        Assert.DoesNotContain(result.Document.AllTokens, t => t.SlashPath == "color/extracted/hex-ff8800");
    }

    [Fact]
    public void CodeTokenExtractor_DeclaredColors_AreNotPromotedAgainAsInferred()
    {
        var result = Extract(minFrequency: 2);

        Assert.DoesNotContain(result.Document.AllTokens, t => t.SlashPath == "color/extracted/hex-3b82f6");
    }

    [Fact]
    public void CodeTokenExtractor_EmptyProject_WarnsInsteadOfReturningNothing()
    {
        var result = RealExtraction.Tokens.Extract([]);

        Assert.Empty(result.Document.Sets);
        Assert.Contains(result.Warnings, warning => warning.Contains("No tokens found", StringComparison.Ordinal));
    }
}

public class ProjectAnalyzerTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app = app;

    [Fact]
    public void ProjectAnalyzer_AnalysisWritten_ReadsBackAsAManifest()
    {
        var outDir = Path.Combine(TestFolder.Create(), "out");

        try
        {
            var analysis = RealExtraction.Analyzer.Analyze([_app.Root], new AnalyzeOptions { OutDir = outDir });
            var path = ExtractionManifests.AnalysisPath(outDir);
            RealExtraction.Manifests.Write(path, analysis);

            var reloaded = RealExtraction.Manifests.Read<DsAnalysis>(path);

            Assert.NotNull(reloaded);
            Assert.Equal(analysis.Components.Count, reloaded!.Components.Count);
            Assert.Equal(_app.Root, reloaded.Targets.Single().Root);
            Assert.NotNull(reloaded.Architecture);
            Assert.Contains(reloaded.Targets.Single().Typography, rule => rule.Selector == "h1");
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void ProjectAnalyzer_PureVendorComponents_GetNoPortingStatus()
    {
        var analysis = RealExtraction.Analyzer.Analyze([_app.Root], new AnalyzeOptions { OutDir = _app.OutDir });
        var status = ProjectAnalyzer.InitStatus(analysis);

        var pureVendor = analysis.Components
            .Where(c => c.Classification == DsClassifications.PureVendor)
            .Select(c => c.Name);

        Assert.All(pureVendor, name => Assert.False(status.Components.ContainsKey(name)));
        Assert.All(status.Components.Values, entry => Assert.Equal(DsPortStatuses.Pending, entry.Status));
    }

    [Fact]
    public void ExtractionManifests_FileMissingOrBroken_ReadReturnsNullInsteadOfThrowing()
    {
        Assert.Null(RealExtraction.Manifests.Read<DsAnalysis>(Path.Combine(_app.Root, "nope.json")));
    }
}

public partial class ScaffolderTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app = app;

    private (DsAnalysis Analysis, TokenDocument Document, string OutDir) Prepare()
    {
        var outDir = Path.Combine(TestFolder.Create(), "pkg");
        var analysis = RealExtraction.Analyzer.Analyze([_app.Root], new AnalyzeOptions { OutDir = outDir });
        var tokens = RealExtraction.Tokens.Extract(
            RealExtraction.Sources.Walk(_app.Root).Files,
            new ExtractTokensOptions { TargetRoots = [_app.Root] });

        return (analysis, tokens.Document, outDir);
    }

    [Fact]
    public void DesignSystemScaffolder_FirstRun_WritesCanonicalTokenFilesAndShowcases()
    {
        var (analysis, document, outDir) = Prepare();

        try
        {
            var result = RealExtraction.Scaffolder.Scaffold(analysis, document, new ScaffoldOptions
            {
                OutDir = outDir,
                PackageName = "@acme/design-system",
                Framework = DsFrameworks.React,
                Formats = ["dtcg", "css-vars"],
            });

            Assert.True(File.Exists(Path.Combine(outDir, "tokens", "tokens.json")));
            Assert.True(File.Exists(Path.Combine(outDir, "tokens", "tokens.css")));
            Assert.True(File.Exists(Path.Combine(outDir, "tokens", "index.css")));
            Assert.True(File.Exists(Path.Combine(outDir, "src", "docs", "Tokens.mdx")));
            Assert.True(File.Exists(Path.Combine(outDir, "package.json")));
            Assert.Contains(result.NextSteps, step => step.Contains("npm create storybook", StringComparison.Ordinal));

            var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "package.json")));
            var dev = package.RootElement.GetProperty("devDependencies");
            Assert.True(dev.TryGetProperty("@tailwindcss/vite", out _));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void DesignSystemScaffolder_SecondRun_KeepsHandWrittenFilesButRefreshesTokens()
    {
        var (analysis, document, outDir) = Prepare();

        try
        {
            var options = new ScaffoldOptions
            {
                OutDir = outDir,
                PackageName = "@acme/design-system",
                Framework = DsFrameworks.React,
            };

            RealExtraction.Scaffolder.Scaffold(analysis, document, options);
            File.WriteAllText(Path.Combine(outDir, "README.md"), "hand edits");

            var second = RealExtraction.Scaffolder.Scaffold(analysis, document, options);

            Assert.Equal("hand edits", File.ReadAllText(Path.Combine(outDir, "README.md")));
            Assert.Contains("README.md", second.FilesSkipped);
            Assert.Contains(second.FilesWritten, path => path.EndsWith("tokens.json", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void DesignSystemScaffolder_AwkwardVariantValues_StoryNamesStayValidAndUnique()
    {
        var component = new DsComponentEntry
        {
            Name = "Button",
            File = "src/Button.tsx",
            Framework = DsFrameworks.React,
            Classification = DsClassifications.Bespoke,
            ObservedProps = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["size"] = ["2xl", "default", "sm"],
            },
        };

        var story = DesignSystemScaffolder.StoryScaffold(component);

        Assert.Contains("export const Variant2xl: Story", story);
        Assert.Contains("export const DefaultVariant: Story", story);
        Assert.Single(DefaultStoryPattern.Matches(story));
    }

    [GeneratedRegex(@"export const Default: Story")]
    private static partial Regex DefaultStoryPattern { get; }
}

public class ExtractionVerifierTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app = app;

    [Fact]
    public void ExtractionVerifier_FreshlyScaffoldedPackage_PassesTheGate()
    {
        var outDir = Path.Combine(TestFolder.Create(), "verify");

        try
        {
            var analysis = RealExtraction.Analyzer.Analyze([_app.Root], new AnalyzeOptions { OutDir = outDir });
            var tokens = RealExtraction.Tokens.Extract(
                RealExtraction.Sources.Walk(_app.Root).Files,
                new ExtractTokensOptions { TargetRoots = [_app.Root] });

            RealExtraction.Scaffolder.Scaffold(analysis, tokens.Document, new ScaffoldOptions
            {
                OutDir = outDir,
                PackageName = "@acme/design-system",
                Framework = DsFrameworks.React,
                Formats = ["dtcg", "css-vars"],
            });

            var status = ProjectAnalyzer.InitStatus(analysis);
            var report = RealExtraction.Verifier.Verify(outDir, analysis, status);

            Assert.True(report.HasPassed, string.Join(" | ", report.Checks
                .Where(check => check.Status == "fail")
                .Select(check => $"{check.Name}: {string.Join(", ", check.Details)}")));
            Assert.True(report.IsFigmaRoundTripReady);
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractionVerifier_TokensFileMissing_FailsWithAnActionableHint()
    {
        var outDir = Path.Combine(TestFolder.Create(), "empty");
        Directory.CreateDirectory(outDir);

        try
        {
            var report = RealExtraction.Verifier.Verify(outDir, analysis: null, status: null);

            Assert.False(report.HasPassed);
            Assert.Contains(report.Checks, check =>
                check.Status == "fail" && check.Details.Any(d => d.Contains("figma_ds_extract_tokens", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractionVerifier_DanglingAlias_FailsTheImportReadinessCheck()
    {
        var outDir = Path.Combine(TestFolder.Create(), "dangling");
        Directory.CreateDirectory(Path.Combine(outDir, "tokens"));

        try
        {
            File.WriteAllText(Path.Combine(outDir, "tokens", "tokens.json"), """
                {
                  "color": {
                    "surface": { "$type": "color", "$value": "{color.missing}" }
                  }
                }
                """);

            var report = RealExtraction.Verifier.Verify(outDir, analysis: null, status: null);

            Assert.False(report.HasPassed);
            Assert.Contains(report.Checks, check =>
                check.Name.Contains("reference resolves", StringComparison.Ordinal) && check.Status == "fail");
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }
}
