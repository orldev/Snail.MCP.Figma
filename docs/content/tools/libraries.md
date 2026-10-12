# Libraries

## <code>figma_<wbr>get_<wbr>library_<wbr>component_<wbr>by_<wbr>key</code>

The full property definitions, variants and visual specifications of ONE published library component by its key — the 40-character hexadecimal string from figma_search_components. Take it when you already have the key and need to know which properties and variants the component offers before inserting it through figma_instantiate_component. It works for both a COMPONENT_SET and a single COMPONENT: for a set you must insert the key of the VARIANT from variants[], not the key of the set itself.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `componentKey` | `string` | yes | — | Component key. |
| `includeVisualSpecs` | `boolean` | — | `true` | Include visual specifications of the variants. |
| `format` | `string` | — | `full` | Response size: full or summary. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "componentKey": {
      "description": "Component key.",
      "type": "string"
    },
    "includeVisualSpecs": {
      "description": "Include visual specifications of the variants.",
      "type": "boolean",
      "default": true
    },
    "format": {
      "description": "Response size: full or summary.",
      "type": "string",
      "default": "full"
    }
  },
  "required": [
    "componentKey"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>library_<wbr>variables</code>

Variables from the team libraries the current file subscribes to: which tokens — colors, spacing, sizes, strings, flags — are available at all, without a link to the library file and without an enterprise plan. It works through the plugin: exactly the libraries connected in the current file are visible. Next the variable key goes into figma_import_library_variable. The filters compare substrings case-insensitively. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `libraryName` | `string?` | — | `null` | Filter by library name. |
| `collectionName` | `string?` | — | `null` | Filter by collection name inside the library. |
| `resolvedType` | `string?` | — | `null` | Filter by type: COLOR, FLOAT, STRING, BOOLEAN. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "libraryName": {
      "description": "Filter by library name.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "collectionName": {
      "description": "Filter by collection name inside the library.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "resolvedType": {
      "description": "Filter by type: COLOR, FLOAT, STRING, BOOLEAN.",
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

## <code>figma_<wbr>import_<wbr>library_<wbr>variable</code>

Imports one variable from a subscribed library into the current file. After the import the variable is addressed locally by the returned id and fits any tool that binds variables to nodes. Calling it again returns the same id. The key comes from figma_get_library_variables — it is not an id. Requires the plugin.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `variableKey` | `string` | yes | — | Variable key from figma_get_library_variables. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "variableKey": {
      "description": "Variable key from figma_get_library_variables.",
      "type": "string"
    }
  },
  "required": [
    "variableKey"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>library_<wbr>style_<wbr>by_<wbr>key</code>

Metadata of a published library style by its key: name, type, description and where it lives (fileKey plus nodeId). The symmetric counterpart of figma_get_library_component_by_key. Costs one REST request; draft and unpublished styles are not visible here.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `styleKey` | `string` | yes | — | Style key. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "styleKey": {
      "description": "Style key.",
      "type": "string"
    }
  },
  "required": [
    "styleKey"
  ]
}
```

</details>

## <code>figma_<wbr>search_<wbr>team_<wbr>components</code>

Searches the published components of a whole team library by name or description substring — unlike figma_search_components, which only sees the currently open file. Takes the team id or a figma.com link with /team/{id}/. Returns keys ready for figma_get_library_component_by_key and figma_instantiate_component; paginate with nextCursor. Costs REST requests.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `teamIdOrUrl` | `string` | yes | — | Team id, or a figma.com link containing /team/{id}/. |
| `query` | `string?` | — | `null` | Substring of the component name or description. Empty returns the whole page. |
| `includeComponentSets` | `boolean` | — | `true` | Also search component sets. |
| `pageSize` | `integer` | — | `100` | Page size per kind, 1-1000. |
| `after` | `string?` | — | `null` | Pagination cursor from the previous response. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "teamIdOrUrl": {
      "description": "Team id, or a figma.com link containing /team/{id}/.",
      "type": "string"
    },
    "query": {
      "description": "Substring of the component name or description. Empty returns the whole page.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "includeComponentSets": {
      "description": "Also search component sets.",
      "type": "boolean",
      "default": true
    },
    "pageSize": {
      "description": "Page size per kind, 1-1000.",
      "type": "integer",
      "default": 100
    },
    "after": {
      "description": "Pagination cursor from the previous response.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "teamIdOrUrl"
  ]
}
```

</details>

