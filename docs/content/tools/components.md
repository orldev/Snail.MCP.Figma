# Components

## <code>figma_<wbr>search_<wbr>components</code>

Searches components in the open file by a substring of the name. Returns the ids and keys needed by figma_instantiate_component. Ids live only inside the current session — do not reuse them from past conversations. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `query` | `string?` | — | `null` | Substring of the name. Empty returns every component of the file. |
| `limit` | `integer` | — | `50` | Maximum number of results. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "query": {
      "description": "Substring of the name. Empty returns every component of the file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "limit": {
      "description": "Maximum number of results.",
      "type": "integer",
      "default": 50
    }
  }
}
```

</details>

## <code>figma_<wbr>instantiate_<wbr>component</code>

Places a component instance on the canvas. The component is addressed by key (componentKey) or by node id (nodeId). Take keys and ids from figma_search_components in the same session. overrides is JSON with the instance property values. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `componentKey` | `string?` | — | `null` | Component key from a library. |
| `nodeId` | `string?` | — | `null` | Id of the component node in the current file. |
| `parentId` | `string?` | — | `null` | Id of the parent to put the instance into. |
| `position` | `string?` | — | `null` | Position as JSON {"x":0,"y":0}. |
| `overrides` | `string?` | — | `null` | Instance property values as JSON. |
| `variant` | `string?` | — | `null` | Set variant as JSON, for example {"Size":"Large"}. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "componentKey": {
      "description": "Component key from a library.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "nodeId": {
      "description": "Id of the component node in the current file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "parentId": {
      "description": "Id of the parent to put the instance into.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "position": {
      "description": "Position as JSON {\u0022x\u0022:0,\u0022y\u0022:0}.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "overrides": {
      "description": "Instance property values as JSON.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "variant": {
      "description": "Set variant as JSON, for example {\u0022Size\u0022:\u0022Large\u0022}.",
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

## <code>figma_<wbr>create_<wbr>component_<wbr>set</code>

Builds a component set out of existing components or from a property matrix. componentIds is a JSON array of ids; properties is JSON of the form {"Size":["S","M"],"State":["Default","Hover"]}. Large matrices take a while to build — the timeout grows with the number of variants. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `name` | `string` | yes | — | Set name. |
| `componentIds` | `string?` | — | `null` | Component ids as a JSON array. |
| `properties` | `string?` | — | `null` | Property matrix as JSON. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "name": {
      "description": "Set name.",
      "type": "string"
    },
    "componentIds": {
      "description": "Component ids as a JSON array.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "properties": {
      "description": "Property matrix as JSON.",
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

## <code>figma_<wbr>analyze_<wbr>component_<wbr>set</code>

Breaks a variant set down: which property axes exist, which combinations are there and which are missing. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the COMPONENT_SET node. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the COMPONENT_SET node.",
      "type": "string"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>arrange_<wbr>component_<wbr>set</code>

Lays the variants inside a set out in a grid: rows are the first property axis, columns the second. Makes a set readable after variants were added in bulk. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the COMPONENT_SET node. |
| `columnGap` | `number` | — | `24` | Horizontal gap between variants. |
| `rowGap` | `number` | — | `24` | Vertical gap between variants. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the COMPONENT_SET node.",
      "type": "string"
    },
    "columnGap": {
      "description": "Horizontal gap between variants.",
      "type": "number",
      "default": 24
    },
    "rowGap": {
      "description": "Vertical gap between variants.",
      "type": "number",
      "default": 24
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>add_<wbr>component_<wbr>property</code>

Adds a property to a component. type: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT, SLOT. Every type except SLOT needs a default value. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the component or variant set. |
| `propertyName` | `string` | yes | — | Property name, for example 'Label'. |
| `type` | `string` | yes | — | Type: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT, SLOT. |
| `defaultValue` | `string?` | — | `null` | Default value. For BOOLEAN it is true/false, for VARIANT the variant name. |
| `variantOptions` | `string?` | — | `null` | Comma-separated value options for VARIANT. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the component or variant set.",
      "type": "string"
    },
    "propertyName": {
      "description": "Property name, for example \u0027Label\u0027.",
      "type": "string"
    },
    "type": {
      "description": "Type: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT, SLOT.",
      "type": "string"
    },
    "defaultValue": {
      "description": "Default value. For BOOLEAN it is true/false, for VARIANT the variant name.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "variantOptions": {
      "description": "Comma-separated value options for VARIANT.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "nodeId",
    "propertyName",
    "type"
  ]
}
```

</details>

## <code>figma_<wbr>edit_<wbr>component_<wbr>property</code>

Changes the default value of a component property. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Component id. |
| `propertyName` | `string` | yes | — | Property name. |
| `newValue` | `string` | yes | — | New default value. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Component id.",
      "type": "string"
    },
    "propertyName": {
      "description": "Property name.",
      "type": "string"
    },
    "newValue": {
      "description": "New default value.",
      "type": "string"
    }
  },
  "required": [
    "nodeId",
    "propertyName",
    "newValue"
  ]
}
```

</details>

## <code>figma_<wbr>delete_<wbr>component_<wbr>property</code>

Deletes a component property. Every instance loses the matching override. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Component id. |
| `propertyName` | `string` | yes | — | Property name. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Component id.",
      "type": "string"
    },
    "propertyName": {
      "description": "Property name.",
      "type": "string"
    }
  },
  "required": [
    "nodeId",
    "propertyName"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>component_<wbr>details</code>

Reads a component straight from the plugin instead of REST: the current state of the file is visible, unsaved edits included. deep=true returns the tree with variable bindings, reactions and constraints. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Node id. |
| `deep` | `boolean` | — | `false` | Deep breakdown with styles, tokens and states. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Node id.",
      "type": "string"
    },
    "deep": {
      "description": "Deep breakdown with styles, tokens and states.",
      "type": "boolean",
      "default": false
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

