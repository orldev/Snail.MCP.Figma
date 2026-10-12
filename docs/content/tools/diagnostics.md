# Diagnostics

## <code>csharp_<wbr>diagnose</code>

Self-check of the Snail.MCP.Figma server: the active configuration, the presence of the Snail Bridge JS plugin and the state of the cache. Call it when you suspect the server is misconfigured.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `probeDatabase` | `boolean` | — | `false` | Open the cache file and check that it reads. |
| `probeToken` | `boolean` | — | `false` | Call GET /v1/me to prove the Figma token actually works; costs one cheap request. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "probeDatabase": {
      "description": "Open the cache file and check that it reads.",
      "type": "boolean",
      "default": false
    },
    "probeToken": {
      "description": "Call GET /v1/me to prove the Figma token actually works; costs one cheap request.",
      "type": "boolean",
      "default": false
    }
  }
}
```

</details>

