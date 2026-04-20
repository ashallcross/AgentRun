| Capability | How it's used |
|---|---|
| `read_file` | Two calls at the start — first `artifacts/scan-results.md` (Audit Configuration block carries the selected pillars + scope; per-node blocks carry the raw evidence), then `artifacts/quality-scores.md` (per-node scores + severity bands + analyser's Cross-Node Observations). |
| `write_file` | One call at the end — writes `artifacts/audit-report.md` with At-a-Glance health grade, Executive Summary, root-cause-clustered findings, individual-node findings for Critical/High singletons, content-model observations, and a severity × effort prioritised action plan. Report stays under 250 lines. |
