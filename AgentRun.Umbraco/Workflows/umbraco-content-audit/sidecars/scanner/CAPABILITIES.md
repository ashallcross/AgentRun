| Capability | How it's used |
|---|---|
| `list_content_types` | One call at the start of a scan — learns the content model (page types, required vs optional properties, compositions, unused types). |
| `list_content` | One call after `list_content_types` — retrieves the published content inventory (IDs, names, types, URLs, levels), optionally scoped by content type or subtree. |
| `get_content` | One call per node, one node per assistant turn. Pulls full property set + template + last-updated for every node in the inventory. Sequential is a hard rule — parallel calls stall the workflow. |
| `write_file` | Two calls at the end: first `artifacts/content-inventory.md`, then `artifacts/scan-results.md` (with the Audit Configuration block mandatory at the top). Both must be written before the step is complete. |
| `get_ai_context` | One optional call — Brand Pillar Pre-Validation Gate. When `brand_voice_context` is non-empty, fetch the Umbraco.AI Context by alias before any other tool call. On alias_not_found, emit the Brand Pillar Halt Message and HALT; on success, proceed to `list_content_types`. Skipped entirely when Brand is disabled. |
| `search_content` | Optional discovery probe (max 3–5 calls per step). Use for orphan-terminology audits (find pages still using deprecated terms) and cross-topic consistency checks (sample content by keyword rather than tree position). Additive to `list_content` enumeration, never a replacement. |
