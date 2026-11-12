using System.Runtime.CompilerServices;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Runs the tests as on the machine the server is made for, whatever machine runs them.</summary>
/// <remarks>
/// The MCP endpoint has no key, so inside a container — <c>DOTNET_RUNNING_IN_CONTAINER=true</c>, which every .NET image
/// sets — the server refuses to answer over HTTP. The tests asserting the loopback endpoint then failed, eleven of them,
/// under BuildBridge's Linux image while green on a Mac and on GitHub's runner. The variable is read from the process
/// by <c>McpEndpoint.Read</c> and inherited by every server a test starts, so it is cleared once, before any test
/// runs; the refusal inside a container is asserted by a test that sets the variable for its own server.
/// </remarks>
internal static class OutsideAContainer
{
    /// <summary>The variable a .NET container image sets.</summary>
    public const string Variable = "DOTNET_RUNNING_IN_CONTAINER";

    [ModuleInitializer]
    internal static void Leave() => Environment.SetEnvironmentVariable(Variable, null);
}
