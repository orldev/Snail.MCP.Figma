# FigJam

## <code>figjam_<wbr>create_<wbr>sticky</code>

Creates a sticky note on a FigJam board. Colors: YELLOW, BLUE, GREEN, PINK, ORANGE, PURPLE, RED, LIGHT_GRAY, GRAY (YELLOW by default). Works in FigJam files only. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `text` | `string` | yes | — | Sticky text. |
| `color` | `string?` | — | `null` | Sticky color. |
| `x` | `number?` | — | `null` | X coordinate on the canvas. |
| `y` | `number?` | — | `null` | Y coordinate on the canvas. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "text": {
      "description": "Sticky text.",
      "type": "string"
    },
    "color": {
      "description": "Sticky color.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X coordinate on the canvas.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y coordinate on the canvas.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "text"
  ]
}
```

</details>

## <code>figjam_<wbr>create_<wbr>stickies</code>

Creates a batch of stickies in one go — this is how a board is filled from structured data (meeting notes, brainstorm ideas). stickies is a JSON array of the form [{"text":"Idea","color":"BLUE","x":0,"y":0}], at most 200 items. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `stickies` | `string` | yes | — | Array of stickies as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "stickies": {
      "description": "Array of stickies as a JSON string.",
      "type": "string"
    }
  },
  "required": [
    "stickies"
  ]
}
```

</details>

## <code>figjam_<wbr>create_<wbr>connector</code>

Connects two nodes with a line: this is how flowcharts, diagrams and relation maps are built. The nodes must already be on the board — take the ids from the creation results. Magnets: AUTO (the default), TOP, BOTTOM, LEFT, RIGHT — they set the attachment point. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `startNodeId` | `string` | yes | — | Id of the start node. |
| `endNodeId` | `string` | yes | — | Id of the end node. |
| `label` | `string?` | — | `null` | Label on the connector. |
| `startMagnet` | `string` | — | `AUTO` | Magnet on the start node. |
| `endMagnet` | `string` | — | `AUTO` | Magnet on the end node. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "startNodeId": {
      "description": "Id of the start node.",
      "type": "string"
    },
    "endNodeId": {
      "description": "Id of the end node.",
      "type": "string"
    },
    "label": {
      "description": "Label on the connector.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "startMagnet": {
      "description": "Magnet on the start node.",
      "type": "string",
      "default": "AUTO"
    },
    "endMagnet": {
      "description": "Magnet on the end node.",
      "type": "string",
      "default": "AUTO"
    }
  },
  "required": [
    "startNodeId",
    "endNodeId"
  ]
}
```

</details>

## <code>figjam_<wbr>create_<wbr>shape_<wbr>with_<wbr>text</code>

Creates a shape with a label: a flowchart node, a process step, a diagram element. Types: ROUNDED_RECTANGLE (the default), DIAMOND, ELLIPSE, TRIANGLE_UP, TRIANGLE_DOWN, PARALLELOGRAM_RIGHT, PARALLELOGRAM_LEFT, ENG_DATABASE, ENG_QUEUE, ENG_FILE, ENG_FOLDER. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `text` | `string?` | — | `null` | Label on the shape. |
| `shapeType` | `string?` | — | `null` | Shape type. |
| `x` | `number?` | — | `null` | X coordinate. |
| `y` | `number?` | — | `null` | Y coordinate. |
| `width` | `number?` | — | `null` | Width in pixels. |
| `height` | `number?` | — | `null` | Height in pixels. |
| `fillColor` | `string?` | — | `null` | Fill color in hex, for example '#E1F5EE'. |
| `strokeColor` | `string?` | — | `null` | Stroke color in hex. |
| `fontSize` | `number?` | — | `null` | Label font size in pixels. |
| `strokeDashPattern` | `string?` | — | `null` | Stroke dash pattern as comma-separated numbers, for example '10,5'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "text": {
      "description": "Label on the shape.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "shapeType": {
      "description": "Shape type.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "width": {
      "description": "Width in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "height": {
      "description": "Height in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "fillColor": {
      "description": "Fill color in hex, for example \u0027#E1F5EE\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "strokeColor": {
      "description": "Stroke color in hex.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "fontSize": {
      "description": "Label font size in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "strokeDashPattern": {
      "description": "Stroke dash pattern as comma-separated numbers, for example \u002710,5\u0027.",
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

## <code>figjam_<wbr>create_<wbr>section</code>

Creates a section — a container for grouping board contents. To put elements inside it, set their coordinates within the section bounds and call appendChild through figma_execute. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `name` | `string?` | — | `null` | Section name. |
| `x` | `number?` | — | `null` | X coordinate. |
| `y` | `number?` | — | `null` | Y coordinate. |
| `width` | `number` | — | `1000` | Width in pixels. |
| `height` | `number` | — | `800` | Height in pixels. |
| `fillColor` | `string?` | — | `null` | Fill color in hex. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "name": {
      "description": "Section name.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "width": {
      "description": "Width in pixels.",
      "type": "number",
      "default": 1000
    },
    "height": {
      "description": "Height in pixels.",
      "type": "number",
      "default": 800
    },
    "fillColor": {
      "description": "Fill color in hex.",
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

## <code>figjam_<wbr>create_<wbr>table</code>

Creates a table on the board: comparison matrices, structured data. data is a JSON array of arrays of strings, one per table row, for example [["What","Who"],["Design","Ann"]]. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `rows` | `integer` | yes | — | Number of rows. |
| `columns` | `integer` | yes | — | Number of columns. |
| `data` | `string?` | — | `null` | Cell contents as a JSON string. |
| `x` | `number?` | — | `null` | X coordinate. |
| `y` | `number?` | — | `null` | Y coordinate. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "rows": {
      "description": "Number of rows.",
      "type": "integer"
    },
    "columns": {
      "description": "Number of columns.",
      "type": "integer"
    },
    "data": {
      "description": "Cell contents as a JSON string.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "rows",
    "columns"
  ]
}
```

</details>

## <code>figjam_<wbr>create_<wbr>code_<wbr>block</code>

Creates a code block on the board — for code snippets, configuration examples and technical notes. language, for example 'JAVASCRIPT', 'PYTHON', 'TYPESCRIPT', 'JSON', 'HTML', 'CSS'. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `code` | `string` | yes | — | Block contents. |
| `language` | `string?` | — | `null` | Highlighting language. |
| `x` | `number?` | — | `null` | X coordinate. |
| `y` | `number?` | — | `null` | Y coordinate. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "code": {
      "description": "Block contents.",
      "type": "string"
    },
    "language": {
      "description": "Highlighting language.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y coordinate.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "code"
  ]
}
```

</details>

## <code>figjam_<wbr>auto_<wbr>arrange</code>

Lays board nodes out in a grid, a row or a column — handy after creating elements in bulk. nodeIds is comma-separated ids, at most 500. layout: grid, horizontal, vertical. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeIds` | `string` | yes | — | Comma-separated node ids. |
| `layout` | `string` | — | `grid` | Layout type: grid, horizontal, vertical. |
| `spacing` | `number` | — | `40` | Gap between nodes in pixels. |
| `columns` | `integer?` | — | `null` | Number of columns for the grid. Defaults to the square root of the node count. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeIds": {
      "description": "Comma-separated node ids.",
      "type": "string"
    },
    "layout": {
      "description": "Layout type: grid, horizontal, vertical.",
      "type": "string",
      "default": "grid"
    },
    "spacing": {
      "description": "Gap between nodes in pixels.",
      "type": "number",
      "default": 40
    },
    "columns": {
      "description": "Number of columns for the grid. Defaults to the square root of the node count.",
      "type": [
        "integer",
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

## <code>figjam_<wbr>get_<wbr>board_<wbr>contents</code>

Reads the contents of a board: stickies, shapes, connectors, tables, code blocks and sections with their text and coordinates. Call it to learn what is already on the board, or to pull structured data out of it. nodeTypes is a comma-separated filter: STICKY, SHAPE_WITH_TEXT, CONNECTOR, TABLE, CODE_BLOCK, SECTION, FRAME, TEXT. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeTypes` | `string?` | — | `null` | Comma-separated node types. Empty means all of them. |
| `maxNodes` | `integer` | — | `500` | Cap on the number of nodes in the response. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeTypes": {
      "description": "Comma-separated node types. Empty means all of them.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "maxNodes": {
      "description": "Cap on the number of nodes in the response.",
      "type": "integer",
      "default": 500
    }
  }
}
```

</details>

## <code>figjam_<wbr>get_<wbr>connections</code>

Returns the relation graph of a board: which connectors join which nodes and with which labels. This is how a flowchart is read as a structure rather than as a picture. Requires the plugin.

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

