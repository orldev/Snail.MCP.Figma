# Architecture

The server is a hexagon: the core knows nothing about REST, WebSockets or SQLite, and the MCP tools see
ports only. Folder = layer = namespace.

| Layer | Holds |
|-------|-------|
| `Domain` | `NodeId`, `DocumentLocator`, `PublishedSourceException` with `SourceFailure`, editor exceptions, `RingBuffer` |
| `Application/Ports` | the published source split by intent, the live editor, the snapshot stores, diagnostics |
| `Application` | the read pipeline, snapshot policy, sanitizer, tokens, extraction, version diffing |
| `Adapters/Rest` | `RestDocumentGateway`, `QuotaGate` — the only place that speaks HTTP |
| `Adapters/Editor` | the WebSocket hub, command router, file registry, the agents and their slots, server-side sibling discovery, embedded read scripts, capture store |
| `Adapters/Persistence/Sqlite` | the three snapshot stores and the cache file, prepared on first use and shared by every server process |
| `Tools` | the MCP driving adapter: 141 tools and the prompt catalog |
| `Extensions` / `Hosting` | the composition root and the process lifetime |

## Two channels

**READ is plugin-first.** While a plugin session exists for the file, reads go through it: zero REST
requests, zero quota, and the current state of the canvas including unsaved edits. Without a session the
server serves a cached snapshot while it is fresh (`Cache.FreshnessSeconds`, 300 s by default), and only then
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
them. When the target's plugin disconnects, the target stays with its file: a command then fails and names that
file, and the agent's next command after the plugin returns goes there again. It is never handed to the file
left behind, which belongs to another agent as often as not.

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
panel who of its agents works in that file, and the panel sends a remark only to them; the server, in turn,
queues a remark only for agents whose target is that file. With two agents in one file the panel lets the human
pick the addressee by name. The panel also tells every server who else has claimed the file (`PEERS`), which is
how `figma_navigate` can name the other agents: servers never see each other.

The bridge speaks two protocols, and each side says which in its first message (`SERVER_HELLO`, `FILE_INFO`).
In protocol 1 a connection is one agent: `TARGET_BOUND` says whether it works in the file and `REMARKS_WAITING`
how many remarks wait. In protocol 2 a connection carries every agent of its server: `AGENTS` lists them by id and
name with `bound` and `waiting` each, and `BUILD_STOP`, `REMARKS_CLEAR` and every command name their `agent`. Each
side answers the other in the older protocol until told otherwise, so a panel and a server of different ages still
understand each other.

## One server for several agents

With `SNAIL_MCP_FIGMA_TRANSPORT=Http` one process serves every agent on the machine over Streamable HTTP, instead of
a process per client over stdio. What a process holds falls into two kinds, and the split follows it.

- **Facts about a file are kept once.** The open files and their sessions — selection, current page, document and
  metadata changes, console lines, the variables snapshot — live in `OpenDocumentRegistry`, along with the snapshot
  cache, the REST gateway and its quota breaker. They read the same whoever asks.
- **An agent's intent is its own.** An `AgentSlot` holds the target, the file last named, the remarks left for the
  agent in each file and its read position in each console. Clearing the console moves that position rather than
  emptying a buffer another agent reads. `AgentRoster` keeps the slots.

Over stdio there is one slot, named after the launch folder, so both ways run the same code. On a shared server every
MCP session opens a slot of its own, and every request has to belong to one. The 2026-07-28 revision of the protocol has
no sessions: its requests carry nothing that tells one window of a client from another, and the server cannot ask a
window for its folder. Claude Code opens with that revision and falls back to `initialize` when it is refused, so the
server refuses it (`-32022`) and each window gets a session of its own. A call filter serves each tool call as its session's agent, and services built once,
such as the gateway, ask the roster whose call is in hand each time rather than holding an agent. The agent is named
after the first folder its client lists as an MCP root, otherwise after the client's name, otherwise `agent N`. Two
sessions of one project — a second conversation, or a window that reconnected before its old session timed out — are
`Keynex` and `Keynex 2` in the server's log, and one agent everywhere else: the panel shows the project once, its
remarks wait in one queue that whichever session reads first takes, and neither session lists the other among the
agents working beside it.

**Commands take turns.** The plugin runs on Figma's one plugin thread and starts every command it is given. The
router keeps one command per agent in flight per file, so one agent's long script cannot push another's short read
behind a dozen of its own. The timeout starts when a command leaves its agent's queue, so it is never spent waiting
in the plugin behind the agent's own earlier commands. Letting a few reads of one agent overlap was tried and gained
nothing: Figma renders one export at a time, and three screenshots took as long together as one after another. An
agent whose session ends gives up the commands still waiting. A connection that drops fails its waits at once
instead of letting them run into their timeouts.

**Failure stays where it happened.** A plugin message the hub cannot read is logged and skipped, never allowed to
end the connection every agent in that file shares. A background service that fails is left stopped rather than
allowed to stop the host. launchd brings a process that falls back within seconds. The panels reconnect by
themselves, and a client opens a new session on the 404 for the old one. The agent's file is written to
`agents.json` in the data folder whenever it chooses one and when the server stops. An agent that comes back from the
same folder gets its file again without a navigate, while the record is less than an hour old or the file is
connected. A command that was in flight when the process fell is lost and has to be repeated.

**The two ports stay apart.** MCP answers on `127.0.0.1:9233` and refuses any request with an `Origin` header: a
browser always sends one, an agent's client never does. The bridge keeps its own port in 9223–9232 and its own
origin check, which admits Figma. Each route is bound to its port. A refusal for the whole host, as the shared
servers without a bridge map it, would have cut off the plugin, which always sends `Origin`.

A session nobody has used for 15 minutes ends, and its agent leaves the panel: a VS Code window that was killed
never says goodbye.

## Who the bridge trusts

Figma runs a plugin's window with Origin `null`, and any web page can take that origin in a sandboxed iframe, so the
origin check cannot tell the plugin from a page posing as it. What a page cannot do is read a file on disk. The first
server that starts on the machine writes a secret into the data folder (`bridge.token`, readable by its owner alone,
created exclusively so two servers starting together agree on one), and the installer writes it into the panel it
copies into `plugin/`. The panel sends it back in `FILE_INFO`; a panel without it — an older bundle, or a page — is told
an update is waiting, which is what the human sees, and the connection is closed. A reply to a command is taken only
from the connection the command was sent on: request ids are a counter and a clock, easy to guess.

The bridge reads a message of at most 128 MB — a screenshot of a whole page at 4x is about 70 MB in base64 — and drops
a longer one as it arrives, keeping the connection every agent of that file shares. It parses a message from the bytes
it arrived in: decoding them into a string first cost four times the message in allocations. It pings each panel every
20 seconds and gives up a connection whose pong does not come back: a Figma window that froze, or a laptop that slept,
used to leave the file connected with nobody behind it.

## Variables: one shape, a snapshot that can be doubted

REST answers variables as dictionaries keyed by id, the plugin as arrays whose items carry their own id.
`VariablesShape` brings both to the keyed form before anything reads them, and refuses a payload that is
neither: an empty answer is a claim about the file, and read carelessly a plugin array used to come back as
"0 collections" with `ok: true`.

The plugin pushes its variables at start-up and the server keeps them on the session, so a read costs no
socket round trip. The snapshot is believed only while nothing was written since (any command of this server,
any `DOCUMENT_CHANGE`) and while it is younger than thirty seconds — Figma raises no event for a variable
edited in its own panel, so age is the only signal for that. A doubted snapshot is replaced through
`REFRESH_VARIABLES`, the one command that makes the plugin read the file again.

## One read, at most one request

The snapshot cache is a SQLite file, opened in the background as soon as the server starts. Its first open costs up to
a second, nearly all of it macOS looking into the native SQLite library the first time a publication loads it; opened
at start, that second is spent while no call waits.

A read costs at most one REST request, and none at all while the snapshot is fresh. The file version is taken
from the response envelope that already arrived rather than from a separate `depth=1` probe — that probe sits
in the same expensive rate-limit bucket as a full read, so it would double the quota cost instead of saving
it. Precise invalidation comes from the plugin instead.

## Quota circuit breaker

A dropped connection, an HttpClient timeout and a 502, 503 or 504 of Figma's edge are retried for a read, twice, a
second and two seconds apart; a comment is not posted twice. What still fails leaves the REST adapter as the same
`Unavailable` failure as an error Figma answered with, so a cached snapshot is served as `cache-stale` and the agent
reads the cause instead of a bare "an error occurred".

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

## What the catalog tells a host

A client that mounts several servers no longer hands the model every schema: it shows the handshake
instructions and a search box, loads a tool when the model asks for it, and may run a script that calls tools
from a sandbox. The server meets that in three places.

- **Instructions at the handshake.** `initialize` carries the server instructions: where to start
  (`figma_navigate`), which family answers which task, and when one `figma_execute` script beats a chain of
  calls. Claude Code cuts server instructions at 2,048 characters, so the text stays under that and puts
  what matters first; `ServerInstructionsTests` keeps every tool class mentioned, every reference real and
  the length in.
- **Behaviour hints on every tool.** All 141 tools state `readOnlyHint`, `destructiveHint`, `idempotentHint`
  and `openWorldHint` explicitly. Read-only means the tool changes neither the Figma file nor the user's files
  (a capture saved beside the server is a read); destructive means it overwrites or deletes what exists;
  open-world means it may reach the REST API or a URL rather than the connected file only. A host uses them to
  let reads through without a prompt and to ask before a delete, including the calls a script makes from a
  sandbox. The convention tests fail on a tool that leaves a hint silent, on a read-only tool marked
  destructive, and on a `_delete_` tool that is not.
- **An output schema where the answer is stable.** `figma_get_status` and `figma_list_open_files` advertise
  `outputSchema` and answer with `structuredContent` next to the usual text, so a host generating typed stubs
  gets a return type instead of `any`. The other tools keep the text envelope: their answers are Figma trees,
  and a schema of those would be wrong more often than useful.

## Process lifetime

Over stdio the server talks JSON-RPC on stdin and stdout, so every log line goes to stderr — a single stray write to
stdout would break the client. A shared server has no idle exit and logs to the file launchd gives it; the rest of
this section is about stdio. An hour without a tool call and the process exits on its own
(`IdleTimeoutMinutes`, `0` disables it) so that orphaned servers do not pile up. A connected plugin holds the
countdown: while the WebSocket session is open the server is in use, and the full timeout starts over once the
last session drops.
