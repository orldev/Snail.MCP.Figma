# Troubleshooting

Start with `csharp_diagnose`: it reports the active configuration, whether the bridge is listening, which
files have a live plugin session, the state of the snapshot cache and any closed quota buckets.

## "The plugin is not connected"

Every write tool and every plugin-first read needs a running plugin **in that file**. Open the file in Figma
Desktop and start Plugins → Development → Snail Bridge, then check `figma_get_status`. The plugin attaches to
**every** server it finds on the port range, so another instance cannot steal the connection; if the server
still does not see it, open the panel — the circle on the right shows the log — see
[Plugin setup](plugin-setup.md).

## `ERR_CONNECTION_REFUSED` lines in the DevTools console

Chrome prints its own `net::ERR_CONNECTION_REFUSED` line for every probe of a closed port — no JavaScript can
suppress it. With at least one live connection the plugin does not probe at all, so a quiet console is the
normal connected state. The lines appear only while **no** server is running: every 3 seconds for the first
half minute after the plugin loads (or after the last server goes away), then once per 30 seconds. That is the
discovery loop waiting for a server to start, not a failure — the Pause button silences it completely.

## A remark was sent and nothing happened

Nothing in MCP lets a server wake an agent, so a remark left while nothing is running waits until the agent
asks for it with `figma_get_remarks`. The field keeps saying `queued` until it is read. If the agent never
asks, the words simply sit in the queue — that is the protocol, not a fault. Two ways out: the agent can end
its turn with `figma_get_remarks(waitMs: 30000)` and hear you within a second, or you can press `cancel` and
take the queue back.

A remark typed **while** `figma_execute_steps` is running is different: it stops the build after the current
step and comes back inside that call's answer, so it needs no separate read.

## `Expected node, got null`, or the script ran in another file

The target of the commands does not follow the human's clicks, so this no longer happens by itself. It means
the agent works in the wrong file: `figma_get_status` shows the target, and `figma_navigate` with the file's
link sets it. With several files connected and no target chosen, the write tools refuse and list the files
instead of guessing.

## The panel says "no agent is working in this file"

Remarks go only to the servers that claimed this file. Either the agent has not named the file yet, so ask it
to `figma_navigate` there, or it works in another one. The words stay in the field; press Enter again once the
agent is here.

## "The file this agent works in, … is not connected"

The agent chose that file with `figma_navigate`, and its plugin is no longer running there: the window was closed or
the plugin stopped. The target is kept on purpose, so a write never lands in another agent's file. Run Snail Bridge in
that file again, or let the agent call `figma_navigate` with the file it should work in now.

## The panel calls an agent `claude-code` or `agent 2`

A shared server names an agent after the first folder its client lists as an MCP root. A client that lists none keeps
the name it gave in `initialize`, and one without that is numbered. Clients that open with the 2026-07-28 protocol
revision no longer send roots at all.

## The shared server answers 403

The request carried an `Origin` header. Every browser sends one and an agent's client never does, so the server
refuses it: no web page may drive Figma through it. A tool that sends `Origin` on purpose cannot use the shared
server; give it a stdio server instead.

## The shared server does not start

Its log is `~/Library/Logs/Snail.MCP.Figma.log`. An address that is not loopback is refused at start; a busy port
9233 needs another one in `SNAIL_MCP_FIGMA_URL`, in the job and in every client registration.

## Reads return `source: "cache-stale"`

Figma was unreachable — usually the quota — so the last known snapshot was served instead of failing. The
content may be older than the file. Force a fresh read with `source: "plugin"` when the plugin is running.

## 429 and a `Retry-After` of many hours

A daily plan quota, not a burst limit. The endpoint bucket is closed locally until it expires and further
requests are refused without touching the network; `csharp_diagnose` lists the closed buckets under
`rateLimit`. Renders live in the same expensive bucket, so during a block take pictures through the plugin —
`figma_take_screenshot` or `figma_get_component_image` with a live session.

## Variables come back as 403

The Figma Variables REST API is Enterprise-only. Read variables through the plugin instead
(`figma_get_token_values`, or `figma_get_variables` with a live session) — that works on any plan.

## "Not connected" in a few milliseconds, right after a call that worked

Look at how long the refusal took. A call refused within milliseconds never reached the plugin: the socket
had already dropped, and the script in that call is not what dropped it. A call that hangs for tens of seconds
and then times out did reach the plugin, and the plugin may still finish it — read the state again before
repeating anything. `figma_get_status` shows a new `connectionId` and `connectedAt` after a reconnect, and
globals kept between `figma_execute` calls are gone with the old connection.

## The server reports an old state after a rebuild

A running process keeps the assembly it started with. Restart the MCP server in your client; `figma_reconnect`
and `figma_reload_plugin` only restart the plugin inside Figma, not the server process.

## Nothing works and stdout looks wrong

The server speaks JSON-RPC over stdio, so nothing but protocol messages may reach stdout; logs go to stderr.
If a client reports a parse error, capture stderr and check for a stray write. The repository guards this with
`StdioContractTests`, which runs the built server as a real process and fails on the first non-JSON line.
