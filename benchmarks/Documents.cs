using System.Text.Json.Nodes;

namespace Snail.MCP.Figma.Benchmarks;

/// <summary>Figma responses of a realistic size, built the same way on every run so that the numbers compare.</summary>
public static class Documents
{
    /// <summary>A built document as the server holds one: parsed from the JSON Figma sent, not assembled in code.</summary>
    public static JsonObject Parsed(JsonObject built) => JsonNode.Parse(built.ToJsonString())!.AsObject();

    /// <summary>A file of three pages, twenty frames each, ten layers per frame: the shape a REST read returns.</summary>
    public static JsonObject File()
    {
        var pages = new JsonArray();

        for (var page = 0; page < 3; page++)
        {
            var frames = new JsonArray();

            for (var frame = 0; frame < 20; frame++)
                frames.Add(Frame($"{page}:{frame}"));

            pages.Add(new JsonObject { ["id"] = $"0:{page}", ["name"] = $"Page {page}", ["type"] = "CANVAS", ["children"] = frames });
        }

        return new JsonObject
        {
            ["name"] = "Benchmark file",
            ["document"] = new JsonObject { ["id"] = "0:0", ["name"] = "Document", ["type"] = "DOCUMENT", ["children"] = pages },
        };
    }

    /// <summary>A component set of forty variants with properties and bound variables; <paramref name="revision"/> renames, rebinds and adds.</summary>
    public static JsonObject ComponentSet(int revision)
    {
        var variants = new JsonArray();
        var count = 40 + (revision * 4);

        for (var variant = 0; variant < count; variant++)
        {
            var binding = (variant + revision) % 7;

            variants.Add(new JsonObject
            {
                ["id"] = $"5:{variant}",
                ["name"] = $"Size=Size{variant % 4}, State=State{variant % 10}",
                ["type"] = "COMPONENT",
                ["boundVariables"] = new JsonObject
                {
                    ["fills"] = new JsonArray(Alias($"VariableID:9:{binding}")),
                    ["paddingLeft"] = Alias($"VariableID:8:{variant % 3}"),
                },
                ["children"] = new JsonArray(Layer($"5:{variant}", 0), Layer($"5:{variant}", 1)),
            });
        }

        var definitions = new JsonObject();

        for (var property = 0; property < 12; property++)
        {
            var renamed = revision > 0 && property % 5 == 0 ? "Renamed" : "Property";

            definitions[$"{renamed}{property}#{property}:0"] = new JsonObject
            {
                ["type"] = property % 3 == 0 ? "BOOLEAN" : "TEXT",
                ["defaultValue"] = property % 3 == 0 ? revision == 0 : $"Label {property} {revision % 2}",
            };
        }

        return new JsonObject
        {
            ["id"] = "4:1",
            ["name"] = "Button",
            ["type"] = "COMPONENT_SET",
            ["description"] = revision == 0 ? "A button" : "A button with sizes",
            ["componentPropertyDefinitions"] = definitions,
            ["children"] = variants,
        };
    }

    /// <summary>A Variables API answer: two collections, two modes, colors, sizes and aliases between them.</summary>
    public static JsonObject Variables()
    {
        var collections = new JsonObject();
        var variables = new JsonObject();

        for (var collection = 0; collection < 2; collection++)
        {
            collections[$"VariableCollectionId:{collection}:1"] = new JsonObject
            {
                ["name"] = collection == 0 ? "Core" : "Semantic",
                ["modes"] = new JsonArray(
                    new JsonObject { ["modeId"] = $"{collection}:0", ["name"] = "Light" },
                    new JsonObject { ["modeId"] = $"{collection}:1", ["name"] = "Dark" }),
            };
        }

        for (var color = 0; color < 120; color++)
        {
            variables[$"VariableID:2:{color}"] = new JsonObject
            {
                ["name"] = $"color/hue{color / 10}/{(color % 10) * 100}",
                ["resolvedType"] = "COLOR",
                ["variableCollectionId"] = "VariableCollectionId:0:1",
                ["description"] = $"Color {color}",
                ["scopes"] = new JsonArray("FILL_COLOR"),
                ["valuesByMode"] = new JsonObject
                {
                    ["0:0"] = Color(color, 0.0),
                    ["0:1"] = Color(color, 0.3),
                },
            };
        }

        for (var size = 0; size < 40; size++)
        {
            variables[$"VariableID:3:{size}"] = new JsonObject
            {
                ["name"] = $"spacing/step{size}",
                ["resolvedType"] = "FLOAT",
                ["variableCollectionId"] = "VariableCollectionId:0:1",
                ["valuesByMode"] = new JsonObject { ["0:0"] = size * 4, ["0:1"] = size * 4 },
            };
        }

        for (var alias = 0; alias < 60; alias++)
        {
            variables[$"VariableID:4:{alias}"] = new JsonObject
            {
                ["name"] = $"semantic/role{alias}",
                ["resolvedType"] = "COLOR",
                ["variableCollectionId"] = "VariableCollectionId:1:1",
                ["valuesByMode"] = new JsonObject
                {
                    ["1:0"] = Alias($"VariableID:2:{alias}"),
                    ["1:1"] = Alias($"VariableID:2:{119 - alias}"),
                },
            };
        }

        return new JsonObject { ["variableCollections"] = collections, ["variables"] = variables };
    }

    private static JsonObject Frame(string id)
    {
        var layers = new JsonArray();

        for (var layer = 0; layer < 10; layer++)
            layers.Add(Layer(id, layer));

        return new JsonObject
        {
            ["id"] = $"1:{id}",
            ["name"] = $"Frame {id}",
            ["type"] = "FRAME",
            ["visible"] = true,
            ["locked"] = false,
            ["opacity"] = 1.0,
            ["blendMode"] = "PASS_THROUGH",
            ["layoutMode"] = "VERTICAL",
            ["itemSpacing"] = 8,
            ["absoluteBoundingBox"] = Box(0, 0, 375, 812),
            ["fills"] = new JsonArray(Solid(1, 1, 1)),
            ["children"] = layers,
        };
    }

    private static JsonObject Layer(string parent, int layer) => new()
    {
        ["id"] = $"2:{parent}:{layer}",
        ["name"] = layer % 2 == 0 ? $"Label {layer}" : $"Icon {layer}",
        ["type"] = layer % 2 == 0 ? "TEXT" : "VECTOR",
        ["visible"] = true,
        ["locked"] = false,
        ["opacity"] = 1.0,
        ["blendMode"] = "PASS_THROUGH",
        ["exportSettings"] = new JsonArray(),
        ["absoluteBoundingBox"] = Box(layer * 10, 0, 24, 24),
        ["absoluteRenderBounds"] = Box(layer * 10, 0, 24, 24),
        ["fillGeometry"] = new JsonArray(new JsonObject { ["path"] = new string('M', 300), ["windingRule"] = "NONZERO" }),
        ["vectorPaths"] = new JsonArray(new JsonObject { ["data"] = new string('C', 300) }),
        ["fills"] = new JsonArray(Solid(0.1, 0.2, 0.3)),
        ["strokes"] = new JsonArray(),
        ["effects"] = new JsonArray(),
    };

    private static JsonObject Box(double x, double y, double width, double height) =>
        new() { ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height };

    private static JsonObject Solid(double red, double green, double blue) => new()
    {
        ["type"] = "SOLID",
        ["blendMode"] = "NORMAL",
        ["color"] = new JsonObject { ["r"] = red, ["g"] = green, ["b"] = blue, ["a"] = 1.0 },
    };

    private static JsonObject Color(int index, double shift) => new()
    {
        ["r"] = (index % 10) / 10.0,
        ["g"] = Math.Abs(shift - ((index / 10) / 12.0)),
        ["b"] = 0.5,
        ["a"] = 1,
    };

    private static JsonObject Alias(string id) => new() { ["type"] = "VARIABLE_ALIAS", ["id"] = id };
}
