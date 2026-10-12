# Reading

## <code>figma_<wbr>get_<wbr>file_<wbr>data</code>

The structure of a Figma file and the document tree. CAUTION: it can eat a lot of tokens — start with depth=1. For component descriptions use figma_get_component. It fits best for learning what a file contains and finding the nodeId of the nodes you need. The response is already cleaned of vector geometry and defaults, repeated calls are served from the cache until the file changes in Figma.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used in this session. |
| `depth` | `integer?` | — | `null` | Tree traversal depth. 1 is pages only, 2 is top-level frames. |
| `maxNodes` | `integer?` | — | `null` | Cap on the number of nodes in the response. |
| `profile` | `string?` | — | `null` | Cleaning profile: aggressive (the default), balanced, raw. It affects the REST path only. |
| `source` | `string` | — | `auto` | Source: auto (the default — the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only). |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used in this session.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "depth": {
      "description": "Tree traversal depth. 1 is pages only, 2 is top-level frames.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    },
    "maxNodes": {
      "description": "Cap on the number of nodes in the response.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    },
    "profile": {
      "description": "Cleaning profile: aggressive (the default), balanced, raw. It affects the REST path only.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "source": {
      "description": "Source: auto (the default \u2014 the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only).",
      "type": "string",
      "default": "auto"
    }
  }
}
```

</details>

## <code>figma_<wbr>get_<wbr>component</code>

Metadata of a single node or component: name, type, description, component properties, the children it holds. This is the right tool for the questions 'what is this component' and 'which properties does it have'. For a picture use figma_get_component_image, for the full file tree use figma_get_file_data.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, for example '1:234'. The dashed form from a link is accepted too. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used in this session. |
| `depth` | `integer?` | — | `null` | Traversal depth of the node subtree. The default 4 is enough to see what a component is made of. |
| `profile` | `string?` | — | `null` | Cleaning profile: aggressive (the default), balanced, raw. It affects the REST path only. |
| `source` | `string` | — | `auto` | Source: auto (the default — the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only). |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, for example \u00271:234\u0027. The dashed form from a link is accepted too.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used in this session.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "depth": {
      "description": "Traversal depth of the node subtree. The default 4 is enough to see what a component is made of.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    },
    "profile": {
      "description": "Cleaning profile: aggressive (the default), balanced, raw. It affects the REST path only.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "source": {
      "description": "Source: auto (the default \u2014 the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only).",
      "type": "string",
      "default": "auto"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>variables</code>

Figma variables (design tokens): colors, spacing, typography, multi-mode ones (Light/Dark) included. It returns a summary by collection and the list of variables. Note: the Variables REST API is available on the Enterprise plan only — on other plans the call comes back with a clear 403, and the tokens have to be taken through the plugin. For styles (not variables) use figma_get_styles.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used in this session. |
| `includePublished` | `boolean` | — | `false` | Add the published variables of the library, not just the local ones. |
| `format` | `string` | — | `summary` | Format: 'summary' (the default, the summary only), 'full' (every variable). |
| `namePattern` | `string?` | — | `null` | Keep only the variables whose name contains the substring. |
| `source` | `string` | — | `auto` | Source: auto (the default — the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only). The plugin works on any plan, Variables REST is Enterprise-only. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used in this session.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "includePublished": {
      "description": "Add the published variables of the library, not just the local ones.",
      "type": "boolean",
      "default": false
    },
    "format": {
      "description": "Format: \u0027summary\u0027 (the default, the summary only), \u0027full\u0027 (every variable).",
      "type": "string",
      "default": "summary"
    },
    "namePattern": {
      "description": "Keep only the variables whose name contains the substring.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "source": {
      "description": "Source: auto (the default \u2014 the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only). The plugin works on any plan, Variables REST is Enterprise-only.",
      "type": "string",
      "default": "auto"
    }
  }
}
```

</details>

## <code>figma_<wbr>get_<wbr>file_<wbr>meta</code>

Name, last-modified time, author of the last edit and editor type of a file — without downloading the document. The cheap way to ask 'did anything change?' before spending the expensive file read. REST only, one request from the light bucket.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used in this session. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used in this session.",
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

## <code>figma_<wbr>get_<wbr>styles</code>

The styles of a file: color, text, effect and grid styles. These are styles, not variables — for variables call figma_get_variables.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used in this session. |
| `source` | `string` | — | `auto` | Source: auto (the default — the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only). |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used in this session.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "source": {
      "description": "Source: auto (the default \u2014 the plugin when a live session for the file exists, otherwise REST), rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only).",
      "type": "string",
      "default": "auto"
    }
  }
}
```

</details>

## <code>figma_<wbr>get_<wbr>component_<wbr>image</code>

Renders a node into a picture (PNG, JPG, SVG, PDF). Needed on requests like 'show me', 'a screenshot', 'what does it look like'. For component data use figma_get_component. The source is chosen automatically: the Figma Desktop plugin → the cache → Figma REST. The plugin and the cache spend no Figma quota at all, and the plugin additionally shows the state right now, unsaved edits included. The response carries a source field: cache/figma-api give a link in data.url (it lives 30 days), plugin gives a path to a file on disk in data.image.path (open the file to see the picture). With deliver=both a picture taken through the plugin also comes back as image content, for a caller that cannot open a path on this machine.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, for example '1:234'. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used in this session. |
| `format` | `string` | — | `png` | Format: png (the default), jpg, svg, pdf. |
| `scale` | `number` | — | `2` | Render scale, from 0.01 to 4. |
| `source` | `string` | — | `auto` | Source: auto (the default — the cache, then the plugin, then REST), rest (Figma REST only, spends quota), plugin (the plugin only). |
| `deliver` | `string` | — | `path` | What comes back: path (the default) saves the picture beside the server and answers with the file path, which suits a human working in the same machine; both answers with the path AND the picture itself as image content, which is what a caller in another process or container needs, since a path there opens nothing. Only PNG and JPG travel as content, and only up to 2 MB — a bigger one still comes back as a path. |

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
      "description": "Link to the Figma file. Optional if the file has already been used in this session.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "format": {
      "description": "Format: png (the default), jpg, svg, pdf.",
      "type": "string",
      "default": "png"
    },
    "scale": {
      "description": "Render scale, from 0.01 to 4.",
      "type": "number",
      "default": 2
    },
    "source": {
      "description": "Source: auto (the default \u2014 the cache, then the plugin, then REST), rest (Figma REST only, spends quota), plugin (the plugin only).",
      "type": "string",
      "default": "auto"
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

## <code>figma_<wbr>get_<wbr>file_<wbr>for_<wbr>plugin</code>

The structure of a file without visual properties: ids, names, types, component links and plugin data only. Needed when developing plugins and when only the hierarchy matters. Noticeably lighter than figma_get_file_data.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used in this session. |
| `depth` | `integer` | — | `3` | Tree traversal depth, at most 5. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "fileUrl": {
      "description": "Link to the Figma file. Optional if the file has already been used in this session.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "depth": {
      "description": "Tree traversal depth, at most 5.",
      "type": "integer",
      "default": 3
    }
  }
}
```

</details>

