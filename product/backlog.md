# Backlog (ranked)

Scoring columns are a lightweight RICE-style score:
- Impact (1–5), Reach (1–5), Confidence (0.5–1.0), Effort (1–5)

## P0: must be solid for friend testing
1. **Friend-test “first 10 minutes” flow**: clear steps, common errors, where configs live
   - Impact 5, Reach 5, Conf 0.8, Effort 2
2. **Visible latency + user feedback**: “Listening… Thinking… Speaking…” states + timeouts
   - Impact 5, Reach 4, Conf 0.7, Effort 3
3. **Cancel behavior**: Ctrl+Alt+X reliably stops TTS and cancels in-flight LLM/STT
   - Impact 5, Reach 4, Conf 0.7, Effort 3
4. **Failure modes are kid-friendly**: short spoken fallback message and one actionable next step
   - Impact 4, Reach 5, Conf 0.8, Effort 2

## P1: improves learning + reduces repeated questions
5. **Answer style control**: default “short + actionable”; optionally “explain more”
   - Impact 4, Reach 4, Conf 0.7, Effort 2
6. **Clarifying question loop**: if the question is ambiguous, ask 1 follow-up
   - Impact 4, Reach 3, Conf 0.6, Effort 3
7. **Per-game context**: remember game name + basic objective for the session
   - Impact 3, Reach 3, Conf 0.6, Effort 3

## P2: platform hardening
8. **Capture robustness**: Windows Graphics Capture fallback when `CopyFromScreen` fails
   - Impact 4, Reach 3, Conf 0.6, Effort 4
9. **Cost controls**: model choice and max tokens/image scaling defaults
   - Impact 3, Reach 4, Conf 0.7, Effort 2

## P3: future / strategic
10. **Key management strategy for broad distribution** (Managed Identity proxy / broker)
    - Impact 5, Reach 5, Conf 0.5, Effort 5
11. **Offline evaluation set + LLM-as-judge**
    - Impact 4, Reach 4, Conf 0.5, Effort 4

---

## How to add items
Add a new row, give it a short title, a one-line outcome, and rough scores. If it’s bigger than ~1 day of work, it probably deserves a PRD.
