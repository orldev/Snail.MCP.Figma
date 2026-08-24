# Troubleshooting

Start with `csharp_diagnose`: it reports the active configuration, whether the bridge is listening, which
files have a live plugin session, the state of the snapshot cache and any closed quota buckets.

## "The plugin is not connected"

Every write tool and every plugin-first read needs a running plugin **in that file**. Open the file in Figma
Desktop and start Plugins → Development → Snail Bridge, then check `figma_get_status`. The plugin attaches to
**every** server it finds on the port range, so another instance cannot steal the connection; if the server
still does not see it, check the plugin's status pill and log panel — see [Plugin setup](plugin-setup.md).

## `ERR_CONNECTION_REFUSED` lines in the DevTools console

Chrome prints its own `net::ERR_CONNECTION_REFUSED` line for every probe of a closed port — no JavaScript can
suppress it. With at least one live connection the plugin does not probe at all, so a quiet console is the
normal connected state. The lines appear only while **no** server is running: every 3 seconds for the first
half minute after the plugin loads (or after the last server goes away), then once per 30 seconds. That is the
discovery loop waiting for a server to start, not a failure — the Pause button silences it completely.

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

## The server reports an old state after a rebuild

A running process keeps the assembly it started with. Restart the MCP server in your client; `figma_reconnect`
and `figma_reload_plugin` only restart the plugin inside Figma, not the server process.

## Nothing works and stdout looks wrong

The server speaks JSON-RPC over stdio, so nothing but protocol messages may reach stdout; logs go to stderr.
If a client reports a parse error, capture stderr and check for a stray write. The repository guards this with
`StdioContractTests`, which runs the built server as a real process and fails on the first non-JSON line.
