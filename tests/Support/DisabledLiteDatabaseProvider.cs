using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Provider that always reports "no database" — bridge tests need no cache.</summary>
public sealed class DisabledLiteDatabaseProvider : ILiteDatabaseProvider
{
    public bool IsStarted => false;

    public string? LastError => "cache disabled in test";

    public LiteDB.LiteDatabase GetDatabase() => throw new InvalidOperationException("cache disabled in test");

    public LiteDB.LiteDatabase? TryGetDatabase() => null;
}
