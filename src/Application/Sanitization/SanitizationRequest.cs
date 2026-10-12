using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Application.Sanitization;

/// <summary>Parameters of one sanitization; everything but the profile is progressive disclosure.</summary>
public sealed record SanitizationRequest
{
    public SanitizationProfile Profile { get; init; } = SanitizationProfile.Aggressive;

    /// <summary>Traversal depth; <c>null</c> means unlimited.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Node count cap; <c>null</c> means unlimited.</summary>
    public int? MaxNodes { get; init; }

    /// <summary>Move repeated styles into the <c>$styles</c> dictionary.</summary>
    public bool ShouldDeduplicateStyles { get; init; } = true;

    /// <summary><see cref="SanitizationProfile.Raw"/> disables all processing.</summary>
    public bool IsPassthrough => Profile == SanitizationProfile.Raw;
}
