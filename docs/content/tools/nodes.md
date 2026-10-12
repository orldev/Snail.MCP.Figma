# Nodes

## <code>figma_<wbr>clone_<wbr>node</code>

Copies a node next to the original. The response carries the id of the copy. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, for example '1:234'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, for example \u00271:234\u0027.",
      "type": "string"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>delete_<wbr>node</code>

Deletes a node together with everything inside it. The operation is irreversible. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

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

## <code>figma_<wbr>rename_<wbr>node</code>

Renames a node (a layer in the layers panel). Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id. |
| `newName` | `string` | yes | — | New name. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id.",
      "type": "string"
    },
    "newName": {
      "description": "New name.",
      "type": "string"
    }
  },
  "required": [
    "nodeId",
    "newName"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>strokes</code>

Sets the strokes of a node. The strokes format is an array of Figma Paint in JSON, color components from 0 to 1: [{"type":"SOLID","color":{"r":0,"g":0,"b":0}}]. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id. |
| `strokes` | `string` | yes | — | Array of Paint as a JSON string. |
| `strokeWeight` | `number?` | — | `null` | Stroke weight in pixels. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id.",
      "type": "string"
    },
    "strokes": {
      "description": "Array of Paint as a JSON string.",
      "type": "string"
    },
    "strokeWeight": {
      "description": "Stroke weight in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "nodeId",
    "strokes"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>image_<wbr>fill</code>

Puts an image into a node as a fill. Give it filePath and the server reads the picture from disk itself; give it imageData and the bytes come as base64 without the data: prefix, at the cost of carrying them through the context. scaleMode: FILL (the default), FIT, CROP, TILE. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id, or several separated by commas. |
| `imageData` | `string?` | — | `null` | Image as base64. Pass either imageData or filePath. |
| `filePath` | `string?` | — | `null` | Absolute path to a picture on the machine running the server; ~ is expanded. The server reads the file and sends the bytes over the bridge itself, so the picture never travels through the context — this is the way to place one at full resolution instead of shrinking it to fit a base64 argument. PNG, JPG, GIF and WebP up to 10 MB. |
| `scaleMode` | `string` | — | `FILL` | Scale mode: FILL, FIT, CROP, TILE. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id, or several separated by commas.",
      "type": "string"
    },
    "imageData": {
      "description": "Image as base64. Pass either imageData or filePath.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "filePath": {
      "description": "Absolute path to a picture on the machine running the server; ~ is expanded. The server reads the file and sends the bytes over the bridge itself, so the picture never travels through the context \u2014 this is the way to place one at full resolution instead of shrinking it to fit a base64 argument. PNG, JPG, GIF and WebP up to 10 MB.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "scaleMode": {
      "description": "Scale mode: FILL, FIT, CROP, TILE.",
      "type": "string",
      "default": "FILL"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>create_<wbr>child</code>

Creates a node inside a parent. nodeType: FRAME, RECTANGLE, ELLIPSE, TEXT, LINE, VECTOR and so on. properties is JSON with the properties of the new node: {"name":"Button","width":120,"height":40,"x":0,"y":0,"characters":"Text"}. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `parentId` | `string` | yes | — | Id of the parent — a frame, a group or a page. |
| `nodeType` | `string` | yes | — | Type of the node to create. |
| `properties` | `string?` | — | `null` | Properties of the new node as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "parentId": {
      "description": "Id of the parent \u2014 a frame, a group or a page.",
      "type": "string"
    },
    "nodeType": {
      "description": "Type of the node to create.",
      "type": "string"
    },
    "properties": {
      "description": "Properties of the new node as a JSON string.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "parentId",
    "nodeType"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>description</code>

Sets the description of a component or a style — what designers see in the assets panel. Markdown is supported through descriptionMarkdown. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the node — a component, a variant set or a style. |
| `description` | `string` | yes | — | Description text. |
| `descriptionMarkdown` | `string?` | — | `null` | Markdown version of the description, when markup is needed. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the node \u2014 a component, a variant set or a style.",
      "type": "string"
    },
    "description": {
      "description": "Description text.",
      "type": "string"
    },
    "descriptionMarkdown": {
      "description": "Markdown version of the description, when markup is needed.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "nodeId",
    "description"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>instance_<wbr>properties</code>

Changes the properties of a component instance: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT. This is the right way to change the contents of an instance — editing a text node inside it directly may silently do nothing. The properties format is JSON: {"Label":"Buy","Show Icon":true}. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the INSTANCE node. |
| `properties` | `string` | yes | — | Properties as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the INSTANCE node.",
      "type": "string"
    },
    "properties": {
      "description": "Properties as a JSON string.",
      "type": "string"
    }
  },
  "required": [
    "nodeId",
    "properties"
  ]
}
```

</details>

## <code>figma_<wbr>create_<wbr>page</code>

Creates a page in the document; makeCurrent switches the editor to it right away. Nodes are then created on it with figma_create_child. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `name` | `string` | yes | — | Name of the new page. |
| `makeCurrent` | `boolean` | — | `false` | Switch the editor to the new page right away. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "name": {
      "description": "Name of the new page.",
      "type": "string"
    },
    "makeCurrent": {
      "description": "Switch the editor to the new page right away.",
      "type": "boolean",
      "default": false
    }
  },
  "required": [
    "name"
  ]
}
```

</details>

## <code>figma_<wbr>create_<wbr>node_<wbr>from_<wbr>svg</code>

Imports SVG markup as a frame with vector children — the way to bring an icon from code into Figma. Appends to the parent node, or to the current page when parentId is empty; x and y are relative to the parent. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `svg` | `string` | yes | — | SVG markup, the full &lt;svg&gt;...&lt;/svg&gt; element. |
| `parentId` | `string?` | — | `null` | Id of the parent frame or page; empty appends to the current page. |
| `name` | `string?` | — | `null` | Name of the created frame. |
| `x` | `number?` | — | `null` | X relative to the parent. |
| `y` | `number?` | — | `null` | Y relative to the parent. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "svg": {
      "description": "SVG markup, the full \u003Csvg\u003E...\u003C/svg\u003E element.",
      "type": "string"
    },
    "parentId": {
      "description": "Id of the parent frame or page; empty appends to the current page.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "name": {
      "description": "Name of the created frame.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X relative to the parent.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y relative to the parent.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "svg"
  ]
}
```

</details>

## <code>figma_<wbr>place_<wbr>image</code>

Places a raster image into the file as a rectangle with an image fill, sized to the source. Three ways in: filePath, which the server reads from disk and is the way to place a photograph at full resolution; base64, which carries the bytes through the context and suits an icon; and url, which Figma downloads itself and only from a host the plugin manifest allows, so a link to a local file server will not do. PNG, JPG, GIF, WebP up to 4096x4096. Requires the plugin.

*only adds · every call adds · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `url` | `string?` | — | `null` | URL of the image; Figma downloads it itself, and only from hosts the plugin manifest allows. |
| `base64` | `string?` | — | `null` | Raw image bytes as base64; it travels through the context, so use it for icons and filePath for photographs. |
| `filePath` | `string?` | — | `null` | Absolute path to a picture on the machine running the server; ~ is expanded. The server reads the file and sends the bytes over the bridge itself, so the picture never travels through the context — this is the way to place one at full resolution instead of shrinking it to fit a base64 argument. PNG, JPG, GIF and WebP up to 10 MB. |
| `parentId` | `string?` | — | `null` | Id of the parent frame or page; empty appends to the current page. |
| `name` | `string?` | — | `null` | Name of the created rectangle. |
| `x` | `number?` | — | `null` | X relative to the parent. |
| `y` | `number?` | — | `null` | Y relative to the parent. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "url": {
      "description": "URL of the image; Figma downloads it itself, and only from hosts the plugin manifest allows.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "base64": {
      "description": "Raw image bytes as base64; it travels through the context, so use it for icons and filePath for photographs.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "filePath": {
      "description": "Absolute path to a picture on the machine running the server; ~ is expanded. The server reads the file and sends the bytes over the bridge itself, so the picture never travels through the context \u2014 this is the way to place one at full resolution instead of shrinking it to fit a base64 argument. PNG, JPG, GIF and WebP up to 10 MB.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "parentId": {
      "description": "Id of the parent frame or page; empty appends to the current page.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "name": {
      "description": "Name of the created rectangle.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X relative to the parent.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y relative to the parent.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    }
  }
}
```

</details>

## <code>figma_<wbr>create_<wbr>node_<wbr>from_<wbr>jsx</code>

Builds a node tree from one declarative JSON spec — the bulk way to create layouts, faster than a chain of figma_create_child calls. Components: AutoLayout, Frame, Text, Rectangle, Ellipse, Line, SVG, Image, Fragment; children hold nested specs or strings for text. Style ids and instances are not supported here. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `spec` | `string` | yes | — | Node tree as JSON: {"type":"AutoLayout","props":{"fill":"#F00","padding":20},"children":[{"type":"Text","props":{"fontSize":24},"children":["Hello"]}]}. Components: AutoLayout, Frame, Text, Rectangle, Ellipse, Line, SVG, Image, Fragment. |
| `parentId` | `string?` | — | `null` | Id of the parent frame or page; empty appends to the current page. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "spec": {
      "description": "Node tree as JSON: {\u0022type\u0022:\u0022AutoLayout\u0022,\u0022props\u0022:{\u0022fill\u0022:\u0022#F00\u0022,\u0022padding\u0022:20},\u0022children\u0022:[{\u0022type\u0022:\u0022Text\u0022,\u0022props\u0022:{\u0022fontSize\u0022:24},\u0022children\u0022:[\u0022Hello\u0022]}]}. Components: AutoLayout, Frame, Text, Rectangle, Ellipse, Line, SVG, Image, Fragment.",
      "type": "string"
    },
    "parentId": {
      "description": "Id of the parent frame or page; empty appends to the current page.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "spec"
  ]
}
```

</details>

## <code>figma_<wbr>boolean_<wbr>operation</code>

Combines vector shapes: UNION, SUBTRACT, INTERSECT, EXCLUDE make a boolean group, FLATTEN bakes into one vector. Node order matters for SUBTRACT — the first node is the base. The icon-work primitive. Requires the plugin.

*may overwrite or delete · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `operation` | `string` | yes | — | Operation: UNION, SUBTRACT, INTERSECT, EXCLUDE or FLATTEN. |
| `nodeIds` | `string` | yes | — | Comma-separated node ids; the order matters for SUBTRACT. |
| `name` | `string?` | — | `null` | Name of the resulting node. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "operation": {
      "description": "Operation: UNION, SUBTRACT, INTERSECT, EXCLUDE or FLATTEN.",
      "type": "string"
    },
    "nodeIds": {
      "description": "Comma-separated node ids; the order matters for SUBTRACT.",
      "type": "string"
    },
    "name": {
      "description": "Name of the resulting node.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "operation",
    "nodeIds"
  ]
}
```

</details>

## <code>figma_<wbr>group_<wbr>nodes</code>

Wraps nodes into a group under the first node's parent. Requires the plugin.

*may overwrite or delete · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeIds` | `string` | yes | — | Comma-separated ids of the nodes to group. |
| `name` | `string?` | — | `null` | Name of the group. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeIds": {
      "description": "Comma-separated ids of the nodes to group.",
      "type": "string"
    },
    "name": {
      "description": "Name of the group.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "nodeIds"
  ]
}
```

</details>

## <code>figma_<wbr>ungroup_<wbr>node</code>

Dissolves a group or frame, releasing its children into the parent; returns the freed ids. Requires the plugin.

*may overwrite or delete · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the group or frame to dissolve. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the group or frame to dissolve.",
      "type": "string"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

