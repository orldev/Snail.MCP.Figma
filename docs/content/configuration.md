# Configuration

The order is defaults → JSON file → environment variables. Environment always wins.

## Environment variables

One prefix, SCREAMING_SNAKE, a section separated by `__`. The variable name is the path to the field in the
settings file: `SANITIZATION__PROFILE` is `Sanitization.Profile`. There are no special variables — the token
is a field like any other.

| Variable | Default | Purpose |
|----------|---------|---------|
| `SNAIL_MCP_FIGMA_ACCESS_TOKEN` | — | Figma personal or OAuth token. Needed by the REST channel only |
| `SNAIL_MCP_FIGMA_CONFIG` | — | Explicit path to the settings file (read before the config is loaded) |
| `SNAIL_MCP_FIGMA_DATA_DIRECTORY` | `~/.snail-mcp-figma` | Snapshot database, bundled plugin, captures |
| `SNAIL_MCP_FIGMA_MODE` | `Local` | Server mode |
| `SNAIL_MCP_FIGMA_IDLE_TIMEOUT_MINUTES` | `60` | Minutes without a tool call before the process exits; the countdown is held while a plugin is connected, `0` disables it |
| `SNAIL_MCP_FIGMA_SANITIZATION__PROFILE` | `Aggressive` | `Aggressive` / `Balanced` / `Raw` |
| `SNAIL_MCP_FIGMA_SANITIZATION__DEFAULT_DEPTH` | `3` | Default tree depth for REST reads |
| `SNAIL_MCP_FIGMA_SANITIZATION__DEFAULT_MAX_NODES` | `500` | Node cap for REST reads |
| `SNAIL_MCP_FIGMA_BRIDGE__PREFER_PLUGIN_FOR_READS` | `true` | Read through the live editor when it is connected |
| `SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_START` | `9223` | First port of the WebSocket bridge |
| `SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_END` | `9232` | Last port of the bridge |
| `SNAIL_MCP_FIGMA_BRIDGE__REQUEST_TIMEOUT_SECONDS` | `30` | Timeout of one plugin command |
| `SNAIL_MCP_FIGMA_CACHE__ENABLED` | `true` | Keep LiteDB snapshots of REST reads |
| `SNAIL_MCP_FIGMA_CACHE__FRESHNESS_SECONDS` | `300` | How long a snapshot is served without checking the version |
| `SNAIL_MCP_FIGMA_CACHE__TTL_HOURS` | `24` | Lifetime of a node snapshot |
| `SNAIL_MCP_FIGMA_CACHE__IMAGE_TTL_HOURS` | `24` | Lifetime of a render link |
| `SNAIL_MCP_FIGMA_SCREENSHOTS__RETAIN_COUNT` | `30` | How many captures are kept on disk |

Enumerations are case-insensitive. An empty value is ignored; an invalid one fails at startup rather than
falling back to a default silently.

## Full client example

```json
{
  "mcpServers": {
    "snail-mcp-figma": {
      "command": "snail-mcp-figma",
      "env": {
        "SNAIL_MCP_FIGMA_ACCESS_TOKEN": "figd_...",
        "SNAIL_MCP_FIGMA_DATA_DIRECTORY": "~/.snail-mcp-figma",
        "SNAIL_MCP_FIGMA_IDLE_TIMEOUT_MINUTES": "60",
        "SNAIL_MCP_FIGMA_SANITIZATION__PROFILE": "Aggressive",
        "SNAIL_MCP_FIGMA_BRIDGE__PREFER_PLUGIN_FOR_READS": "true",
        "SNAIL_MCP_FIGMA_CACHE__ENABLED": "true"
      }
    }
  }
}
```

Only `command` and the token are needed in practice; the rest is shown to make the defaults explicit.

## Settings file

Optional — without it the server runs on defaults. It is looked up along the chain

1. `$SNAIL_MCP_FIGMA_CONFIG`
2. `./.snail-mcp-figma.json`
3. `./snail-mcp-figma.json`
4. `~/.config/snail-mcp-figma/config.json`
5. `~/.snail-mcp-figma.json`

Every field is optional and the values below are the defaults, so an empty `{}` behaves exactly like no file.

```json
{
  "Mode": "Local",
  "DataDirectory": "~/.snail-mcp-figma",
  "IdleTimeoutMinutes": 60,
  "Bridge": {
    "PortRangeStart": 9223,
    "PortRangeEnd": 9232,
    "Path": "/",
    "RequestTimeoutSeconds": 30,
    "PreferPluginForReads": true
  },
  "Cache": {
    "Enabled": true,
    "DatabaseFileName": "cache.db",
    "TtlHours": 24,
    "ExpirationSweepSeconds": 60,
    "ImageTtlHours": 24,
    "FreshnessSeconds": 300,
    "SharedConnection": true,
    "VersionSnapshotEntries": 50
  },
  "Sanitization": {
    "Profile": "Aggressive",
    "LogMetrics": true,
    "DefaultDepth": 3,
    "DefaultMaxNodes": 500
  },
  "Screenshots": {
    "RetainCount": 30
  }
}
```

Do not put the token in the file — keep it in the environment.
