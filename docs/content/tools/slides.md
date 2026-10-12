# Slides

## <code>figma_<wbr>list_<wbr>slides</code>

The list of presentation slides: ids, names, grid positions and the skip flag. Works in Figma Slides files only. Requires the plugin.

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

## <code>figma_<wbr>get_<wbr>slide_<wbr>content</code>

The content tree of a slide: texts, shapes and frames with their properties. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id, for example '1:23'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id, for example \u00271:23\u0027.",
      "type": "string"
    }
  },
  "required": [
    "slideId"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>slide_<wbr>grid</code>

The two-dimensional slide grid: how the slides are laid out in rows and columns. Requires the plugin.

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

## <code>figma_<wbr>get_<wbr>slide_<wbr>transition</code>

The current transition settings of a slide: style, duration, easing curve. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    }
  },
  "required": [
    "slideId"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>focused_<wbr>slide</code>

The slide currently open in single-slide mode. Requires the plugin.

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

## <code>figma_<wbr>create_<wbr>slide</code>

Creates an empty slide. Its grid position can be set by row and column. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `row` | `integer?` | — | `null` | Row index in the grid, zero-based. |
| `col` | `integer?` | — | `null` | Column index in the grid, zero-based. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "row": {
      "description": "Row index in the grid, zero-based.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    },
    "col": {
      "description": "Column index in the grid, zero-based.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    }
  }
}
```

</details>

## <code>figma_<wbr>delete_<wbr>slide</code>

Deletes a slide from the presentation. The action is destructive — it is undone only through undo in Figma. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Id of the slide to delete. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Id of the slide to delete.",
      "type": "string"
    }
  },
  "required": [
    "slideId"
  ]
}
```

</details>

## <code>figma_<wbr>duplicate_<wbr>slide</code>

Duplicates a slide. The copy lands next to the original. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    }
  },
  "required": [
    "slideId"
  ]
}
```

</details>

## <code>figma_<wbr>reorder_<wbr>slides</code>

Rearranges the slides into a new grid. grid is a JSON array of arrays of ids, each nested array being one grid row: [["1:2","1:3"],["1:4"]]. The action is destructive: the order is replaced entirely. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `grid` | `string` | yes | — | New grid as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "grid": {
      "description": "New grid as a JSON string.",
      "type": "string"
    }
  },
  "required": [
    "grid"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>slide_<wbr>transition</code>

Sets the transition of a slide. style: NONE, DISSOLVE, SLIDE_FROM_LEFT/RIGHT/TOP/BOTTOM, PUSH_FROM_LEFT/RIGHT/TOP/BOTTOM, MOVE_FROM_LEFT/RIGHT/TOP/BOTTOM, SLIDE_OUT_TO_LEFT/RIGHT/TOP/BOTTOM, MOVE_OUT_TO_LEFT/RIGHT/TOP/BOTTOM, SMART_ANIMATE. curve: LINEAR, EASE_IN, EASE_OUT, EASE_IN_AND_OUT, GENTLE, QUICK, BOUNCY, SLOW. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |
| `style` | `string` | yes | — | Transition style. |
| `duration` | `number` | — | `0.4` | Duration in seconds, from 0.01 to 10. |
| `curve` | `string` | — | `EASE_IN_AND_OUT` | Easing curve. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    },
    "style": {
      "description": "Transition style.",
      "type": "string"
    },
    "duration": {
      "description": "Duration in seconds, from 0.01 to 10.",
      "type": "number",
      "default": 0.4
    },
    "curve": {
      "description": "Easing curve.",
      "type": "string",
      "default": "EASE_IN_AND_OUT"
    }
  },
  "required": [
    "slideId",
    "style"
  ]
}
```

</details>

## <code>figma_<wbr>skip_<wbr>slide</code>

Turns skipping a slide in presentation mode on and off. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |
| `skip` | `boolean` | yes | — | true skips it, false shows it. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    },
    "skip": {
      "description": "true skips it, false shows it.",
      "type": "boolean"
    }
  },
  "required": [
    "slideId",
    "skip"
  ]
}
```

</details>

## <code>figma_<wbr>add_<wbr>text_<wbr>to_<wbr>slide</code>

Adds text to a slide: font, color, alignment and the rest of the formatting. width enables line wrapping. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |
| `text` | `string` | yes | — | Text. |
| `x` | `number` | — | `100` | X coordinate. |
| `y` | `number` | — | `100` | Y coordinate. |
| `fontSize` | `number` | — | `24` | Font size in pixels. |
| `fontFamily` | `string` | — | `Inter` | Font family, for example 'Manrope', 'IBM Plex Sans'. |
| `fontStyle` | `string` | — | `Regular` | Font style, for example 'Bold', 'SemiBold', 'Medium'. |
| `color` | `string?` | — | `null` | Text color in hex, for example '#FFFFFF'. |
| `textAlign` | `string?` | — | `null` | Alignment: LEFT, CENTER, RIGHT. |
| `width` | `number?` | — | `null` | Width of the text block — it enables line wrapping. |
| `lineHeight` | `number?` | — | `null` | Line height in pixels. |
| `letterSpacing` | `number?` | — | `null` | Letter spacing in pixels. |
| `textCase` | `string?` | — | `null` | Case: ORIGINAL, UPPER, LOWER, TITLE. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    },
    "text": {
      "description": "Text.",
      "type": "string"
    },
    "x": {
      "description": "X coordinate.",
      "type": "number",
      "default": 100
    },
    "y": {
      "description": "Y coordinate.",
      "type": "number",
      "default": 100
    },
    "fontSize": {
      "description": "Font size in pixels.",
      "type": "number",
      "default": 24
    },
    "fontFamily": {
      "description": "Font family, for example \u0027Manrope\u0027, \u0027IBM Plex Sans\u0027.",
      "type": "string",
      "default": "Inter"
    },
    "fontStyle": {
      "description": "Font style, for example \u0027Bold\u0027, \u0027SemiBold\u0027, \u0027Medium\u0027.",
      "type": "string",
      "default": "Regular"
    },
    "color": {
      "description": "Text color in hex, for example \u0027#FFFFFF\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "textAlign": {
      "description": "Alignment: LEFT, CENTER, RIGHT.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "width": {
      "description": "Width of the text block \u2014 it enables line wrapping.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "lineHeight": {
      "description": "Line height in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "letterSpacing": {
      "description": "Letter spacing in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "textCase": {
      "description": "Case: ORIGINAL, UPPER, LOWER, TITLE.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "slideId",
    "text"
  ]
}
```

</details>

## <code>figma_<wbr>add_<wbr>shape_<wbr>to_<wbr>slide</code>

Adds a rectangle or an ellipse to a slide. shapeType: RECTANGLE or ELLIPSE. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |
| `shapeType` | `string` | yes | — | Shape type: RECTANGLE or ELLIPSE. |
| `x` | `number` | yes | — | X coordinate. |
| `y` | `number` | yes | — | Y coordinate. |
| `width` | `number` | yes | — | Width in pixels. |
| `height` | `number` | yes | — | Height in pixels. |
| `fillColor` | `string` | — | `#CCCCCC` | Fill color in hex, for example '#FF5733'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    },
    "shapeType": {
      "description": "Shape type: RECTANGLE or ELLIPSE.",
      "type": "string"
    },
    "x": {
      "description": "X coordinate.",
      "type": "number"
    },
    "y": {
      "description": "Y coordinate.",
      "type": "number"
    },
    "width": {
      "description": "Width in pixels.",
      "type": "number"
    },
    "height": {
      "description": "Height in pixels.",
      "type": "number"
    },
    "fillColor": {
      "description": "Fill color in hex, for example \u0027#FF5733\u0027.",
      "type": "string",
      "default": "#CCCCCC"
    }
  },
  "required": [
    "slideId",
    "shapeType",
    "x",
    "y",
    "width",
    "height"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>slide_<wbr>background</code>

Sets the slide background: it creates or updates a rectangle covering the whole slide. color is hex of the form '#181818'. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |
| `color` | `string` | yes | — | Background color in hex. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    },
    "color": {
      "description": "Background color in hex.",
      "type": "string"
    }
  },
  "required": [
    "slideId",
    "color"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>slides_<wbr>view_<wbr>mode</code>

Switches the presentation view: mode is 'grid' or 'single-slide'. Requires the plugin.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `mode` | `string` | yes | — | View mode: grid or single-slide. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "mode": {
      "description": "View mode: grid or single-slide.",
      "type": "string"
    }
  },
  "required": [
    "mode"
  ]
}
```

</details>

## <code>figma_<wbr>focus_<wbr>slide</code>

Moves to a slide in single-slide mode. Requires the plugin.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slideId` | `string` | yes | — | Slide id. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slideId": {
      "description": "Slide id.",
      "type": "string"
    }
  },
  "required": [
    "slideId"
  ]
}
```

</details>

