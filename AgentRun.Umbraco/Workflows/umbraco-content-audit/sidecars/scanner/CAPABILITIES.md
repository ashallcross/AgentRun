| Capability | How it's used |
|---|---|
| `list_content_types` | One call at the start of a scan — learns the content model (page types, required vs optional properties, compositions, unused types). |
| `list_content` | One call after `list_content_types` — retrieves the published content inventory (IDs, names, types, URLs, levels), optionally scoped by content type or subtree. |
| `get_content` | One call per node, one node per assistant turn. Pulls full property set + template + last-updated for every node in the inventory. Sequential is a hard rule — parallel calls stall the workflow. |
| `write_file` | Two calls at the end: first `artifacts/content-inventory.md`, then `artifacts/scan-results.md` (with the Audit Configuration block mandatory at the top). Both must be written before the step is complete. |
