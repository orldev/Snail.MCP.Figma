# Comments

## <code>figma_<wbr>get_<wbr>comments</code>

File comments: discussion threads with the author, the text, the time and the node the comment is pinned to. includeResolved=true also shows resolved threads.

*read-only · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. Optional if the file has already been used. |
| `asMarkdown` | `boolean` | — | `false` | Return comment text as Markdown. |
| `includeResolved` | `boolean` | — | `false` | Include resolved threads. |

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
    "asMarkdown": {
      "description": "Return comment text as Markdown.",
      "type": "boolean",
      "default": false
    },
    "includeResolved": {
      "description": "Include resolved threads.",
      "type": "boolean",
      "default": false
    }
  }
}
```

</details>

## <code>figma_<wbr>post_<wbr>comment</code>

Leaves a comment in the file, optionally pinned to a node. Fits after figma_check_design_parity, when the code is the source of truth and the designer has to hear about the mismatch. replyToCommentId turns the comment into a reply in an existing thread. @ mentions are a Figma UI feature: in the text they stay plain words.

*only adds · every call adds · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `message` | `string` | yes | — | Comment text. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. |
| `nodeId` | `string?` | — | `null` | Id of the node to pin the comment to. |
| `x` | `number?` | — | `null` | X coordinate of the point on the canvas. Works together with nodeId. |
| `y` | `number?` | — | `null` | Y coordinate of the point on the canvas. Works together with nodeId. |
| `replyToCommentId` | `string?` | — | `null` | Id of the comment being replied to. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "message": {
      "description": "Comment text.",
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
    "nodeId": {
      "description": "Id of the node to pin the comment to.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "x": {
      "description": "X coordinate of the point on the canvas. Works together with nodeId.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "y": {
      "description": "Y coordinate of the point on the canvas. Works together with nodeId.",
      "type": [
        "number",
        "null"
      ],
      "default": null
    },
    "replyToCommentId": {
      "description": "Id of the comment being replied to.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "message"
  ]
}
```

</details>

## <code>figma_<wbr>delete_<wbr>comment</code>

Deletes a comment by id. figma_get_comments provides the ids.

*may overwrite or delete · repeat-safe · may reach the REST API.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `commentId` | `string` | yes | — | Comment id. |
| `fileUrl` | `string?` | — | `null` | Link to the Figma file. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "commentId": {
      "description": "Comment id.",
      "type": "string"
    },
    "fileUrl": {
      "description": "Link to the Figma file.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "commentId"
  ]
}
```

</details>

