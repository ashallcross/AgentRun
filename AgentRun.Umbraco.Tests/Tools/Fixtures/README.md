# fetch_url regression fixtures

These three HTML payloads back the Story 9.7 regression tests for `FetchUrlTool`'s
response-offloading contract. They were extracted from the failing instance
`caed201cbc5d4a9eb6a68f1ff6aafb06` of the Content Quality Audit example workflow,
which was the production reproduction of the Sonnet 4.6 empty-turn stall caused
by raw HTML being inlined into the conversation context.

| File | Source URL | jsonl line | Approx size |
|---|---|---|---|
| `fetch-url-100kb.html` | `https://wearecogworks.com/` | line 8 (`tool` role) | ~107 KB |
| `fetch-url-500kb.html` | `https://umbraco.com/products/cms/` | line 11 (`tool` role) | ~207 KB |
| `fetch-url-1500kb.html` | `https://www.bbc.co.uk/news` | line 14 (`tool` role) | ~1 MB |

The architect's intent was small/medium/large tier coverage, anchored on the BBC
payload because that is the one that broke production. The exact byte counts are
not the gate.

To re-extract from the original instance:

```
AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/caed201cbc5d4a9eb6a68f1ff6aafb06/conversation-scanner.jsonl
```

Each `tool`-role line has a `toolResult` string field whose contents are the
exact bytes that the previous `fetch_url` contract would have inlined into
the conversation.
