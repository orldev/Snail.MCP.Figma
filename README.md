# Snail.MCP.Figma

[![Documentation](https://img.shields.io/badge/docs-orldev.github.io-blue)](https://orldev.github.io/Snail.MCP.Figma/)
[![NuGet](https://img.shields.io/nuget/v/Snail.MCP.Figma?logo=nuget)](https://www.nuget.org/packages/Snail.MCP.Figma)
[![Release](https://github.com/orldev/Snail.MCP.Figma/actions/workflows/publish.yml/badge.svg)](https://github.com/orldev/Snail.MCP.Figma/actions/workflows/publish.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](https://github.com/orldev/Snail.MCP.Figma/blob/master/LICENSE)

A local MCP server that gives your AI assistant **141 tools for reading and editing Figma**. It runs on your
machine, talks to your client over stdio — or, with several agents at once, serves all of them from one process
over HTTP — and reaches Figma two ways: through the **Figma Desktop app**
(a bundled plugin — it sees unsaved edits and spends no API quota) and through the **Figma REST API**
(behind a snapshot cache and a sanitizer that keeps raw Figma JSON out of the model's context).

Point it at a Figma link and ask in plain words: *read this screen and implement it in React*,
*export the variables as Tailwind tokens*, *add a Dark mode*, *what changed in this file this week*,
*build a FigJam board out of this plan*.

- **Read** files, pages, components, variables, styles, renders — with the tree already trimmed to what a model can use.
- **Write** to the canvas: create, move, restyle, rename and delete nodes, instantiate components, run arbitrary Plugin API code.
- **Design tokens** in 11 formats — DTCG, Tokens Studio, CSS variables, SCSS, LESS, Tailwind v3/v4, Style Dictionary, TypeScript, flat and nested JSON.
- **Design ↔ code**: component specs for implementation, parity checks, generated Markdown docs, and design system extraction out of an existing codebase.
- **History**: version diffs, per-node blame, generated changelogs. **Comments**, **libraries**, **FigJam**, **Figma Slides**.

Full tool reference with every parameter and JSON schema:
**[orldev.github.io/Snail.MCP.Figma](https://orldev.github.io/Snail.MCP.Figma/)**

## Requirements

| | |
|---|---|
| **.NET 10 SDK** | The server is a .NET tool. Download: [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **An MCP client** | Claude Code, Claude Desktop, Cursor, VS Code, Codex — anything that speaks MCP over stdio |
| **Figma Desktop** | Needed for every write and for quota-free reads. The free plan is enough |
| **A Figma token** | Optional. Only the REST features need it: version history, comments, other teams' libraries, files that are not open in the desktop app |

Works on macOS, Windows and Linux.

## Install

Five steps: the server, a token, your client, the Figma plugin, a check. The same walkthrough with
per-client paths and fixes for what usually goes wrong lives in
[the installation guide](https://orldev.github.io/Snail.MCP.Figma/installation/).

### 1. Install the server

The recommended way is a global .NET tool — it gives you the `s3l-mcp-figma` command:

```bash
dotnet tool install -g Snail.MCP.Figma
```

If the shell cannot find the command afterwards, add the tools folder to `PATH`
(`~/.dotnet/tools` on macOS and Linux, `%USERPROFILE%\.dotnet\tools` on Windows) and open a new terminal.

Two alternatives:

```bash
dnx Snail.MCP.Figma --yes          # run without installing (.NET 10 SDK), downloads on first use
```

```bash
git clone https://github.com/orldev/Snail.MCP.Figma.git   # build from source
cd Snail.MCP.Figma && dotnet build -c Release
# the command is then: dotnet <repo>/src/bin/Release/net10.0/Snail.MCP.Figma.dll
```

Do not launch the server by hand — it speaks JSON-RPC over stdio and has no interactive interface.
Your MCP client starts and stops it for you.

### 2. Get a Figma token (optional, for the REST features)

In Figma: **your avatar → Settings → Security → Personal access tokens → Generate new token**.
The token starts with `figd_`; OAuth tokens (`figu_`) work too. Grant the scopes for what you plan to use:

| Scope | What it unlocks |
|-------|-----------------|
| File content — read | Reading files, nodes and renders through REST |
| File versions — read | `figma_get_file_versions`, version diffs, `figma_blame_node` |
| Comments — read/write | `figma_get_comments`, `figma_post_comment`, `figma_delete_comment` |
| Library assets — read | Components and styles of other teams' libraries by key |
| Variables — read/write | REST variables; Figma exposes this on Enterprise plans only — through the plugin variables work on any plan |

Keep the token in the environment of the MCP client (below), not in a file in your repository.
Without a token the server still starts and everything the plugin can do keeps working.

### 3. Register it with your client

**Claude Code**

```bash
claude mcp add s3l-mcp-figma --scope user \
  --env SNAIL_MCP_FIGMA_ACCESS_TOKEN=figd_... \
  -- s3l-mcp-figma
```

Check it with `claude mcp list`, or `/mcp` inside a session.

**Claude Desktop** — edit `claude_desktop_config.json`
(macOS: `~/Library/Application Support/Claude/`, Windows: `%APPDATA%\Claude\`) and restart the app:

```json
{
  "mcpServers": {
    "s3l-mcp-figma": {
      "command": "s3l-mcp-figma",
      "env": {
        "SNAIL_MCP_FIGMA_ACCESS_TOKEN": "figd_..."
      }
    }
  }
}
```

**Cursor** — the same block in `~/.cursor/mcp.json` (or `.cursor/mcp.json` for one project).

**VS Code** — `.vscode/mcp.json` uses a `servers` key and an explicit transport:

```json
{
  "servers": {
    "s3l-mcp-figma": {
      "type": "stdio",
      "command": "s3l-mcp-figma",
      "env": { "SNAIL_MCP_FIGMA_ACCESS_TOKEN": "figd_..." }
    }
  }
}
```

**Codex CLI** — `~/.codex/config.toml`:

```toml
[mcp_servers.s3l-mcp-figma]
command = "s3l-mcp-figma"
args = []
env = { SNAIL_MCP_FIGMA_ACCESS_TOKEN = "figd_..." }
```

If you chose `dnx` instead of a global install, the command is `dnx` with
`"args": ["Snail.MCP.Figma", "--yes"]`; for a build from source it is `dotnet` with
`"args": ["<repo>/src/bin/Release/net10.0/Snail.MCP.Figma.dll"]`.

Every other setting is optional — see [Configuration](#configuration).

### 4. Install the Figma plugin

The plugin is what makes writes possible and reads free. On its first run the server unpacks it into
`~/.s3l-mcp-figma/plugin` (ask the agent for `csharp_diagnose` to see the exact path under
`plugin.bundledPath`).

1. Open **Figma Desktop** (the browser cannot run development plugins).
2. **Plugins → Development → Import plugin from manifest…** and pick `manifest.json` from that folder.
3. Open the file you want to work in and run **Plugins → Development → Snail Bridge**.

Keep the plugin window open — closing it ends the session. The plugin finds the server itself on ports
9223–9232, so the order in which you start them does not matter, and several agent sessions can share
one plugin. Details: [Plugin setup](https://orldev.github.io/Snail.MCP.Figma/plugin-setup/).

### 5. Check that it works

Ask your assistant to call `csharp_diagnose` — it reports the configuration, whether the bridge is
listening, which files have a live plugin session and whether the token is valid. `figma_get_status`
answers the same question from the Figma side.

Then paste a Figma link and ask for something small: *"list the pages of this file"*.

### Updating and removing

```bash
dotnet tool update -g Snail.MCP.Figma     # update
dotnet tool uninstall -g Snail.MCP.Figma  # remove
```

After an update the plugin panel shows a *re-import plugin* banner when the bundled plugin changed —
repeat step 4 to pick up the new bundle. Removing `~/.s3l-mcp-figma` deletes the snapshot cache,
the unpacked plugin and saved screenshots; nothing else is stored outside it.

## What you can ask for

Point at a file by pasting its link — every tool takes a `fileUrl`, and after the first call the session
remembers it. Typical requests:

| Ask | What runs |
|-----|-----------|
| "Read the Button component from `<link>` and implement it in React with our tokens" | `figma_get_component_for_development` → `figma_export_tokens` |
| "Export this file's variables as Tailwind v4" | `figma_export_tokens` |
| "Add a Dark mode and fill it from `tokens.dark.json`" | `figma_add_mode`, `figma_import_tokens` |
| "Capture the whole design system into one document" | `figma_get_design_system_kit` |
| "What changed in this file since Monday, as a changelog" | `figma_get_changes_since_version`, `figma_generate_changelog` |
| "Who changed this frame and when" | `figma_blame_node` |
| "Find loose colors on this page that should be tokens" | `figma_lint_design`, `figma_get_selection_colors` |
| "Build the login screen from this spec" | `figma_create_node_from_jsx`, `figma_set_fills`, `figma_set_text` |
| "Turn our component library in this repo into Figma variables and a package" | `figma_ds_analyze` → `figma_ds_extract_tokens` → `figma_ds_scaffold` |
| "Lay this retro out as a FigJam board" | `figjam_create_stickies`, `figjam_create_connector` |

## Tools

141 tools in 18 families. Each name below links to the reference page with parameters and JSON schemas.

- **[Reading](https://orldev.github.io/Snail.MCP.Figma/tools/reading/)** (7) — `figma_get_file_data`, `figma_get_component`, `figma_get_variables`, `figma_get_file_meta`, `figma_get_styles`, `figma_get_component_image`, `figma_get_file_for_plugin`
- **[Writing](https://orldev.github.io/Snail.MCP.Figma/tools/writing/)** (12) — `figma_get_selection`, `figma_move_node`, `figma_resize_node`, `figma_set_fills`, `figma_set_text`, `figma_execute`, `figma_execute_steps`, `figma_get_selection_colors`, `figma_sort_styles`, `figma_commit_undo`, `figma_create_style`, `figma_get_status`
- **[Nodes](https://orldev.github.io/Snail.MCP.Figma/tools/nodes/)** (15) — `figma_clone_node`, `figma_delete_node`, `figma_rename_node`, `figma_set_strokes`, `figma_set_image_fill`, `figma_create_child`, `figma_set_description`, `figma_set_instance_properties`, `figma_create_page`, `figma_create_node_from_svg`, `figma_place_image`, `figma_create_node_from_jsx`, `figma_boolean_operation`, `figma_group_nodes`, `figma_ungroup_node`
- **[Components](https://orldev.github.io/Snail.MCP.Figma/tools/components/)** (9) — `figma_search_components`, `figma_instantiate_component`, `figma_create_component_set`, `figma_analyze_component_set`, `figma_arrange_component_set`, `figma_add_component_property`, `figma_edit_component_property`, `figma_delete_component_property`, `figma_get_component_details`
- **[Variables](https://orldev.github.io/Snail.MCP.Figma/tools/variables/)** (13) — `figma_create_variable_collection`, `figma_delete_variable_collection`, `figma_create_variable`, `figma_update_variable`, `figma_delete_variable`, `figma_rename_variable`, `figma_set_variable_description`, `figma_add_mode`, `figma_rename_mode`, `figma_get_token_values`, `figma_batch_create_variables`, `figma_batch_update_variables`, `figma_setup_design_tokens`
- **[Design tokens](https://orldev.github.io/Snail.MCP.Figma/tools/design-tokens/)** (3) — `figma_export_tokens`, `figma_import_tokens`, `figma_convert_tokens`
- **[Design to code](https://orldev.github.io/Snail.MCP.Figma/tools/design-to-code/)** (5) — `figma_get_component_for_development`, `figma_get_component_for_development_deep`, `figma_extract_style_values`, `figma_check_design_parity`, `figma_generate_component_doc`
- **[Design system kit](https://orldev.github.io/Snail.MCP.Figma/tools/design-system-kit/)** (1) — `figma_get_design_system_kit`
- **[Design system from code](https://orldev.github.io/Snail.MCP.Figma/tools/design-system-from-code/)** (7) — `figma_ds_analyze`, `figma_ds_extract_tokens`, `figma_ds_scaffold`, `figma_ds_setup_storybook`, `figma_ds_extract_component`, `figma_ds_verify`, `figma_ds_status`
- **[Version history](https://orldev.github.io/Snail.MCP.Figma/tools/version-history/)** (7) — `figma_get_file_versions`, `figma_get_file_at_version`, `figma_diff_versions`, `figma_get_changes_since_version`, `figma_generate_changelog`, `figma_blame_node`, `figma_save_version`
- **[Comments](https://orldev.github.io/Snail.MCP.Figma/tools/comments/)** (3) — `figma_get_comments`, `figma_post_comment`, `figma_delete_comment`
- **[Libraries](https://orldev.github.io/Snail.MCP.Figma/tools/libraries/)** (5) — `figma_get_library_component_by_key`, `figma_get_library_variables`, `figma_import_library_variable`, `figma_get_library_style_by_key`, `figma_search_team_components`
- **[FigJam](https://orldev.github.io/Snail.MCP.Figma/tools/figjam/)** (10) — `figjam_create_sticky`, `figjam_create_stickies`, `figjam_create_connector`, `figjam_create_shape_with_text`, `figjam_create_section`, `figjam_create_table`, `figjam_create_code_block`, `figjam_auto_arrange`, `figjam_get_board_contents`, `figjam_get_connections`
- **[Slides](https://orldev.github.io/Snail.MCP.Figma/tools/slides/)** (16) — `figma_list_slides`, `figma_get_slide_content`, `figma_get_slide_grid`, `figma_get_slide_transition`, `figma_get_focused_slide`, `figma_create_slide`, `figma_delete_slide`, `figma_duplicate_slide`, `figma_reorder_slides`, `figma_set_slide_transition`, `figma_skip_slide`, `figma_add_text_to_slide`, `figma_add_shape_to_slide`, `figma_set_slide_background`, `figma_set_slides_view_mode`, `figma_focus_slide`
- **[Slots](https://orldev.github.io/Snail.MCP.Figma/tools/slots/)** (5) — `figma_create_slot`, `figma_get_slots`, `figma_append_to_slot`, `figma_reset_slot`, `figma_add_slot_property`
- **[Quality and annotations](https://orldev.github.io/Snail.MCP.Figma/tools/quality/)** (6) — `figma_lint_design`, `figma_audit_component_accessibility`, `figma_get_annotations`, `figma_set_annotations`, `figma_get_annotation_categories`, `figma_add_annotation_category`
- **[Session and console](https://orldev.github.io/Snail.MCP.Figma/tools/session/)** (16) — `figma_list_open_files`, `figma_navigate`, `figma_execute_across_files`, `figma_reconnect`, `figma_reload_plugin`, `figma_get_console_logs`, `figma_get_remarks`, `figma_clear_console`, `figma_watch_console`, `figma_export_node`, `figma_get_active_users`, `figma_notify`, `figma_scroll_to_node`, `figma_set_file_thumbnail`, `figma_take_screenshot`, `figma_get_text_styles`
- **[Diagnostics](https://orldev.github.io/Snail.MCP.Figma/tools/diagnostics/)** (1) — `csharp_diagnose`

Which tools need what:

| | Works without a token | Works without the plugin |
|---|---|---|
| Reading files, components, styles, variables, renders | yes, with the plugin running | yes, through REST |
| Any write, FigJam, Slides, slots, console, screenshots | yes | no |
| Version history, blame, changelogs, comments, other teams' libraries | no | yes |

## Configuration

Nothing has to be configured — the defaults work. Every setting is an environment variable
(`SNAIL_MCP_FIGMA_` + the field, sections separated by `__`) that goes into the `env` block of your client:

| Variable | Default | Purpose |
|----------|---------|---------|
| `SNAIL_MCP_FIGMA_ACCESS_TOKEN` | — | Figma token, for the REST channel |
| `SNAIL_MCP_FIGMA_DATA_DIRECTORY` | `~/.s3l-mcp-figma` | Snapshot cache, unpacked plugin, screenshots |
| `SNAIL_MCP_FIGMA_IDLE_TIMEOUT_MINUTES` | `60` | Minutes without a tool call before the server exits by itself; `0` disables the watchdog |
| `SNAIL_MCP_FIGMA_SANITIZATION__PROFILE` | `Aggressive` | How hard REST responses are trimmed: `Aggressive` / `Balanced` / `Raw` |
| `SNAIL_MCP_FIGMA_SANITIZATION__DEFAULT_DEPTH` | `3` | Default tree depth of a REST read |
| `SNAIL_MCP_FIGMA_SANITIZATION__DEFAULT_MAX_NODES` | `500` | Node cap of a REST read |
| `SNAIL_MCP_FIGMA_BRIDGE__PREFER_PLUGIN_FOR_READS` | `true` | Read through the plugin when it is connected |
| `SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_START` / `__PORT_RANGE_END` | `9223` / `9232` | The bridge port range — it is also written into `plugin/manifest.json`, so it cannot be changed without editing the manifest |
| `SNAIL_MCP_FIGMA_BRIDGE__REQUEST_TIMEOUT_SECONDS` | `30` | Timeout of one plugin command |
| `SNAIL_MCP_FIGMA_CACHE__ENABLED` | `true` | Keep snapshots of REST reads |
| `SNAIL_MCP_FIGMA_CACHE__FRESHNESS_SECONDS` | `300` | How long a snapshot is served without checking the version |
| `SNAIL_MCP_FIGMA_CACHE__TTL_HOURS` / `__IMAGE_TTL_HOURS` | `24` / `24` | Lifetime of a node snapshot and of a render link |
| `SNAIL_MCP_FIGMA_SCREENSHOTS__RETAIN_COUNT` | `30` | How many screenshots are kept on disk |

A settings file can hold the same fields instead, looked up as `$SNAIL_MCP_FIGMA_CONFIG` →
`./.s3l-mcp-figma.json` → `./s3l-mcp-figma.json` → `~/.config/s3l-mcp-figma/config.json` →
`~/.s3l-mcp-figma.json`, then the same files under the snail name from before the rename (`./.snail-mcp-figma.json` →
`./snail-mcp-figma.json` → `~/.snail-mcp-figma.json`). Environment variables always win. Full sample:
[Configuration](https://orldev.github.io/Snail.MCP.Figma/configuration/).

## Troubleshooting

| Symptom | What it means |
|---------|---------------|
| "The plugin is not connected" | The plugin is not running **in that file**. Open it in Figma Desktop, start Snail Bridge, check `figma_get_status` |
| `ERR_CONNECTION_REFUSED` in the plugin console | The discovery loop looking for a server that is not running yet. It goes quiet as soon as one connects |
| Answers marked `source: "cache-stale"` | Figma was unreachable, so the last snapshot was served. Ask again with `source: "plugin"` |
| 429 with a `Retry-After` of hours | The daily REST quota. The bucket closes locally until it expires; work through the plugin meanwhile |
| Variables return 403 | The REST variables API is Enterprise-only — read them through the plugin (`figma_get_token_values`) |
| The tool list is empty in the client | The command is not on `PATH`. Use the absolute path to `s3l-mcp-figma`, or the `dotnet <dll>` form |

More cases: [Troubleshooting](https://orldev.github.io/Snail.MCP.Figma/troubleshooting/).

## How it works

```
                 ┌── READ ──→ Live Editor (plugin) ──→ LiteDB snapshot ──→ REST ──→ Sanitizer
Claude ── stdio ─┤
                 └── WRITE ─→ EditorSocketHub (Kestrel, ws://127.0.0.1:9223…9232) ─→ ui.html ─→ code.js ─→ figma.*
                                       └─ DOCUMENT_CHANGE ─→ SnapshotInvalidator
```

- **Plugin-first reads.** While the plugin is connected to a file, reads go through it: no REST requests,
  no quota, and unsaved edits are visible. The path used is in the `source` field of every response
  (`plugin` / `cache` / `figma-api` / `cache-stale`) and can be forced per call with `source`.
- **One read costs at most one REST request**, and none while the snapshot is fresh. Invalidation is precise:
  the plugin reports `DOCUMENT_CHANGE` at the moment of the edit.
- **Quota circuit breaker.** A 429 with a long `Retry-After` closes that endpoint locally until it expires,
  so a refusal already known costs no network round trip. State: `csharp_diagnose → rateLimit`.
- **Writes are confirmed by the plugin's answer**, never assumed; the node and its ancestors are invalidated after each one.
- **Watchdog.** An hour without a tool call and the process exits on its own, so orphaned servers do not pile up.
- **One server for several agents.** With `SNAIL_MCP_FIGMA_TRANSPORT=Http` one process under launchd serves every
  agent on the machine instead of a process per client: each MCP session is an agent of its own, named after its
  project folder, with its own target file, remarks and console position, and one command per agent in flight per
  file. See [Installation](https://orldev.github.io/Snail.MCP.Figma/installation/#one-server-for-every-agent).
- **One target per agent.** Commands go to the file the agent named with `figma_navigate`, and a human clicking
  in another file does not move it: with several files and several agents open, the click was meant for one of
  them. The panel sends a remark only to the agents working in its file, and names them when there are two.
- **Built for hosts that load tools on demand.** The handshake carries instructions with a map of the 18
  families; every tool states whether it reads, adds, overwrites or deletes, and whether it may call the REST
  API, so a client can let reads through and ask before a delete; the status tools advertise an output schema.
- **The panel talks back.** Type into the plugin window and the words reach the agent with the layer you had
  selected: during `figma_execute_steps` the remark stops the build after the current step, in the quiet it
  waits for `figma_get_remarks` — which is when a human actually reviews a result. The server tells the panel
  how many remarks are unread and which step of a build is running, so the window never has to guess.

The internals — the hexagonal layout, the snapshot policy, the sanitizer — are described in
[Architecture](https://orldev.github.io/Snail.MCP.Figma/architecture/).

## Development

```bash
git clone https://github.com/orldev/Snail.MCP.Figma.git
cd Snail.MCP.Figma
export SNAIL_MCP_FIGMA_ACCESS_TOKEN=figd_...   # REST tests and manual runs only
dotnet build
dotnet test
```

- .NET SDK 10. Folder = layer = namespace `Snail.MCP.Figma.<Folder>`; the core knows nothing of REST,
  WebSockets or LiteDB, and the tools see ports only. A new external dependency starts with a port in
  `Application/Ports`, then an adapter, then a registration in `Extensions`.
- Code, documentation and every string the model reads are in English. The layer boundary, the prompt
  catalog, the stdio contract and the code style are all guarded by tests.
- `.mcp.json` in the repository root registers a local debug build for Claude Code.
- Docs: `dotnet build src -p:SnailReference=true` regenerates the tool reference from the built server
  (Snail.Sdk; families and titles in `docs/tools.json`), `mkdocs serve -f docs/mkdocs.yml` previews the site
  (`pip install -r docs/requirements.txt`); the theme is our own and lives in `docs/theme/`.
- Workflows call the shared ones of Snail.Sdk. Release: a `v*` tag runs the tests, syncs the version in
  `.mcp/server.json` and pushes the package to NuGet; every push to `master` deploys the documentation.

Repository layout:

- `src/` — the server, one folder per layer: `Domain`, `Application` (with `Ports`), `Adapters`, `Tools`,
  `Extensions`, `Hosting`, `Configuration`.
- `tests/` — xUnit, mirroring `src/`; `tests/Contracts/` holds the guard tests together with the vendored
  Figma API contracts they compare against (`Figma/openapi.yaml`, `Figma/plugin-api.d.ts`).
- `plugin/` — the Figma extension bundle the server installs for import.
- `docs/` — the documentation site: `content/` (the pages; `tools/` and `schemas/` inside it are
  generated), `theme/`, `tools.json` (the tool families) and `mkdocs.yml`.
- `.mcp/server.json` — the MCP registry manifest packed into the NuGet package; `.mcp.json` registers
  the local debug build for Claude Code.

The bundled Figma extension in `plugin/` carries third-party code; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licences

The server code and the `plugin/` bundle are MIT — `LICENSE` and `plugin/LICENSE`
