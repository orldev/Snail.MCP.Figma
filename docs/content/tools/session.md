# Session and console

## <code>figma_<wbr>list_<wbr>open_<wbr>files</code>

Shows every Figma file the plugin is currently running in, which of them is the target of the commands, and which other agents work in each. Useful when several files are open and commands go to the wrong one.

*read-only · repeat-safe · the connected file only.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

<details><summary>Output schema</summary>

```json
{
  "type": "object",
  "properties": {
    "ok": {
      "description": "true on success; a failure comes as text with ok false, error and hint.",
      "type": "boolean"
    },
    "data": {
      "type": [
        "object",
        "null"
      ],
      "properties": {
        "files": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "connectionId": {
                "description": "Identity of the plugin connection.",
                "type": "string"
              },
              "fileKey": {
                "description": "Key of the file, as in its URL.",
                "type": [
                  "string",
                  "null"
                ]
              },
              "fileName": {
                "type": [
                  "string",
                  "null"
                ]
              },
              "currentPage": {
                "type": [
                  "string",
                  "null"
                ]
              },
              "editorType": {
                "description": "figma, figjam or slides.",
                "type": [
                  "string",
                  "null"
                ]
              },
              "pluginVersion": {
                "type": [
                  "string",
                  "null"
                ]
              },
              "connectedAt": {
                "description": "ISO 8601 instant the plugin connected.",
                "type": "string"
              },
              "open": {
                "description": "Whether the connection is still open.",
                "type": "boolean"
              },
              "target": {
                "description": "Whether this file is where the commands of this server go.",
                "type": "boolean"
              },
              "othersHere": {
                "description": "Names of the other agents bound to this file.",
                "type": "array",
                "items": {
                  "type": "string"
                }
              }
            },
            "required": [
              "connectionId",
              "fileKey",
              "fileName",
              "currentPage",
              "editorType",
              "pluginVersion",
              "connectedAt",
              "open",
              "target",
              "othersHere"
            ]
          }
        },
        "activeFileKey": {
          "description": "Key of the target file; null when no file is bound.",
          "type": [
            "string",
            "null"
          ]
        }
      },
      "required": [
        "files",
        "activeFileKey"
      ]
    }
  },
  "required": [
    "ok",
    "data"
  ]
}
```

</details>

## <code>figma_<wbr>navigate</code>

Selects the file the following commands will go to, by a link to a Figma file or its key. The target then stays put: a human switching files in Figma does not move it, only another figma_navigate does. With several files connected and no target chosen, the write tools refuse and list the files — this call is the answer. The response names the other agents working in the same file, so the agent can keep to its own page or section.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrlOrKey` | `string` | yes | — | Link to the Figma file or its key. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrlOrKey": {
      "description": "Link to the Figma file or its key.",
      "type": "string"
    }
  },
  "required": [
    "fileUrlOrKey"
  ]
}
```

</details>

## <code>figma_<wbr>execute_<wbr>across_<wbr>files</code>

Runs the same JavaScript IN SEVERAL Figma files AT ONCE — in parallel, without moving the target. For auditing and editing across a multi-file design system: one check runs over every file and the results are compared with each other — instead of switching the active file and calling figma_execute one by one. In every file the code runs in that file's own plugin sandbox, with the same figma object as figma_execute. The targets must be named explicitly: fileKeys (the list from figma_list_open_files) or allFiles=true. Requires a running plugin in every target file.

*may overwrite or delete · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `code` | `string` | yes | — | JavaScript code, the same as in figma_execute. Example: return figma.root.children.length; |
| `fileKeys` | `string?` | — | `null` | Comma-separated keys of the target files. They come from figma_list_open_files. |
| `allFiles` | `boolean` | — | `false` | Run it over EVERY connected file. Requires explicit consent: the code will reach the target file too. |
| `timeoutMs` | `integer` | — | `10000` | Per-file timeout in milliseconds, at most 30000. A slow file does not hold the others back. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "code": {
      "description": "JavaScript code, the same as in figma_execute. Example: return figma.root.children.length;",
      "type": "string"
    },
    "fileKeys": {
      "description": "Comma-separated keys of the target files. They come from figma_list_open_files.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "allFiles": {
      "description": "Run it over EVERY connected file. Requires explicit consent: the code will reach the target file too.",
      "type": "boolean",
      "default": false
    },
    "timeoutMs": {
      "description": "Per-file timeout in milliseconds, at most 30000. A slow file does not hold the others back.",
      "type": "integer",
      "default": 10000
    }
  },
  "required": [
    "code"
  ]
}
```

</details>

## <code>figma_<wbr>reconnect</code>

Re-reads the list of connected files and reports the target. Call it when the server keeps aiming at a file that is already closed.

*read-only · repeat-safe · the connected file only.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>figma_<wbr>reload_<wbr>plugin</code>

Reloads the plugin UI in Figma. It helps when the plugin stopped answering. Requires the plugin.

*only adds · repeat-safe · the connected file only.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>figma_<wbr>get_<wbr>console_<wbr>logs</code>

The latest console lines from the plugin sandbox. Needed to understand why figma_execute failed or what exactly the plugin did.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `limit` | `integer` | — | `50` | How many of the latest lines to return. |
| `level` | `string?` | — | `null` | Keep only the lines of this level: log, info, warn, error. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "limit": {
      "description": "How many of the latest lines to return.",
      "type": "integer",
      "default": 50
    },
    "level": {
      "description": "Keep only the lines of this level: log, info, warn, error.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  }
}
```

</details>

## <code>figma_<wbr>get_<wbr>remarks</code>

Reads what the human typed in the plugin panel: their words plus the layers they had selected. THIS IS THE CHANNEL FOR the case where the build is finished, the human looked at it and found a defect — call it after finishing a build and before starting the next task, because a remark left while nothing was running reaches nobody otherwise. Reading TAKES the remarks: the same one never comes back, so act on it in the same turn. A remark typed WHILE figma_execute_steps was running is not here — that one stops the build and comes back in its own answer. waitMs holds the call open until somebody writes, up to 60000 ms: nothing in MCP lets a server wake an agent, so a wait here is the only way to hear the human within a second rather than at the next turn. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `waitMs` | `integer` | — | `0` | How long to hold the call open when nothing is waiting yet, in milliseconds, up to 60000. 0 answers straight away; waiting is how the agent hears the human within a second instead of at its next turn. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "waitMs": {
      "description": "How long to hold the call open when nothing is waiting yet, in milliseconds, up to 60000. 0 answers straight away; waiting is how the agent hears the human within a second instead of at its next turn.",
      "type": "integer",
      "default": 0
    }
  }
}
```

</details>

## <code>figma_<wbr>clear_<wbr>console</code>

Clears the console buffer, both on the server and in the plugin. Handy before a new figma_execute run.

*may overwrite or delete · repeat-safe · the connected file only.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>figma_<wbr>watch_<wbr>console</code>

Waits for new lines to appear in the plugin console up to the given timeout. Needed when an edit runs asynchronously and the result arrives with a delay.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `timeoutSeconds` | `integer` | — | `10` | How many seconds to wait for new lines. |
| `minEntries` | `integer` | — | `1` | Wait for at least this many new lines. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "timeoutSeconds": {
      "description": "How many seconds to wait for new lines.",
      "type": "integer",
      "default": 10
    },
    "minEntries": {
      "description": "Wait for at least this many new lines.",
      "type": "integer",
      "default": 1
    }
  }
}
```

</details>

## <code>figma_<wbr>export_<wbr>node</code>

Exports a node through the live editor: SVG returns the markup itself, PDF/PNG/JPG are saved to disk and the path is returned. With deliver=both a PNG or JPG also comes back as image content, which is what a caller outside this machine needs, since the path opens nothing there. No REST quota is spent — this replaces the paid /v1/images render for files open in Figma Desktop. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id; a page id works too. |
| `format` | `string` | — | `SVG` | Format: SVG, PDF, PNG or JPG. |
| `scale` | `number` | — | `1` | Render scale for PNG and JPG. |
| `deliver` | `string` | — | `path` | What comes back: path (the default) saves the picture beside the server and answers with the file path, which suits a human working in the same machine; both answers with the path AND the picture itself as image content, which is what a caller in another process or container needs, since a path there opens nothing. Only PNG and JPG travel as content, and only up to 2 MB — a bigger one still comes back as a path. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id; a page id works too.",
      "type": "string"
    },
    "format": {
      "description": "Format: SVG, PDF, PNG or JPG.",
      "type": "string",
      "default": "SVG"
    },
    "scale": {
      "description": "Render scale for PNG and JPG.",
      "type": "number",
      "default": 1
    },
    "deliver": {
      "description": "What comes back: path (the default) saves the picture beside the server and answers with the file path, which suits a human working in the same machine; both answers with the path AND the picture itself as image content, which is what a caller in another process or container needs, since a path there opens nothing. Only PNG and JPG travel as content, and only up to 2 MB \u2014 a bigger one still comes back as a path.",
      "type": "string",
      "default": "path"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>active_<wbr>users</code>

Who is in the file right now: the token owner (me) and the other live collaborators with their multiplayer colors. Useful before heavy mutations — a human working in the same file deserves figma_commit_undo batches and gentler pacing. Requires the plugin.

*read-only · repeat-safe · may reach the REST API.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>figma_<wbr>notify</code>

Shows a toast inside the Figma window — the way to tell the human at the canvas that a batch of work is done or needs their eyes. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `message` | `string` | yes | — | Toast text shown in the Figma window. |
| `timeoutMs` | `integer` | — | `4000` | How long it stays, in milliseconds. |
| `isError` | `boolean` | — | `false` | Style the toast as an error. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "message": {
      "description": "Toast text shown in the Figma window.",
      "type": "string"
    },
    "timeoutMs": {
      "description": "How long it stays, in milliseconds.",
      "type": "integer",
      "default": 4000
    },
    "isError": {
      "description": "Style the toast as an error.",
      "type": "boolean",
      "default": false
    }
  },
  "required": [
    "message"
  ]
}
```

</details>

## <code>figma_<wbr>scroll_<wbr>to_<wbr>node</code>

Scrolls the user's viewport to a node, switching pages when needed; select=true also selects it. Call it after creating something so the human sees the result without hunting for it. Requires the plugin.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id to bring into view. |
| `select` | `boolean` | — | `false` | Also select the node. |
| `zoom` | `number?` | — | `null` | Zoom level after centering; empty keeps the fitted zoom. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id to bring into view.",
      "type": "string"
    },
    "select": {
      "description": "Also select the node.",
      "type": "boolean",
      "default": false
    },
    "zoom": {
      "description": "Zoom level after centering; empty keeps the fitted zoom.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>file_<wbr>thumbnail</code>

Makes a frame the file cover shown in the project browser; an empty nodeId resets to the default. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string?` | — | `null` | Node whose render becomes the file cover; empty resets to the default. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node whose render becomes the file cover; empty resets to the default.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  }
}
```

</details>

## <code>figma_<wbr>take_<wbr>screenshot</code>

Captures a node or the current page straight from the plugin. Unlike figma_get_component_image, it shows the state RIGHT NOW, including edits made a moment ago — so this is the tool that verifies the result of a mutation. By default the picture is saved to disk and the response carries the file path (image.path) and the metadata: open the file to see it. Pass deliver=both and the picture comes back as image content as well — the way to look at it from another process, at the cost of carrying the bytes in the answer. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string?` | — | `null` | Node id. Empty captures the current page. |
| `format` | `string` | — | `PNG` | Format: PNG (the default), JPG, SVG. |
| `scale` | `number` | — | `1` | Render scale. |
| `deliver` | `string` | — | `path` | What comes back: path (the default) saves the picture beside the server and answers with the file path, which suits a human working in the same machine; both answers with the path AND the picture itself as image content, which is what a caller in another process or container needs, since a path there opens nothing. Only PNG and JPG travel as content, and only up to 2 MB — a bigger one still comes back as a path. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id. Empty captures the current page.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "format": {
      "description": "Format: PNG (the default), JPG, SVG.",
      "type": "string",
      "default": "PNG"
    },
    "scale": {
      "description": "Render scale.",
      "type": "number",
      "default": 1
    },
    "deliver": {
      "description": "What comes back: path (the default) saves the picture beside the server and answers with the file path, which suits a human working in the same machine; both answers with the path AND the picture itself as image content, which is what a caller in another process or container needs, since a path there opens nothing. Only PNG and JPG travel as content, and only up to 2 MB \u2014 a bigger one still comes back as a path.",
      "type": "string",
      "default": "path"
    }
  }
}
```

</details>

## <code>figma_<wbr>get_<wbr>text_<wbr>styles</code>

The text styles of a file read through the plugin: name, size, weight, line height. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

