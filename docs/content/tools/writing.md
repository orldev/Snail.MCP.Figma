# Writing

## <code>figma_<wbr>get_<wbr>selection</code>

What is selected in Figma right now in the target file: the ids, names, types and sizes of the nodes. This is the right first step for 'move this' and 'change the selected one' — the nodeId comes from the response. While no target is chosen it reports the file the human touched last, with its fileKey. Requires a running Snail Bridge plugin.

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

## <code>figma_<wbr>move_<wbr>node</code>

Moves a node. The x and y coordinates are relative to the parent, exactly as the Figma Plugin API understands them. To move it '10 pixels to the right', first read the current position (figma_get_component or figma_get_selection) and add the offset to it. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, for example '1:234'. |
| `x` | `number` | yes | — | New X coordinate relative to the parent. |
| `y` | `number` | yes | — | New Y coordinate relative to the parent. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, for example \u00271:234\u0027.",
      "type": "string"
    },
    "x": {
      "description": "New X coordinate relative to the parent.",
      "type": "number"
    },
    "y": {
      "description": "New Y coordinate relative to the parent.",
      "type": "number"
    }
  },
  "required": [
    "nodeId",
    "x",
    "y"
  ]
}
```

</details>

## <code>figma_<wbr>resize_<wbr>node</code>

Resizes a node. With withConstraints=true Figma recalculates the nested elements by their constraints — usually that is what you want for frames. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, for example '1:234'. |
| `width` | `number` | yes | — | New width in pixels. |
| `height` | `number` | yes | — | New height in pixels. |
| `withConstraints` | `boolean` | — | `true` | Recalculate the children by their constraints. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, for example \u00271:234\u0027.",
      "type": "string"
    },
    "width": {
      "description": "New width in pixels.",
      "type": "number"
    },
    "height": {
      "description": "New height in pixels.",
      "type": "number"
    },
    "withConstraints": {
      "description": "Recalculate the children by their constraints.",
      "type": "boolean",
      "default": true
    }
  },
  "required": [
    "nodeId",
    "width",
    "height"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>fills</code>

Sets the fills of a node. The fills format is an array of Figma Paint in JSON, for example [{"type":"SOLID","color":{"r":1,"g":0,"b":0},"opacity":1}]. Channel components are fractions from 0 to 1, not 0-255. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, for example '1:234'. |
| `fills` | `string` | yes | — | Array of Paint as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, for example \u00271:234\u0027.",
      "type": "string"
    },
    "fills": {
      "description": "Array of Paint as a JSON string.",
      "type": "string"
    }
  },
  "required": [
    "nodeId",
    "fills"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>text</code>

Changes the text in a TEXT node. Careful: when the node belongs to a component instance and the text is driven by a component property, the edit may not apply — use figma_set_instance_properties then. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | TEXT node id, for example '1:234'. |
| `text` | `string` | yes | — | New text. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "TEXT node id, for example \u00271:234\u0027.",
      "type": "string"
    },
    "text": {
      "description": "New text.",
      "type": "string"
    }
  },
  "required": [
    "nodeId",
    "text"
  ]
}
```

</details>

## <code>figma_<wbr>execute</code>

Runs arbitrary JavaScript in the Figma plugin sandbox: the whole figma object (Plugin API) is available. The universal tool for what the specialised commands do not cover. The code runs as the body of an async function; return a value with return. The asynchronous Figma methods (loadFontAsync, getNodeByIdAsync, loadAllPagesAsync) must be awaited. The code runs in the target file: the one figma_navigate chose, or the only connected one. With several files connected and none chosen the call refuses and lists them. Requires the plugin.

*may overwrite or delete · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `code` | `string` | yes | — | JavaScript code. Example: const n = await figma.getNodeByIdAsync('1:2'); return n.name; |
| `timeoutMs` | `integer` | — | `15000` | Execution timeout in milliseconds. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "code": {
      "description": "JavaScript code. Example: const n = await figma.getNodeByIdAsync(\u00271:2\u0027); return n.name;",
      "type": "string"
    },
    "timeoutMs": {
      "description": "Execution timeout in milliseconds.",
      "type": "integer",
      "default": 15000
    }
  },
  "required": [
    "code"
  ]
}
```

</details>

## <code>figma_<wbr>execute_<wbr>steps</code>

Runs short scripts one after another, so a layout grows element by element instead of appearing all at once, and a failure names the step that caused it instead of the whole batch. Each step is a figma_execute body, and each runs on its own: declarations do not survive between steps, so carry what the next step needs on globalThis. Up to 60 steps in one call; globals live on afterwards, so a longer build continues in the next call. Prefer this over one large figma_execute: the sandbox parser chokes on a big nested expression, and there the error says nothing about where. The human can step in from the plugin panel while it runs: the answer then carries stopped=true, their remark and what they had selected, and the steps that did not run are simply sent again once the remark is dealt with. A remark left BEFORE this call does not stop it: that one waits for figma_get_remarks. Build into ONE frame created by the first step: undo cannot merge the steps back together, so a single container is what makes the whole build undoable by deleting it. Requires the plugin.

*may overwrite or delete · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `steps` | `string` | yes | — | JSON array of code strings, run in order: ["globalThis.card = figma.createFrame();", "globalThis.card.resize(320, 180);"]. |
| `delayMs` | `integer` | — | `0` | Pause between steps in milliseconds, up to 2000. 0 runs as fast as the bridge allows; 100 to 200 reads as a build a human can follow. |
| `stopOnError` | `boolean` | — | `true` | Stop at the first failing step, which is the default, or run the rest and report every failure. |
| `timeoutMs` | `integer` | — | `15000` | Execution timeout of a single step in milliseconds. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "steps": {
      "description": "JSON array of code strings, run in order: [\u0022globalThis.card = figma.createFrame();\u0022, \u0022globalThis.card.resize(320, 180);\u0022].",
      "type": "string"
    },
    "delayMs": {
      "description": "Pause between steps in milliseconds, up to 2000. 0 runs as fast as the bridge allows; 100 to 200 reads as a build a human can follow.",
      "type": "integer",
      "default": 0
    },
    "stopOnError": {
      "description": "Stop at the first failing step, which is the default, or run the rest and report every failure.",
      "type": "boolean",
      "default": true
    },
    "timeoutMs": {
      "description": "Execution timeout of a single step in milliseconds.",
      "type": "integer",
      "default": 15000
    }
  },
  "required": [
    "steps"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>selection_<wbr>colors</code>

Every color used in the current selection, with the styles they come from — the quick token audit: select a screen, see which paints are loose values instead of styles or variables. Returns null-ish when nothing is selected. Requires the plugin.

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

## <code>figma_<wbr>sort_<wbr>styles</code>

Reorders the local styles panel: alphabetically by default, or by an explicit comma-separated id list. styleType is PAINT, TEXT, EFFECT or GRID; ids come from figma_get_styles. Folder prefixes (Brand/…) sort with their names. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `styleType` | `string` | yes | — | Style type: PAINT, TEXT, EFFECT or GRID. |
| `order` | `string?` | — | `null` | Comma-separated style ids in the desired order; empty sorts alphabetically. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "styleType": {
      "description": "Style type: PAINT, TEXT, EFFECT or GRID.",
      "type": "string"
    },
    "order": {
      "description": "Comma-separated style ids in the desired order; empty sorts alphabetically.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "styleType"
  ]
}
```

</details>

## <code>figma_<wbr>commit_<wbr>undo</code>

Closes the current undo group: everything changed since the previous commit becomes a single Ctrl+Z step for the human working in the same file. Call it after a finished batch of mutations, not after every small edit. Requires the plugin.

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

## <code>figma_<wbr>create_<wbr>style</code>

Creates a local style. styleType is PAINT, TEXT, EFFECT or GRID; properties is JSON with the style contents: paints for PAINT, fontName/fontSize/lineHeight and the other text fields for TEXT, effects for EFFECT, layoutGrids for GRID. A slash in the name makes a folder: Brand/Primary. Existing styles are read by figma_get_styles. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `styleType` | `string` | yes | — | Style type: PAINT, TEXT, EFFECT or GRID. |
| `name` | `string` | yes | — | Style name; a slash makes a folder: Brand/Primary. |
| `properties` | `string?` | — | `null` | Style contents as JSON, keyed by type: paints, text fields, effects or layoutGrids. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "styleType": {
      "description": "Style type: PAINT, TEXT, EFFECT or GRID.",
      "type": "string"
    },
    "name": {
      "description": "Style name; a slash makes a folder: Brand/Primary.",
      "type": "string"
    },
    "properties": {
      "description": "Style contents as JSON, keyed by type: paints, text fields, effects or layoutGrids.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "styleType",
    "name"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>status</code>

The state of the bridge to Figma: whether the plugin is connected, which files are visible, which of them is the target of the commands and which other agents work there. Call it first when the write tools answer 'the plugin is not connected'.

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
        "connected": {
          "description": "Whether at least one plugin session is open.",
          "type": "boolean"
        },
        "activeFileKey": {
          "description": "Key of the target file; null when no file is bound.",
          "type": [
            "string",
            "null"
          ]
        },
        "othersHere": {
          "description": "Names of the other agents bound to the target file.",
          "type": "array",
          "items": {
            "type": "string"
          }
        },
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
        }
      },
      "required": [
        "connected",
        "activeFileKey",
        "othersHere",
        "files"
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

