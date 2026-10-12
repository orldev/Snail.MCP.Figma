# Design to code

## <code>figma_<wbr>get_<wbr>component_<wbr>for_<wbr>development</code>

Component data for implementing it in code: the tree with variable bindings, sizes, auto layout and text properties, plus a ready reconstruction specification. Call it on requests like 'build this' or 'make a React component'. When the plugin is running the data comes from it — it is fresher than REST. For metadata alone figma_get_component is enough, for a picture use figma_get_component_image.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, for example '1:234'. |
| `fileUrl` | `string?` | — | `null` | Link to the file. Needed only when the plugin is not running. |
| `depth` | `integer` | — | `4` | Tree traversal depth. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, for example \u00271:234\u0027.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the file. Needed only when the plugin is not running.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "depth": {
      "description": "Tree traversal depth.",
      "type": "integer",
      "default": 4
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>component_<wbr>for_<wbr>development_<wbr>deep</code>

The same as figma_get_component_for_development, but with a deep breakdown through the plugin: states, reactions, slots, Dev Mode annotations. Slower and heavier — take it when the plain breakdown was not enough. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id.",
      "type": "string"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>extract_<wbr>style_<wbr>values</code>

Pulls token-like values out of the DESIGN — colors, typography, spacing, radii — and collapses duplicates. Works on any Figma plan, unlike figma_get_variables. A good starting point when the file has no variables yet. Not to be confused with figma_ds_extract_tokens: that one mines tokens from the CODEBASE.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used. |
| `nodeId` | `string?` | — | `null` | Id of the root node. Empty means the whole file. |
| `depth` | `integer` | — | `8` | Traversal depth. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "nodeId": {
      "description": "Id of the root node. Empty means the whole file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "depth": {
      "description": "Traversal depth.",
      "type": "integer",
      "default": 8
    }
  }
}
```

</details>

## <code>figma_<wbr>check_<wbr>design_<wbr>parity</code>

Compares a component in Figma with what is implemented in code. codeSpec is JSON with what you read out of the sources: {"fills":[…],"fontSize":14,"paddingLeft":16,"variants":[…]}. Returns a conformance score and the list of mismatches on both sides. Read the component code first.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the component node in Figma. |
| `codeSpec` | `string` | yes | — | Code-side data as a JSON string. |
| `fileUrl` | `string?` | — | `null` | Link to the file. Needed only when the plugin is not running. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the component node in Figma.",
      "type": "string"
    },
    "codeSpec": {
      "description": "Code-side data as a JSON string.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the file. Needed only when the plugin is not running.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "nodeId",
    "codeSpec"
  ]
}
```

</details>

## <code>figma_<wbr>generate_<wbr>component_<wbr>doc</code>

Assembles component documentation in Markdown: contents, variants, colors, typography, spacing, the Figma description and the annotations. codeInfo is optional JSON with data from the sources (props, CVA variants, file paths); it lands in the code section.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the component node. |
| `fileUrl` | `string?` | — | `null` | Link to the file. Needed only when the plugin is not running. |
| `codeInfo` | `string?` | — | `null` | Code-side data as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the component node.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the file. Needed only when the plugin is not running.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "codeInfo": {
      "description": "Code-side data as a JSON string.",
      "type": [
        "string",
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

