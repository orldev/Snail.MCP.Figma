# Architecture

The server is a hexagon: the core knows nothing about REST, WebSockets or LiteDB, and the MCP tools see
ports only. Folder = layer = namespace.

| Layer | Holds |
|-------|-------|
| `Domain` | `NodeId`, `DocumentLocator`, `PublishedSourceException` with `SourceFailure`, editor exceptions, `RingBuffer` |
| `Application/Ports` | the published source split by intent, the live editor, the snapshot stores, diagnostics |
| `Application` | the read pipeline, snapshot policy, sanitizer, tokens, extraction, version diffing |
| `Adapters/Rest` | `RestDocumentGateway`, `QuotaGate` — the only place that speaks HTTP |
| `Adapters/Editor` | the WebSocket hub, command router, session registry, server-side sibling discovery, embedded read scripts, capture store |
| `Adapters/Persistence/LiteDb` | the three snapshot stores and the lazily opened database |
| `Tools` | the MCP driving adapter: 139 tools and the prompt catalog |
| `Extensions` / `Hosting` | the composition root and the process lifetime |

## Two channels

**READ is plugin-first.** While a plugin session exists for the file, reads go through it: zero REST
requests, zero quota, and the current state of the canvas including unsaved edits. Without a session the
server serves a LiteDB snapshot while it is fresh (`Cache.FreshnessSeconds`, 300 s by default), and only then
calls REST. Every response carries a `source` field — `plugin`, `cache`, `figma-api` or `cache-stale`.

The path can be forced per call with the `source` parameter (`auto` / `rest` / `plugin`) and switched off
globally with `Bridge.PreferPluginForReads: false`.

**WRITE is plugin-only.** Mutations are confirmed by the plugin's answer, never assumed. After each one the
node and its ancestors are invalidated; the plugin's `DOCUMENT_CHANGE` event drops snapshots at the moment of
the edit. Coordinates always come from the plugin (`node.x` / `node.y`, relative to the parent).

REST stays the only path for what the plugin cannot do: version history and blame, comments, other teams'
libraries, `svg`/`pdf` rendering, and files that are not open in the desktop app.

## One read, at most one request

A read costs at most one REST request, and none at all while the snapshot is fresh. The file version is taken
from the response envelope that already arrived rather than from a separate `depth=1` probe — that probe sits
in the same expensive rate-limit bucket as a full read, so it would double the quota cost instead of saving
it. Precise invalidation comes from the plugin instead.

## Quota circuit breaker

When Figma answers 429 with a long `Retry-After` (a daily plan quota), the endpoint bucket is closed locally
until it expires and further requests to it are refused without a network round trip — a refusal that is
already known is not worth a request, and failed requests appear to count against the limit as well. Buckets
are per endpoint (`files`, `files/nodes`, `images`), so a 429 on one does not silence the others. A successful
response reopens a bucket immediately. The state is visible in `csharp_diagnose` under `rateLimit`.

## Images

`figma_get_component_image` goes plugin → snapshot → REST, and only the last step spends quota. A render link
lives 30 days at Figma and is cached for `Cache.ImageTtlHours` (24 h). The image cache is dropped for the whole
file on any `DOCUMENT_CHANGE`, because what is usually cached is the render of a parent node while the plugin
reports only the nodes that changed directly.

## Process lifetime

The server talks JSON-RPC over stdio, so every log line goes to stderr — a single stray write to stdout would
break the client. An hour without a tool call and the process exits on its own
(`IdleTimeoutMinutes`, `0` disables it) so that orphaned servers do not pile up. A connected plugin holds the
countdown: while the WebSocket session is open the server is in use, and the full timeout starts over once the
last session drops.
