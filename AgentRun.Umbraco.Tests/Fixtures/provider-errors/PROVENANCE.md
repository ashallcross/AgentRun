# Provider error fixtures

These JSON files mirror the response bodies that OpenAI and Azure OpenAI return
for the listed failure modes. They feed `LlmErrorClassifierTests` so the
classifier is exercised against real-world error shapes rather than only
hand-rolled exception text.

**Source:** documented examples from the public OpenAI and Azure OpenAI API
reference pages (captured 2026-04-16). Not captured from live calls — Adam
chose not to burn API credit for this. Per Story 10.13 Dev Notes "Test fixture
pragma": these are sufficient for classifier-shape testing because we match on
JSON shape, not live behaviour. Future maintainers updating fixtures should
either re-capture from docs or capture live (and update this note).

| File | Provider | Scenario | Mapped HTTP status |
| --- | --- | --- | --- |
| `openai-billing.json` | OpenAI | Quota exceeded | 429 |
| `openai-auth.json` | OpenAI | Invalid API key | 401 |
| `openai-rate-limit.json` | OpenAI | TPM rate limit | 429 |
| `azure-quota.json` | Azure OpenAI | Token quota exceeded | 429 |
| `azure-auth.json` | Azure OpenAI | Invalid subscription key | 401 |
| `azure-rate-limit.json` | Azure OpenAI | Generic rate limit | 429 |

Anthropic shapes are intentionally absent — `LlmErrorClassifierTests` already
covers them well via the existing message-based tests, and Anthropic is the
default Umbraco.AI provider with the most coverage in real usage.
