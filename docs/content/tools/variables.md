# Variables

## <code>figma_<wbr>create_<wbr>variable_<wbr>collection</code>

Creates a variable collection — a container of tokens with modes (themes). The collection is created first, then variables are put into it with figma_create_variable. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `name` | `string` | yes | — | Collection name, for example 'Colors' or 'Spacing'. |
| `initialModeName` | `string?` | — | `null` | Name of the first mode. By default Figma calls it 'Mode 1'. |
| `additionalModes` | `string?` | — | `null` | Additional modes, comma-separated, for example 'Dark, High Contrast'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "name": {
      "description": "Collection name, for example \u0027Colors\u0027 or \u0027Spacing\u0027.",
      "type": "string"
    },
    "initialModeName": {
      "description": "Name of the first mode. By default Figma calls it \u0027Mode 1\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "additionalModes": {
      "description": "Additional modes, comma-separated, for example \u0027Dark, High Contrast\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "name"
  ]
}
```

</details>

## <code>figma_<wbr>delete_<wbr>variable_<wbr>collection</code>

Deletes a variable collection together with all its variables. The operation is irreversible. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `collectionId` | `string` | yes | — | Collection id, for example 'VariableCollectionId:1:2'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "collectionId": {
      "description": "Collection id, for example \u0027VariableCollectionId:1:2\u0027.",
      "type": "string"
    }
  },
  "required": [
    "collectionId"
  ]
}
```

</details>

## <code>figma_<wbr>create_<wbr>variable</code>

Creates a variable in a collection. resolvedType: COLOR, FLOAT, STRING or BOOLEAN. A color in valuesByMode is set by the string '#RRGGBB'. Name hierarchy is made with a slash: 'color/brand/primary'. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `name` | `string` | yes | — | Variable name, for example 'color/brand/primary'. |
| `collectionId` | `string` | yes | — | Collection id. |
| `resolvedType` | `string` | yes | — | Type: COLOR, FLOAT, STRING, BOOLEAN. |
| `valuesByMode` | `string?` | — | `null` | Values per mode, JSON of the form {"1:0":"#FF0000","1:1":"#0000FF"}. |
| `description` | `string?` | — | `null` | Variable description. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "name": {
      "description": "Variable name, for example \u0027color/brand/primary\u0027.",
      "type": "string"
    },
    "collectionId": {
      "description": "Collection id.",
      "type": "string"
    },
    "resolvedType": {
      "description": "Type: COLOR, FLOAT, STRING, BOOLEAN.",
      "type": "string"
    },
    "valuesByMode": {
      "description": "Values per mode, JSON of the form {\u00221:0\u0022:\u0022#FF0000\u0022,\u00221:1\u0022:\u0022#0000FF\u0022}.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "description": {
      "description": "Variable description.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "name",
    "collectionId",
    "resolvedType"
  ]
}
```

</details>

## <code>figma_<wbr>update_<wbr>variable</code>

Changes the value of a variable in a specific mode. A color is set by the string '#RRGGBB', a number by a number, a reference to another variable by its id. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `variableId` | `string` | yes | — | Variable id, for example 'VariableID:1:23'. |
| `modeId` | `string` | yes | — | Mode id, for example '1:0'. |
| `value` | `string` | yes | — | New value: '#FF0000', a number, true/false or JSON. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "variableId": {
      "description": "Variable id, for example \u0027VariableID:1:23\u0027.",
      "type": "string"
    },
    "modeId": {
      "description": "Mode id, for example \u00271:0\u0027.",
      "type": "string"
    },
    "value": {
      "description": "New value: \u0027#FF0000\u0027, a number, true/false or JSON.",
      "type": "string"
    }
  },
  "required": [
    "variableId",
    "modeId",
    "value"
  ]
}
```

</details>

## <code>figma_<wbr>delete_<wbr>variable</code>

Deletes a variable. When it is used somewhere Figma refuses — remove the bindings first. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `variableId` | `string` | yes | — | Variable id. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "variableId": {
      "description": "Variable id.",
      "type": "string"
    }
  },
  "required": [
    "variableId"
  ]
}
```

</details>

## <code>figma_<wbr>rename_<wbr>variable</code>

Renames a variable. Slashes in the name define the group hierarchy. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `variableId` | `string` | yes | — | Variable id. |
| `newName` | `string` | yes | — | New name, for example 'color/brand/primary'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "variableId": {
      "description": "Variable id.",
      "type": "string"
    },
    "newName": {
      "description": "New name, for example \u0027color/brand/primary\u0027.",
      "type": "string"
    }
  },
  "required": [
    "variableId",
    "newName"
  ]
}
```

</details>

## <code>figma_<wbr>set_<wbr>variable_<wbr>description</code>

Sets the description of a variable — designers see it in the variables panel. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `variableId` | `string` | yes | — | Variable id. |
| `description` | `string` | yes | — | Description text. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "variableId": {
      "description": "Variable id.",
      "type": "string"
    },
    "description": {
      "description": "Description text.",
      "type": "string"
    }
  },
  "required": [
    "variableId",
    "description"
  ]
}
```

</details>

## <code>figma_<wbr>add_<wbr>mode</code>

Adds a mode to a collection — this is how themes (Dark) and breakpoints (Mobile) are made. The number of modes is limited by the Figma plan. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `collectionId` | `string` | yes | — | Collection id. |
| `modeName` | `string` | yes | — | Mode name, for example 'Dark'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "collectionId": {
      "description": "Collection id.",
      "type": "string"
    },
    "modeName": {
      "description": "Mode name, for example \u0027Dark\u0027.",
      "type": "string"
    }
  },
  "required": [
    "collectionId",
    "modeName"
  ]
}
```

</details>

## <code>figma_<wbr>rename_<wbr>mode</code>

Renames a mode of a collection. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `collectionId` | `string` | yes | — | Collection id. |
| `modeId` | `string` | yes | — | Mode id. |
| `newName` | `string` | yes | — | New mode name. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "collectionId": {
      "description": "Collection id.",
      "type": "string"
    },
    "modeId": {
      "description": "Mode id.",
      "type": "string"
    },
    "newName": {
      "description": "New mode name.",
      "type": "string"
    }
  },
  "required": [
    "collectionId",
    "modeId",
    "newName"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>token_<wbr>values</code>

Reads variables straight from the plugin, bypassing REST. It works on any Figma plan, unlike figma_get_variables. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `refresh` | `boolean` | — | `false` | Re-read the data in the plugin before reading. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "refresh": {
      "description": "Re-read the data in the plugin before reading.",
      "type": "boolean",
      "default": false
    }
  }
}
```

</details>

## <code>figma_<wbr>batch_<wbr>create_<wbr>variables</code>

Creates many variables in one collection at once: noticeably faster than one by one. The variables format is a JSON array: [{"name":"color/bg","resolvedType":"COLOR","valuesByMode":{"1:0":"#FFFFFF"}}]. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `collectionId` | `string` | yes | — | Collection id. |
| `variables` | `string` | yes | — | Array of variable descriptions as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "collectionId": {
      "description": "Collection id.",
      "type": "string"
    },
    "variables": {
      "description": "Array of variable descriptions as a JSON string.",
      "type": "string"
    }
  },
  "required": [
    "collectionId",
    "variables"
  ]
}
```

</details>

## <code>figma_<wbr>batch_<wbr>update_<wbr>variables</code>

Updates the values of many variables in one call. The updates format is a JSON array: [{"variableId":"VariableID:1:2","modeId":"1:0","value":"#FF0000"}]. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `updates` | `string` | yes | — | Array of updates as a JSON string. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "updates": {
      "description": "Array of updates as a JSON string.",
      "type": "string"
    }
  },
  "required": [
    "updates"
  ]
}
```

</details>

## <code>figma_<wbr>setup_<wbr>design_<wbr>tokens</code>

Rolls out the foundation of a design system in one call: it creates a collection with the modes you need and fills it with variables. The tokens format is a JSON array, the same as in figma_batch_create_variables. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `collectionName` | `string` | yes | — | Collection name, for example 'Design Tokens'. |
| `tokens` | `string` | yes | — | Array of variable descriptions as a JSON string. |
| `initialModeName` | `string?` | — | `null` | Name of the first mode, for example 'Light'. |
| `additionalModes` | `string?` | — | `null` | Additional modes, comma-separated, for example 'Dark'. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "collectionName": {
      "description": "Collection name, for example \u0027Design Tokens\u0027.",
      "type": "string"
    },
    "tokens": {
      "description": "Array of variable descriptions as a JSON string.",
      "type": "string"
    },
    "initialModeName": {
      "description": "Name of the first mode, for example \u0027Light\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "additionalModes": {
      "description": "Additional modes, comma-separated, for example \u0027Dark\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "collectionName",
    "tokens"
  ]
}
```

</details>

