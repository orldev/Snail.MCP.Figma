# Quality and annotations

## <code>figma_<wbr>lint_<wbr>design</code>

Checks a subtree for the usual defects: values off the tokens, detached styles, elements hugging instead of filling, inconsistent spacing. rules is a comma-separated list of rules; empty means every rule. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string?` | — | `null` | Id of the root node. Empty means the whole current page. |
| `rules` | `string?` | — | `null` | Comma-separated rules. |
| `maxDepth` | `integer` | — | `10` | Maximum traversal depth. |
| `maxFindings` | `integer` | — | `100` | Cap on the number of findings. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the root node. Empty means the whole current page.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "rules": {
      "description": "Comma-separated rules.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "maxDepth": {
      "description": "Maximum traversal depth.",
      "type": "integer",
      "default": 10
    },
    "maxFindings": {
      "description": "Cap on the number of findings.",
      "type": "integer",
      "default": 100
    }
  }
}
```

</details>

## <code>figma_<wbr>audit_<wbr>component_<wbr>accessibility</code>

Accessibility audit of a component: text contrast, tap target sizes, presence of text alternatives. targetSize is the minimum interactive area size in pixels (44 per WCAG). Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the component or frame. |
| `targetSize` | `integer` | — | `44` | Minimum size of tap targets in pixels. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the component or frame.",
      "type": "string"
    },
    "targetSize": {
      "description": "Minimum size of tap targets in pixels.",
      "type": "integer",
      "default": 44
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>annotations</code>

Developer annotations on a node — the notes a designer left for the implementation. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string?` | — | `null` | Node id. Empty means the whole page. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id. Empty means the whole page.",
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

## <code>figma_<wbr>set_<wbr>annotations</code>

Puts annotations on a node. annotations is a JSON array: [{"label":"Spacing","labelMarkdown":"…"}]. See the categories in figma_get_annotation_categories. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id. |
| `annotations` | `string` | yes | — | Array of annotations as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id.",
      "type": "string"
    },
    "annotations": {
      "description": "Array of annotations as a JSON string.",
      "type": "string"
    }
  },
  "required": [
    "nodeId",
    "annotations"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>annotation_<wbr>categories</code>

The annotation categories defined in the file. Requires the plugin.

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

## <code>figma_<wbr>add_<wbr>annotation_<wbr>category</code>

Creates an annotation category. color is one of yellow, orange, red, pink, violet, blue, teal, green. The categories that already exist come from figma_get_annotation_categories. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `label` | `string` | yes | — | Label of the category. |
| `color` | `string` | yes | — | Color: yellow, orange, red, pink, violet, blue, teal or green. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "label": {
      "description": "Label of the category.",
      "type": "string"
    },
    "color": {
      "description": "Color: yellow, orange, red, pink, violet, blue, teal or green.",
      "type": "string"
    }
  },
  "required": [
    "label",
    "color"
  ]
}
```

</details>

