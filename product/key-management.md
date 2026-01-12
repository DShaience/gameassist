# Key Management Strategy (draft)

## Context
Friends testing can use the developer’s keys, but broader distribution cannot safely embed API keys in a client app.

## Phase 0 (friends-only)
- Keys remain in local config files created next to the EXE.
- Communicate clearly: “friends build; do not share publicly.”

## Phase 1 (small beta)
Goal: reduce blast radius without heavy infra.
- Consider per-tester keys with limited quotas.
- Rotate keys regularly.

## Phase 2 (real distribution)
Goal: no long-lived secrets in the client.

Options:
1) **Broker service** (recommended): app calls your service; service calls Foundry/Speech.
   - Pros: no keys on client; can enforce rate limits; can add abuse protection.
   - Cons: you operate a service; adds latency; costs.
2) **User brings their own Azure keys**: user configures their own Foundry/Speech.
   - Pros: simplest operationally; no central service.
   - Cons: setup friction; not kid-friendly.
3) **Managed Identity**: generally works for server-to-Azure, not for arbitrary client devices.
   - Likely requires a broker anyway.

Recommended next step:
- For a public beta, build a minimal broker with rate limiting and basic auth.
