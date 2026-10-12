# Slots

## <code>figma_<wbr>create_<wbr>slot</code>

Creates a SlotNode inside a component and at the same time defines a linked property of type SLOT with the same name (renaming the slot renames the property). It works on a standalone COMPONENT and on the variants inside a COMPONENT_SET — call it once per variant then. GRID layout on slots is forbidden. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the COMPONENT node to add the slot to. |
| `name` | `string?` | — | `null` | Slot layer name, for example 'Content' or 'Footer'. |
| `width` | `number?` | — | `null` | Initial width in pixels. |
| `height` | `number?` | — | `null` | Initial height in pixels. |
| `layoutMode` | `string?` | — | `null` | Auto layout mode of the slot: NONE, HORIZONTAL, VERTICAL. GRID is not supported. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the COMPONENT node to add the slot to.",
      "type": "string"
    },
    "name": {
      "description": "Slot layer name, for example \u0027Content\u0027 or \u0027Footer\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "width": {
      "description": "Initial width in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "height": {
      "description": "Initial height in pixels.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "layoutMode": {
      "description": "Auto layout mode of the slot: NONE, HORIZONTAL, VERTICAL. GRID is not supported.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>get_<wbr>slots</code>

The list of slots of a component, a component set or an instance: ids, names, property keys, sizes and the current contents. Call it on an instance before figma_append_to_slot to learn the slot names. Requires the plugin.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the COMPONENT, COMPONENT_SET or INSTANCE node. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the COMPONENT, COMPONENT_SET or INSTANCE node.",
      "type": "string"
    }
  },
  "required": [
    "nodeId"
  ]
}
```

</details>

## <code>figma_<wbr>append_<wbr>to_<wbr>slot</code>

Puts content into an instance slot: it either clones sourceNodeId there or creates a new node by nodeType (FRAME, RECTANGLE, ELLIPSE, TEXT, LINE, POLYGON, STAR, VECTOR). A slot is addressed directly by slotId or by the pair instanceId + slotName. Widgets, stickies and bare ComponentNodes do not go into a slot. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slotId` | `string?` | — | `null` | SlotNode id — from figma_get_slots. |
| `instanceId` | `string?` | — | `null` | Instance id. Works together with slotName. |
| `slotName` | `string?` | — | `null` | Slot layer name on the instance, for example 'Content'. |
| `sourceNodeId` | `string?` | — | `null` | The node to clone into the slot. |
| `nodeType` | `string?` | — | `null` | Type of the new node instead of cloning. |
| `properties` | `string?` | — | `null` | Properties of the node being created as JSON: {"name":"…","text":"…","width":100}. |
| `clone` | `boolean` | — | `true` | With sourceNodeId: clone (true) or move (false). |
| `clearExisting` | `boolean` | — | `false` | Delete the previous slot contents before inserting. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slotId": {
      "description": "SlotNode id \u2014 from figma_get_slots.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "instanceId": {
      "description": "Instance id. Works together with slotName.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "slotName": {
      "description": "Slot layer name on the instance, for example \u0027Content\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "sourceNodeId": {
      "description": "The node to clone into the slot.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "nodeType": {
      "description": "Type of the new node instead of cloning.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "properties": {
      "description": "Properties of the node being created as JSON: {\u0022name\u0022:\u0022\u2026\u0022,\u0022text\u0022:\u0022\u2026\u0022,\u0022width\u0022:100}.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "clone": {
      "description": "With sourceNodeId: clone (true) or move (false).",
      "type": "boolean",
      "default": true
    },
    "clearExisting": {
      "description": "Delete the previous slot contents before inserting.",
      "type": "boolean",
      "default": false
    }
  }
}
```

</details>

## <code>figma_<wbr>reset_<wbr>slot</code>

Clears a slot, returning it to its default state. It is addressed the same way as figma_append_to_slot: slotId or instanceId + slotName. Requires the plugin.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `slotId` | `string?` | — | `null` | SlotNode id. |
| `instanceId` | `string?` | — | `null` | Instance id. Works together with slotName. |
| `slotName` | `string?` | — | `null` | Slot layer name on the instance. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "slotId": {
      "description": "SlotNode id.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "instanceId": {
      "description": "Instance id. Works together with slotName.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "slotName": {
      "description": "Slot layer name on the instance.",
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

## <code>figma_<wbr>add_<wbr>slot_<wbr>property</code>

The manual alternative to figma_create_slot: it defines a property of type SLOT and binds it to an already existing frame. The frame must be a direct child of the component (of its variant, for a set), must not use GRID layout and must not sit inside another slot. preferredValues is a JSON array of the form [{"type":"COMPONENT","key":"…"}]. For new slots figma_create_slot is better. Requires the plugin.

*only adds · every call adds · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `nodeId` | `string` | yes | — | Id of the COMPONENT or COMPONENT_SET node. |
| `propertyName` | `string` | yes | — | Slot property name, for example 'Content'. |
| `frameNodeId` | `string` | yes | — | Id of the frame that becomes the content area of the slot. |
| `description` | `string?` | — | `null` | Description of the slot property. |
| `preferredValues` | `string?` | — | `null` | Preferred components for the contents, a JSON array. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "nodeId": {
      "description": "Id of the COMPONENT or COMPONENT_SET node.",
      "type": "string"
    },
    "propertyName": {
      "description": "Slot property name, for example \u0027Content\u0027.",
      "type": "string"
    },
    "frameNodeId": {
      "description": "Id of the frame that becomes the content area of the slot.",
      "type": "string"
    },
    "description": {
      "description": "Description of the slot property.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "preferredValues": {
      "description": "Preferred components for the contents, a JSON array.",
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
    "frameNodeId"
  ]
}
```

</details>

