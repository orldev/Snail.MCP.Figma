# Design system from code

## <code>figma_<wbr>ds_<wbr>analyze</code>

The first step of design system extraction: it breaks down the codebase of a production application. It detects the framework (React/Next/Astro/Angular/Web Components), the styling approaches (Tailwind v3/v4, CSS Modules, SCSS, Emotion, styled-components) and the vendor layers (shadcn/ui, Radix, MUI, Chakra and the rest); it collects a component inventory with a classification (vendored / wrapped / pure-vendor / bespoke), the props contract, the real usage count (the porting rank), the prop values seen at call sites (variant inference) and duplicate detection. Several targets means the shared design language of several applications at once. The full manifest is written to &lt;outDir&gt;/.extraction/analysis.json, the response carries a summary.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `targets` | `string` | yes | — | Application roots, comma-separated or as a JSON array. Absolute paths are safer: the server working directory is not your project. |
| `outDir` | `string?` | — | `null` | Where the design system package will be generated. Defaults to '&lt;first target&gt;/design-system'. |
| `include` | `string?` | — | `null` | Substrings of relative paths: when set, only matching files are scanned. |
| `exclude` | `string?` | — | `null` | Substrings of relative paths to skip. node_modules, dist, .next and the like are always skipped. |
| `maxFiles` | `integer` | — | `5000` | Cap on the number of files per target. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "targets": {
      "description": "Application roots, comma-separated or as a JSON array. Absolute paths are safer: the server working directory is not your project.",
      "type": "string"
    },
    "outDir": {
      "description": "Where the design system package will be generated. Defaults to \u0027\u003Cfirst target\u003E/design-system\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "include": {
      "description": "Substrings of relative paths: when set, only matching files are scanned.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "exclude": {
      "description": "Substrings of relative paths to skip. node_modules, dist, .next and the like are always skipped.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "maxFiles": {
      "description": "Cap on the number of files per target.",
      "type": "integer",
      "default": 5000
    }
  },
  "required": [
    "targets"
  ]
}
```

</details>

## <code>figma_<wbr>ds_<wbr>extract_<wbr>tokens</code>

Mines design tokens FROM THE CODEBASE into canonical DTCG (plus, optionally, CSS variables, Tailwind, SCSS, TypeScript). It starts with what is declared: the :root and @theme custom properties (light and dark themes become modes of one token), SCSS variables, tailwind.config values, shadcn HSL triples; then it promotes repeated raw values (colors, spacing, radii, font sizes) above the frequency threshold. Every token carries its origin in $extensions: file, line, confidence, frequency. The names are structural, as found — semantic renaming is a separate pass with the team. The result is immediately usable by figma_import_tokens. Requires a figma_ds_analyze run over the same outDir. Values are pulled out of the DESIGN by a different tool — figma_extract_style_values.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `outDir` | `string?` | — | `null` | The directory used in figma_ds_analyze: .extraction/analysis.json is read from it. |
| `targets` | `string?` | — | `null` | Scan these roots instead of the targets from the manifest. Rarely needed. |
| `formats` | `string` | — | `dtcg,css-vars` | Comma-separated file formats under &lt;outDir&gt;/tokens/. dtcg is always written. |
| `minFrequency` | `integer` | — | `4` | How many times a raw value must occur to become a token. |
| `write` | `boolean` | — | `true` | Write files to disk. false is a dry run and returns the document in the response. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "outDir": {
      "description": "The directory used in figma_ds_analyze: .extraction/analysis.json is read from it.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "targets": {
      "description": "Scan these roots instead of the targets from the manifest. Rarely needed.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "formats": {
      "description": "Comma-separated file formats under \u003CoutDir\u003E/tokens/. dtcg is always written.",
      "type": "string",
      "default": "dtcg,css-vars"
    },
    "minFrequency": {
      "description": "How many times a raw value must occur to become a token.",
      "type": "integer",
      "default": 4
    },
    "write": {
      "description": "Write files to disk. false is a dry run and returns the document in the response.",
      "type": "boolean",
      "default": true
    }
  }
}
```

</details>

## <code>figma_<wbr>ds_<wbr>scaffold</code>

Assembles the scaffold of a design system package in outDir from a finished analysis and tokens: package.json (the application framework goes into peer dependencies), the src/components layout, token files through the shared formatter engine, MDX showcases (tokens, typography, icons) and a README with the workflow. Storybook itself is NOT installed: after the scaffold run `npm create storybook@latest` in outDir — its CLI detects the framework and installs the current version itself. By default it only fills gaps: existing files are skipped unless force is passed. Token files are always refreshed.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `outDir` | `string` | yes | — | The directory used in figma_ds_analyze and figma_ds_extract_tokens. |
| `packageName` | `string?` | — | `null` | npm package name of the design system, for example '@acme/design-system'. |
| `framework` | `string?` | — | `null` | Scaffold framework. Defaults to the first one the analysis found. Storybook has no .astro renderer: for astro the workshop is built on react-vite. |
| `formats` | `string` | — | `dtcg,css-vars` | Comma-separated token file formats. |
| `force` | `boolean` | — | `false` | Overwrite scaffold files that already exist. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "outDir": {
      "description": "The directory used in figma_ds_analyze and figma_ds_extract_tokens.",
      "type": "string"
    },
    "packageName": {
      "description": "npm package name of the design system, for example \u0027@acme/design-system\u0027.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "framework": {
      "description": "Scaffold framework. Defaults to the first one the analysis found. Storybook has no .astro renderer: for astro the workshop is built on react-vite.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "formats": {
      "description": "Comma-separated token file formats.",
      "type": "string",
      "default": "dtcg,css-vars"
    },
    "force": {
      "description": "Overwrite scaffold files that already exist.",
      "type": "boolean",
      "default": false
    }
  },
  "required": [
    "outDir"
  ]
}
```

</details>

## <code>figma_<wbr>ds_<wbr>setup_<wbr>storybook</code>

Wires a freshly installed Storybook workshop to the extracted design system. Run it AFTER `npm create storybook@latest` in outDir. It generates .storybook/preview.css (a Tailwind entry point with the mined tokens, the @theme utility map of the source application, its @utility, @layer base and @font-face, with a dark variant covering both .dark and [data-theme]), copies self-hosted fonts into staticDirs and edits main.js (the tailwind plugin for vite and the automatic JSX runtime — without it stories fail on 'React is not defined') and preview.jsx (the preview.css import plus a theme panel with the mined modes). Running it again is safe; everything that cannot be fixed safely comes back as a list of manual steps.

*may overwrite or delete · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `outDir` | `string` | yes | — | Directory of the design system package with a freshly installed .storybook. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "outDir": {
      "description": "Directory of the design system package with a freshly installed .storybook.",
      "type": "string"
    }
  },
  "required": [
    "outDir"
  ]
}
```

</details>

## <code>figma_<wbr>ds_<wbr>extract_<wbr>component</code>

Breaks ONE component of the inventory down deeply for porting: the source (with a size cap), the local import closure (relative imports worth following), the props contract, the variants seen at call sites, the classification against the vendor, the styling touchpoints (classes, CSS module imports, custom properties in use) and a ready CSF3 story scaffold with variant stories taken from real usage. Next you port the component into &lt;outDir&gt;/src/components/&lt;Name&gt;/ and record the progress with figma_ds_status.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `outDir` | `string` | yes | — | The directory used in figma_ds_analyze. |
| `component` | `string` | yes | — | Component name from the inventory: see topByUsage in the figma_ds_analyze response or the manifest itself. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "outDir": {
      "description": "The directory used in figma_ds_analyze.",
      "type": "string"
    },
    "component": {
      "description": "Component name from the inventory: see topByUsage in the figma_ds_analyze response or the manifest itself.",
      "type": "string"
    }
  },
  "required": [
    "outDir",
    "component"
  ]
}
```

</details>

## <code>figma_<wbr>ds_<wbr>verify</code>

Deterministic checks of the extracted design system package — the gate before handover or the trip into Figma. It checks: tokens.json parses as DTCG and every alias resolves (importable); the generated token files carry no quoted CSS functional expressions (they kill transitions); every var() consumed in component and preview CSS resolves in the workshop; every component directory has a story file and an index barrel; every portable component of the inventory has a status. It also reports the readiness of the return trip into Figma with the exact figma_import_tokens call.

*read-only · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `outDir` | `string` | yes | — | Design system package directory. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "outDir": {
      "description": "Design system package directory.",
      "type": "string"
    }
  },
  "required": [
    "outDir"
  ]
}
```

</details>

## <code>figma_<wbr>ds_<wbr>status</code>

Reads and updates the progress of design system porting. It lives in &lt;outDir&gt;/.extraction/status.json, so long work survives session boundaries. A call with outDir alone returns a summary; pass component and status to record the state of a component.

*only adds · repeat-safe · the connected file only.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `outDir` | `string` | yes | — | The directory used in figma_ds_analyze. |
| `component` | `string?` | — | `null` | Component name from the inventory. Empty just reads the progress. |
| `status` | `string?` | — | `null` | New status: pending, in-progress, ported, skipped. |
| `notes` | `string?` | — | `null` | Why it was skipped, what was merged, what to watch out for. |
| `storyFile` | `string?` | — | `null` | Relative path of the story file once it is written. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "outDir": {
      "description": "The directory used in figma_ds_analyze.",
      "type": "string"
    },
    "component": {
      "description": "Component name from the inventory. Empty just reads the progress.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "status": {
      "description": "New status: pending, in-progress, ported, skipped.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "notes": {
      "description": "Why it was skipped, what was merged, what to watch out for.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "storyFile": {
      "description": "Relative path of the story file once it is written.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "outDir"
  ]
}
```

</details>

