# Plugin setup

The WRITE channel and plugin-first reads need the Figma Desktop app with the bundled bridge plugin running.
If the server itself is not installed yet, start with [Installation](installation.md).

## Install

On its first run the server copies the plugin into `~/.snail-mcp-figma/plugin` (the path follows
`SNAIL_MCP_FIGMA_DATA_DIRECTORY`). `csharp_diagnose` prints the exact location under `plugin.bundledPath`.

In Figma Desktop: **Plugins → Development → Import plugin from manifest…** and pick `manifest.json` from that
directory. This is done once — except when `manifest.json` itself changes, permissions in particular: Figma
grants those at import, so a new permission needs the plugin imported again, not merely restarted. The panel
shows a *re-import plugin* banner when the bundled plugin is newer than the running one.

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

## The panel

The window is one field. Type into it and press Enter to leave a remark for the agent — the current selection
travels with the words, so "this padding is wrong" points at a node rather than at the whole build.

- While `figma_execute_steps` is running, the remark stops it after the current step and comes back in that
  call's own answer.
- In the quiet it waits until the agent calls `figma_get_remarks`. The field says `queued` until that
  happens, and `cancel` takes the queue back.
- During a stepped build the field fills to the share of steps done and reads `step 3 of 12`.

The field speaks only to the agents working in this file, the servers that claimed it, and the number in the
circle counts them rather than every server the panel is connected to. With nobody here the field says so, and
a remark typed anyway stays in the field instead of going nowhere. With two agents or more, a line under the
field names them, `to Vertical.Hub, Keynex, all`, and the underlined one gets the remark; by default that is
the one that last ran a command here.

The circle on the right is a button, not an indicator: it opens the log and the actions — `stop` ends a
running build after the current step, `copy` puts the session log on the clipboard, `pause` disconnects the
plugin from every server so an agent cannot change anything while you work.

## Check the connection

```json
{ "name": "figma_get_status", "arguments": {} }
```

`csharp_diagnose` shows the same from the server side: whether the hub is listening, which files have a live
session and which one is active.

## Several files at once

Every open file that runs the plugin registers its own session, and the panel in each file connects to every
running server. A server therefore sees all your files, and each agent has to say which one it works in.

The target moves only on the agent's word: `figma_navigate` with the file's link claims it, and it stays
claimed until the next `figma_navigate`. A human switching files or clicking around in another file does not
move it: with several agents open, that click was meant for one of them and the server cannot know which.
The only connected file is the target by itself; with several connected and none named, the write tools
refuse and list the files instead of guessing. `figma_list_open_files` shows the sessions, which one is the
target and which other agents work in each. `figma_execute_across_files` runs the same code in several files
in parallel and demands explicit targets, so a fan-out never reaches the target file by accident.

Two agents in one file are allowed. Each is named after the folder its server was started in, which is the
agent's project, and `SNAIL_MCP_FIGMA_BRIDGE__AGENT_NAME` overrides the name. `figma_navigate` and
`figma_get_status` tell an agent who else works in the file, so it can keep to its own page or section:
Figma does not lock nodes, and two agents editing one frame share its undo history.
