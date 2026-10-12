# Design tokens

## <code>figma_<wbr>export_<wbr>tokens</code>

Exports Figma variables into code. Formats: dtcg, tokens-studio, css-vars, tailwind-v4, tailwind-v3, scss, less, ts-module, json-flat, json-nested, style-dictionary-v3. The data source is chosen automatically: when the plugin is running the variables come from it (works on any plan), otherwise through the Variables REST API (Enterprise only). It returns the file contents — writing them to disk is up to you.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `format` | `string` | — | `dtcg` | Output format. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used. |
| `prefix` | `string?` | — | `null` | Prefix of the token names, for example 'ds-'. |
| `splitByMode` | `boolean` | — | `false` | A separate file per mode. |
| `splitByCollection` | `boolean` | — | `false` | A separate file per collection. |
| `resolveAliases` | `boolean` | — | `false` | Unfold references into final values. |
| `colorFormat` | `string` | — | `hex` | Color format: hex, hex8, rgba, hsl, oklch. |
| `sizeUnit` | `string?` | — | `null` | Size unit: px, rem, pt, dp. |
| `remBase` | `number` | — | `16` | Base font size for converting px into rem. |
| `modeMap` | `string?` | — | `null` | Mode renaming, JSON of the form {"Light":"light","Dark":"dark"}. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "format": {
      "description": "Output format.",
      "type": "string",
      "default": "dtcg"
    },
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "prefix": {
      "description": "Prefix of the token names, for example \u0027ds-\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "splitByMode": {
      "description": "A separate file per mode.",
      "type": "boolean",
      "default": false
    },
    "splitByCollection": {
      "description": "A separate file per collection.",
      "type": "boolean",
      "default": false
    },
    "resolveAliases": {
      "description": "Unfold references into final values.",
      "type": "boolean",
      "default": false
    },
    "colorFormat": {
      "description": "Color format: hex, hex8, rgba, hsl, oklch.",
      "type": "string",
      "default": "hex"
    },
    "sizeUnit": {
      "description": "Size unit: px, rem, pt, dp.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "remBase": {
      "description": "Base font size for converting px into rem.",
      "type": "number",
      "default": 16
    },
    "modeMap": {
      "description": "Mode renaming, JSON of the form {\u0022Light\u0022:\u0022light\u0022,\u0022Dark\u0022:\u0022dark\u0022}.",
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

## <code>figma_<wbr>import_<wbr>tokens</code>

Reads tokens from code and pushes them into Figma variables. content is the file contents, format is how to parse it (dtcg, tokens-studio, css-vars, scss, tailwind-v4, tailwind-v3, json-flat, json-nested, style-dictionary-v3). dryRun=true (the default) only shows the plan of changes without changing anything. Writing requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `content` | `string` | yes | — | Contents of the token file. |
| `format` | `string?` | — | `null` | Source format. Empty means detect it from the file name. |
| `fileName` | `string?` | — | `null` | File name — the format is guessed from it. |
| `collectionName` | `string?` | — | `null` | Name of the Figma collection to push the tokens into. |
| `dryRun` | `boolean` | — | `true` | Only show the plan, change nothing. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "content": {
      "description": "Contents of the token file.",
      "type": "string"
    },
    "format": {
      "description": "Source format. Empty means detect it from the file name.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "fileName": {
      "description": "File name \u2014 the format is guessed from it.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "collectionName": {
      "description": "Name of the Figma collection to push the tokens into.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "dryRun": {
      "description": "Only show the plan, change nothing.",
      "type": "boolean",
      "default": true
    }
  },
  "required": [
    "content"
  ]
}
```

</details>

## <code>figma_<wbr>convert_<wbr>tokens</code>

Converts tokens from one format into another without touching Figma. Useful for handing the same system out in CSS, Tailwind and TypeScript.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `content` | `string` | yes | — | Contents of the source file. |
| `fromFormat` | `string` | yes | — | Source format. |
| `toFormat` | `string` | yes | — | Target format. |
| `prefix` | `string?` | — | `null` | Prefix of the token names. |
| `colorFormat` | `string` | — | `hex` | Color format: hex, hex8, rgba, hsl, oklch. |
| `sizeUnit` | `string?` | — | `null` | Size unit: px, rem, pt, dp. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "content": {
      "description": "Contents of the source file.",
      "type": "string"
    },
    "fromFormat": {
      "description": "Source format.",
      "type": "string"
    },
    "toFormat": {
      "description": "Target format.",
      "type": "string"
    },
    "prefix": {
      "description": "Prefix of the token names.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "colorFormat": {
      "description": "Color format: hex, hex8, rgba, hsl, oklch.",
      "type": "string",
      "default": "hex"
    },
    "sizeUnit": {
      "description": "Size unit: px, rem, pt, dp.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "content",
    "fromFormat",
    "toFormat"
  ]
}
```

</details>

