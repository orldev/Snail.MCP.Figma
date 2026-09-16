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
| `Tools` | the MCP driving adapter: 141 tools and the prompt catalog |
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

## One target per agent

Commands without a file key go to the agent's target. It moves only on the agent's word, `figma_navigate` or
the file it last named in any tool, and never on the human's clicks: with the plugin open in several files and
several agents around, a click in another file was meant for one of them and the server cannot tell which.
The only connected file is the target by itself; several with none named make the write tools refuse and list
them. When the target's plugin disconnects, the only file left takes over; otherwise there is no target until
the agent names one.

## The third channel: the panel

The two channels above carry the agent's intent. A third one carries the human's, and it runs the other way.

MCP is pull-only — a server cannot wake an agent — so the panel cannot deliver a remark by itself. What it
can do is leave one where the agent will look. Typing into the panel field sends `BUILD_STOP` with the words
and the current selection; the server puts it in a queue of twenty (`Domain/Remarks`).

What happens next depends on whether anything is running:

- **During `figma_execute_steps`** the loop checks the queue between steps and stops at a whole step rather
  than half a node. Only remarks left *after* the build began count — one written about the previous result
  must not kill the build that just started, so it stays in the queue instead.
- **In the quiet** the remark waits for `figma_get_remarks`, which is when a human actually reviews a result.
  Reading takes: the same remark never comes back twice. `waitMs` holds the call open for up to a minute, so
  an agent can hear the human within a second instead of at its next turn.

The server answers back over the same socket so the panel never has to guess: `REMARKS_WAITING` carries how
many remarks are still unread, and `BUILD_PROGRESS` carries `step` and `total` — the plugin receives every
step as its own command and cannot count them, so the share can only come from the server. A step of zero
means the build is over.

A remark is addressed, not broadcast. Every server is connected to every panel, so each server tells each
panel whether its agent works in that file (`TARGET_BOUND`), and the panel sends a remark only to the servers
that said yes; the server, in turn, queues a remark only for its target file. With two agents in one file the
panel lets the human pick the addressee by name. The panel also tells every server who else has claimed the
file (`PEERS`), which is how `figma_navigate` can name the other agents: servers never see each other.

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
