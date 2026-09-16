# Vendored Figma API artifacts

Machine-readable contracts the guard tests in `tests/Contracts/` compare our surface against.

| File | Source package | Version | Vendored |
|------|----------------|---------|----------|
| `openapi.yaml` | `@figma/rest-api-spec` | 0.42.0 | 2026-08-24 |
| `plugin-api.d.ts` | `@figma/plugin-typings` | 1.134.0 | 2026-08-24 |

Refresh: `npm pack @figma/rest-api-spec @figma/plugin-typings`, copy
`package/openapi/openapi.yaml` and `package/plugin-api.d.ts` here, update this table.
The monthly `contracts` job in `.github/workflows/ci.yml` runs the same tests against the
LATEST packages, so a red scheduled run means Figma changed something we use.
