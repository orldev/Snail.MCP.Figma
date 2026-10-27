# Installation

Everything runs locally: a .NET tool started by your MCP client, plus a plugin inside Figma Desktop.
Nothing is uploaded anywhere, and the only outbound traffic is to `api.figma.com` when a tool actually
needs REST.

## What you need

| | |
|---|---|
| **.NET 10 SDK** | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/10.0). The SDK ships the ASP.NET Core runtime the bridge needs; installing the runtime alone is not enough to run `dotnet tool install` |
| **An MCP client** | Claude Code, Claude Desktop, Cursor, VS Code, Codex, Windsurf — anything that starts an MCP server over stdio |
| **Figma Desktop** | Required for every write tool and for quota-free reads. Any plan, including free. Browser Figma cannot run a development plugin |
| **A Figma token** | Optional — only the REST channel uses it |

macOS, Windows and Linux are all supported. Check the SDK with:

```bash
dotnet --version   # 10.x
```

## Step 1 — install the server

### As a global tool (recommended)

```bash
dotnet tool install -g Snail.MCP.Figma
```

This gives you an `s3l-mcp-figma` command, which is what MCP clients will start. Verify:

```bash
dotnet tool list -g | grep snail   # snail.mcp.figma   <version>   s3l-mcp-figma
```

If the shell answers `command not found`, the tools folder is not on `PATH`:

| OS | Folder | Fix |
|----|--------|-----|
| macOS, Linux | `~/.dotnet/tools` | add `export PATH="$PATH:$HOME/.dotnet/tools"` to `~/.zshrc` or `~/.bashrc` |
| Windows | `%USERPROFILE%\.dotnet\tools` | usually added by the installer; otherwise add it in *Environment Variables* |

Clients do not always inherit your shell `PATH` — GUI apps in particular. If a client cannot start the
server, put the absolute path in `command`: `~/.dotnet/tools/s3l-mcp-figma` (macOS, Linux) or
`C:\Users\<you>\.dotnet\tools\s3l-mcp-figma.exe` (Windows).

### Without installing

.NET 10 can run the package on demand:

```bash
dnx Snail.MCP.Figma --yes
```

In a client configuration that becomes `"command": "dnx"`, `"args": ["Snail.MCP.Figma", "--yes"]`.
The first start downloads the package; later starts are as fast as the installed tool. Pin a version with
`Snail.MCP.Figma@0.1.0` if you do not want it to move.

### From source

```bash
git clone https://github.com/orldev/Snail.MCP.Figma.git
cd Snail.MCP.Figma
dotnet build -c Release
```

The command is then `dotnet` with `"args": ["<repo>/src/bin/Release/net10.0/Snail.MCP.Figma.dll"]`.
The repository also carries `.mcp.json`, which registers a Debug build for Claude Code out of the box.

!!! note "Do not start the server by hand"
    It speaks JSON-RPC over stdio and has no interactive interface — launched from a terminal it just waits.
    The client starts it, and the idle watchdog stops it an hour after the last tool call.

## Step 2 — create a Figma token (optional)

Skip this if you only intend to work in files open in Figma Desktop: the plugin channel needs no token.

In Figma: **avatar → Settings → Security → Personal access tokens → Generate new token**. Copy it right
away — it is shown once. Personal tokens start with `figd_`, OAuth tokens (`figu_`) are accepted as well.

The scopes worth granting, and what each one buys:

| Scope | Endpoints the server calls | Tools |
|-------|---------------------------|-------|
| File content — read | `/v1/files/…`, `/v1/images/…` | REST reads and renders: `figma_get_file_data`, `figma_get_component`, `figma_get_styles`, `figma_get_component_image` |
| File versions — read | `/v1/files/{key}/versions` | `figma_get_file_versions`, `figma_get_file_at_version`, `figma_diff_versions`, `figma_blame_node`, `figma_generate_changelog` |
| Comments — read and write | `/v1/files/{key}/comments` | `figma_get_comments`, `figma_post_comment`, `figma_delete_comment` |
| Library assets — read | `/v1/components/{key}`, `/v1/styles/{key}`, `/v1/teams/{id}/…` | `figma_get_library_component_by_key`, `figma_get_library_style_by_key`, `figma_search_team_components` |
| Variables — read and write | `/v1/files/{key}/variables/…` | REST variables. Figma restricts this API to Enterprise plans; on any other plan read variables through the plugin (`figma_get_token_values`) |

Keep the token in the `env` block of the client configuration or in your shell environment. The server will
read it from a settings file too, but do not put it there — a file is easy to commit by accident.

## Step 3 — register the server with your client

Every example below sets one variable, the token; it can be dropped entirely if you work through the plugin
only. Everything else has a default — see [Configuration](configuration.md).

### Claude Code

```bash
claude mcp add s3l-mcp-figma --scope user \
  --env SNAIL_MCP_FIGMA_ACCESS_TOKEN=figd_... \
  -- s3l-mcp-figma
```

`--scope user` makes it available in every project; use `--scope project` to write it into the repository's
`.mcp.json` instead. `claude mcp list` shows the result, and `/mcp` inside a session shows the live
connection.

### Claude Desktop

Edit `claude_desktop_config.json` and restart the app.

| OS | Path |
|----|------|
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |

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

### Cursor

The same block, in `~/.cursor/mcp.json` for every project or `.cursor/mcp.json` for one.

### VS Code

`.vscode/mcp.json` (per project) or the user-level `mcp.json` — the key is `servers` and the transport is
explicit:

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

### Codex CLI

`~/.codex/config.toml`:

```toml
[mcp_servers.s3l-mcp-figma]
command = "s3l-mcp-figma"
args = []
env = { SNAIL_MCP_FIGMA_ACCESS_TOKEN = "figd_..." }
```

### Windsurf and other clients

Windsurf reads `~/.codeium/windsurf/mcp_config.json`, and most other clients use the same `mcpServers`
shape as Claude Desktop: a `command`, optional `args`, and an `env` map.

### If you did not install the global tool

Replace the command with the form matching your choice:

| Install | `command` | `args` |
|---------|-----------|--------|
| Global tool | `s3l-mcp-figma` | — |
| `dnx` | `dnx` | `["Snail.MCP.Figma", "--yes"]` |
| From source | `dotnet` | `["<repo>/src/bin/Release/net10.0/Snail.MCP.Figma.dll"]` |

## Step 4 — install the Figma plugin

Writes and plugin-first reads need the bundled plugin running in Figma Desktop. On its first start the
server unpacks it into `~/.s3l-mcp-figma/plugin` — so let the client start the server once before this
step, and read the exact path from `csharp_diagnose` under `plugin.bundledPath`.

1. **Plugins → Development → Import plugin from manifest…**
2. Pick `manifest.json` from that folder. This is done once.
3. Open a file and run **Plugins → Development → Snail Bridge**; keep the plugin window open.

The plugin discovers the server itself on ports 9223–9232, so the start order does not matter and several
agent sessions attach to the same plugin. The full picture, including multi-file sessions:
[Plugin setup](plugin-setup.md).

## Step 5 — verify

Ask your assistant to run these two:

| Tool | Answers |
|------|---------|
| `csharp_diagnose` | the effective configuration, whether the bridge listens, live plugin sessions, the snapshot cache, quota buckets, whether the token is valid |
| `figma_get_status` | the same connection seen from Figma: which files have a session and which one is active |

Then paste a file link and ask for something cheap, such as the list of pages
(`figma_get_file_data` with `depth: 1`).

## One server for every agent

By default every client session starts a server of its own: four agents in Figma Desktop are four processes, and four
panels' worth of connections in every file. With several agents working at once, run one server for all of them
instead. stdio stays the default; the shared server is opt-in.

Measured with four sessions on an Apple Silicon Mac:

| | Framework-dependent (`dotnet …dll`) | Native |
|---|---|---|
| Four stdio processes | 359 MB, 135 ms from start to `initialize` | 160 MB, 82 ms |
| One shared server | 100 MB | 40 MB, a new session answered in about a millisecond |

1. **Publish and install the job.** From a clone of the repository:

    ```bash
    dotnet publish src -c Release -r osx-arm64 -p:SnailMcpInstall=true
    ```

    This publishes the native server into `~/.s3l-mcp/Snail.MCP.Figma/` and writes the launchd job `s3l.mcp.figma.plist` beside it,
    from `deploy/s3l.mcp.figma.plist`. The first time, it prints the two commands that load the job:

    ```bash
    cp ~/.s3l-mcp/Snail.MCP.Figma/s3l.mcp.figma.plist ~/Library/LaunchAgents/
    launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/s3l.mcp.figma.plist
    ```

    Later installs restart a loaded job by themselves — as long as the loaded job is the one they write. Edit the
    copy in `~/Library/LaunchAgents/` and they stop restarting it, printing the commands that would load the new
    job over your edits instead.

2. **Give the server your settings in its settings file, not in the job.** The job runs with
   `SNAIL_MCP_FIGMA_TRANSPORT=Http` and nothing else. Put the token into `~/.config/s3l-mcp-figma/config.json`,
   readable by you alone:

    ```bash
    mkdir -p ~/.config/s3l-mcp-figma
    printf '{ "AccessToken": "figd_..." }\n' > ~/.config/s3l-mcp-figma/config.json
    chmod 600 ~/.config/s3l-mcp-figma/config.json
    launchctl kickstart -k gui/$(id -u)/s3l.mcp.figma
    ```

    The server reads the file itself, so the same file serves stdio sessions too, and on Linux or Windows the service
    needs nothing of its own. Any other setting goes there as well — `"DataDirectory"` when the plugin was imported
    into Figma from a folder other than `~/.s3l-mcp-figma`; see [Configuration](configuration.md#settings-file).

3. **Register it as an HTTP server** in every client, in place of the stdio entry:

    ```json
    {
      "mcpServers": {
        "s3l-mcp-figma": {
          "type": "http",
          "url": "http://127.0.0.1:9233/mcp"
        }
      }
    }
    ```

4. **Restart the plugin** in each open file once (Plugins → Development → Snail Bridge). The panel learns the new
   protocol from the server; nothing has to be imported again.

The server listens on loopback only and refuses any request that carries an `Origin` header, so no web page can
reach it. Each client session is an agent of its own, named in the panel after the folder it works in, and a
session nobody has used for 15 minutes ends. Logs go to `~/Library/Logs/Snail.MCP.Figma.log`; once it passes 64 MB the
server moves it to `Snail.MCP.Figma.1.log` and starts it over, so the two together stay under 128 MB. To go back to
stdio, `launchctl bootout gui/$(id -u)/s3l.mcp.figma` and restore the stdio entry.

## Updating

A native publication holds the program itself, `~/.s3l-mcp/Snail.MCP.Figma/current/Snail.MCP.Figma`, and no
`Snail.MCP.Figma.dll`: a stdio registration that ran `dotnet …/Snail.MCP.Figma.dll` from there names the program as its
`command` instead, with no arguments.

```bash
dotnet tool update -g Snail.MCP.Figma
```

Restart the MCP server in your client afterwards — a running process keeps the assembly it started with.
If the update also changed the plugin bundle, its panel shows a *re-import plugin* banner: repeat step 4.

## Uninstalling

```bash
dotnet tool uninstall -g Snail.MCP.Figma
```

Remove the registration from your client's configuration, delete `~/.s3l-mcp-figma` (snapshot cache,
unpacked plugin, screenshots — nothing is stored anywhere else) with `~/.snail-mcp-figma` when it is there, the link the
server left at the old name when it moved the folder, and remove **Snail Bridge** from
Figma's Plugins → Development list.

## When installation goes wrong

| Symptom | Cause and fix |
|---------|---------------|
| `command not found: s3l-mcp-figma` | `PATH` — see step 1, or use the absolute path in `command` |
| The client shows the server but no tools | The process failed to start. Check the client's MCP log; a wrong `command` or a missing .NET 10 is the usual reason |
| `You must install .NET to run this application` | The tool was installed with an older SDK, or only a runtime is present. Install the .NET 10 SDK |
| The plugin sees no server | The server exits after an hour of idling and starts again with the next tool call — ask the assistant for anything, then check the plugin. Ports 9223–9232 must be free |
| A tool answers that the plugin is not connected | Snail Bridge is not running **in that file**; see [Troubleshooting](troubleshooting.md) |
