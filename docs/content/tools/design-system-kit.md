# Design system kit

## <code>figma_<wbr>get_<wbr>design_<wbr>system_<wbr>kit</code>

The MAIN tool for capturing a design system: it replaces separate calls to figma_get_styles, figma_get_variables and figma_get_component. It returns tokens, components and styles in one response with adaptive compression to the size of the system. Inside are the visual specifications of the components (exact colors, spacing, typography, layout), token values per mode and resolved style values. Take it instead of the separate tools so the context does not overflow. Variant specifications are delta-encoded: the base variant carries the full visualSpec, its neighbours only the differences. Variables are read through the plugin and work on any plan; the Variables REST API is the fallback and needs Enterprise.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file or its key. Optional if the file has already been used. |
| `include` | `string?` | — | `null` | Which sections to include, comma-separated: tokens, components, styles. Empty means all three. |
| `componentIds` | `string?` | — | `null` | Comma-separated ids of specific components. Empty means every published one. |
| `includeImages` | `boolean` | — | `false` | Add links to component images. Noticeably slower. |
| `format` | `string` | — | `full` | Response size: full is everything, summary drops variant specifications, compact keeps only names, types and properties. It is compressed further automatically when the response is too large. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrl": {
      "description": "Link to the Figma file or its key. Optional if the file has already been used.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "include": {
      "description": "Which sections to include, comma-separated: tokens, components, styles. Empty means all three.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "componentIds": {
      "description": "Comma-separated ids of specific components. Empty means every published one.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "includeImages": {
      "description": "Add links to component images. Noticeably slower.",
      "type": "boolean",
      "default": false
    },
    "format": {
      "description": "Response size: full is everything, summary drops variant specifications, compact keeps only names, types and properties. It is compressed further automatically when the response is too large.",
      "type": "string",
      "default": "full"
    }
  }
}
```

</details>

