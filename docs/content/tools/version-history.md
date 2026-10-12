# Version history

## <code>figma_<wbr>get_<wbr>file_<wbr>versions</code>

The version history of a file with metadata: label, description, author, time. It walks the pages itself up to maxVersions. By default it returns named versions only — autosaves are skipped; includeAutosaves=true shows every saved state. To continue, pass the next_cursor from the previous response. The token needs the file_versions:read scope (OAuth) or the Versions Read permission on a personal token.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used. |
| `includeAutosaves` | `boolean` | — | `false` | Include autosaves — versions without a label. |
| `maxVersions` | `integer` | — | `50` | How many versions to return, at most 200. |
| `cursor` | `string?` | — | `null` | Cursor from next_cursor of the previous response. |

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
    "includeAutosaves": {
      "description": "Include autosaves \u2014 versions without a label.",
      "type": "boolean",
      "default": false
    },
    "maxVersions": {
      "description": "How many versions to return, at most 200.",
      "type": "integer",
      "default": 50
    },
    "cursor": {
      "description": "Cursor from next_cursor of the previous response.",
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

## <code>figma_<wbr>get_<wbr>file_<wbr>at_<wbr>version</code>

The file or individual nodes as they were at a past version. The response has the same shape as figma_get_file_data, only the snapshot is bound to a version. Version ids come from figma_get_file_versions. nodeIds and depth shrink the response noticeably.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `versionId` | `string` | yes | — | Version id from figma_get_file_versions. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. |
| `nodeIds` | `string?` | — | `null` | Comma-separated node ids. Empty means the whole file. |
| `depth` | `integer?` | — | `null` | Tree traversal depth. Empty means unlimited. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "versionId": {
      "description": "Version id from figma_get_file_versions.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the Figma file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "nodeIds": {
      "description": "Comma-separated node ids. Empty means the whole file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "depth": {
      "description": "Tree traversal depth. Empty means unlimited.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "versionId"
  ]
}
```

</details>

## <code>figma_<wbr>diff_<wbr>versions</code>

Compares two versions of a file. It always returns the cheap page structure comparison (added, removed, renamed) — that is two API requests. componentIds adds a breakdown of specific nodes at depth 2: children that appeared and disappeared, name and description changes, changes of componentPropertyDefinitions on a COMPONENT_SET and of variable bindings — two requests per node. toVersion='current' compares against the current state. When componentIds is not passed, the current selection in Figma is used. REST does not show description and annotation edits in version snapshots — they are collected from the plugin event buffer while it is connected.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fromVersion` | `string` | yes | — | The earlier version to compare from. |
| `toVersion` | `string` | yes | — | The later version. 'current' is the current state. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. |
| `componentIds` | `string?` | — | `null` | Comma-separated node ids for the detailed comparison. |
| `mode` | `string` | — | `standard` | Detail level: summary, standard, detailed. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fromVersion": {
      "description": "The earlier version to compare from.",
      "type": "string"
    },
    "toVersion": {
      "description": "The later version. \u0027current\u0027 is the current state.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the Figma file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "componentIds": {
      "description": "Comma-separated node ids for the detailed comparison.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "mode": {
      "description": "Detail level: summary, standard, detailed.",
      "type": "string",
      "default": "standard"
    }
  },
  "required": [
    "fromVersion",
    "toVersion"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>changes_<wbr>since_<wbr>version</code>

The same as figma_diff_versions, only the second version is always the current one. Handy for the question 'what changed since the last sync with the code'.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `sinceVersion` | `string` | yes | — | The version the current state is compared against. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. |
| `componentIds` | `string?` | — | `null` | Comma-separated node ids for the detailed comparison. |
| `mode` | `string` | — | `standard` | Detail level: summary, standard, detailed. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "sinceVersion": {
      "description": "The version the current state is compared against.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the Figma file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "componentIds": {
      "description": "Comma-separated node ids for the detailed comparison.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "mode": {
      "description": "Detail level: summary, standard, detailed.",
      "type": "string",
      "default": "standard"
    }
  },
  "required": [
    "sinceVersion"
  ]
}
```

</details>

## <code>figma_<wbr>generate_<wbr>changelog</code>

A changelog between two versions in Markdown — ready text for release notes, a pull request or an MDX page. On top of the version comparison it adds authors and dates, for which it makes one cheap request to the version list. It returns both the markdown and the structured data. toVersion='current' compares against the current state.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fromVersion` | `string` | yes | — | The earlier version. |
| `toVersion` | `string` | yes | — | The later version. 'current' is the current state. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. |
| `componentIds` | `string?` | — | `null` | Comma-separated node ids for the per-component section. |
| `mode` | `string` | — | `standard` | Detail level: summary, standard, detailed. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fromVersion": {
      "description": "The earlier version.",
      "type": "string"
    },
    "toVersion": {
      "description": "The later version. \u0027current\u0027 is the current state.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the Figma file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "componentIds": {
      "description": "Comma-separated node ids for the per-component section.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "mode": {
      "description": "Detail level: summary, standard, detailed.",
      "type": "string",
      "default": "standard"
    }
  },
  "required": [
    "fromVersion",
    "toVersion"
  ]
}
```

</details>

## <code>figma_<wbr>blame_<wbr>node</code>

Finds the version an edit appeared in — the answer to 'who added this and when'. It walks the history by binary search: about log2(N) requests instead of N. It returns the version metadata: label, author, time. Autosaves are included by default — the attribution is more precise that way; the Figma system user shows up on scheduled snapshots and is marked attribution_certainty='system_attributed'. Pass EXACTLY ONE of targetComponentProperty or targetChildNodeId.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string?` | — | `null` | Node id, usually a COMPONENT_SET. Empty takes the first node of the current selection. |
| `targetComponentProperty` | `string?` | — | `null` | Key from componentPropertyDefinitions, for example 'Disabled#1:2'. |
| `targetChildNodeId` | `string?` | — | `null` | Child id — when it first appeared under the node. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. |
| `startVersion` | `string` | — | `current` | The version to walk back from. The current one by default. |
| `maxVersionsToWalk` | `integer` | — | `200` | How many versions to look back through, from 2 to 500. |
| `includeAutosaves` | `boolean` | — | `true` | Include autosaves in the search. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, usually a COMPONENT_SET. Empty takes the first node of the current selection.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "targetComponentProperty": {
      "description": "Key from componentPropertyDefinitions, for example \u0027Disabled#1:2\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "targetChildNodeId": {
      "description": "Child id \u2014 when it first appeared under the node.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "fileUrl": {
      "description": "Link to the Figma file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "startVersion": {
      "description": "The version to walk back from. The current one by default.",
      "type": "string",
      "default": "current"
    },
    "maxVersionsToWalk": {
      "description": "How many versions to look back through, from 2 to 500.",
      "type": "integer",
      "default": 200
    },
    "includeAutosaves": {
      "description": "Include autosaves in the search.",
      "type": "boolean",
      "default": true
    }
  }
}
```

</details>

## <code>figma_<wbr>save_<wbr>version</code>

Saves a named checkpoint into the file's version history — the anchor for figma_diff_versions and figma_generate_changelog. Call it after a finished batch of agent work, so the history reads like a changelog instead of autosaves. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `title` | `string` | yes | — | Title of the version checkpoint. |
| `description` | `string?` | — | `null` | Optional description shown in the history panel. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "title": {
      "description": "Title of the version checkpoint.",
      "type": "string"
    },
    "description": {
      "description": "Optional description shown in the history panel.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "title"
  ]
}
```

</details>

