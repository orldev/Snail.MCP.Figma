namespace Snail.MCP.Figma.Adapters.Persistence.LiteDb;

/// <summary>Collection names and key scheme; the names are historical and cannot change without a database migration.</summary>
internal static class LiteDbCollections
{
    public const string Nodes = "CachedFigmaNodes";

    public const string Images = "CachedFigmaImages";

    public const string Versions = "FigmaFileVersions";

    public static string NodeKey(string fileKey, string nodeId, string variant) =>
        $"FigmaNodes/{Escape(fileKey)}_{Escape(nodeId)}_{Escape(variant)}";

    public static string ImageKey(string fileKey, string nodeId, string variant) =>
        $"FigmaImages/{Escape(fileKey)}_{Escape(nodeId)}_{Escape(variant)}";

    public static string VersionKey(string fileKey) => $"FigmaFileVersions/{fileKey}";

    private static string Escape(string value) => value.Replace(':', '-');
}
