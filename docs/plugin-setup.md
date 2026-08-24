# Plugin setup

The WRITE channel and plugin-first reads need the Figma Desktop app with the bundled bridge plugin running.
If the server itself is not installed yet, start with [Installation](installation.md).

## Install

On its first run the server copies the plugin into `~/.snail-mcp-figma/plugin` (the path follows
`SNAIL_MCP_FIGMA_DATA_DIRECTORY`). `csharp_diagnose` prints the exact location under `plugin.bundledPath`.

In Figma Desktop: **Plugins → Development → Import plugin from manifest…** and pick `manifest.json` from that
directory. This is done once.

## Run it

Open the file you want to work in and start **Plugins → Development → Snail Bridge**. The plugin
window must stay open — closing it ends the session, and write tools will answer that the plugin is not
connected.

The plugin finds servers on ports **9223–9232** health-first: it probes `http://localhost:<port>/health` and
opens a WebSocket only to ports that answered as this bridge. While no server is running it probes every 3
seconds for about half a minute, then falls back to every 30 seconds — so a server started later is picked up
without restarting the plugin. Once at least one connection is live the browser stops probing entirely: every
connected server scans the range from its side and pushes the live set (`PORTS_ALIVE`), which is how
additional servers — for example a second agent session — get attached silently.

The range is written into `plugin/manifest.json` as allowed domains, so changing `Bridge.PortRangeStart` /
`PortRangeEnd` without editing the manifest will leave the plugin unable to reach the server.

## Check the connection

```json
{ "name": "figma_get_status", "arguments": {} }
```

`csharp_diagnose` shows the same from the server side: whether the hub is listening, which files have a live
session and which one is active.

## Several files at once

Every open file that runs the plugin registers its own session. `figma_list_open_files` lists them,
`figma_navigate` selects the one the next commands go to, and `figma_navigate(lockTarget: true)` pins it so a
human switching files in Figma does not move the agent's target. `figma_execute_across_files` runs the same
code in several files in parallel and demands explicit targets, so a fan-out never reaches a pinned file by
accident.
