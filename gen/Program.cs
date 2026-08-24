using Snail.MCP.Figma.Documentation;

var output = args.Length > 0 ? args[0] : "docs";

var families = ToolCatalog.Read();

var written = ToolCatalogExporter.Export(families, output);

Console.WriteLine($"{families.Sum(family => family.Tools.Count)} tools in {families.Count} families");

Console.WriteLine($"{written.Count} files written under {output}");
