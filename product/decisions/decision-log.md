# Decision Log

Record decisions that affect product direction, UX defaults, privacy posture, and platform scope.

Template:
- Date:
- Decision:
- Context:
- Options considered:
- Why this choice:
- Follow-ups:

---

## 2026-01-12 — Windows-first friend-shippable MVP
- **Decision:** Focus on Windows PC games; ship a portable EXE for friend testing.
- **Context:** Current product works on the author’s PC; next step is external validation.
- **Options:**
  1) Keep iterating privately
  2) Ship portable EXE to friends
  3) Move directly to store/distribution
- **Why:** Fastest path to real usage feedback with minimal distribution overhead.
- **Follow-ups:** Document “friends-only keys” risk and define a key management strategy for broader release.

## 2026-01-12 — Cloud-dependent inference (on purpose)
- **Decision:** Prefer cloud models to avoid competing with game resources.
- **Why:** A kid experience that tanks game performance fails even if answers are good.

---

## 2026-01-13 — Earcons over spoken state announcements
- **Decision:** Use non-speech audio cues (earcons) instead of spoken state announcements like "Listening..." or "Thinking...".
- **Context:** PRD 0001 required feedback for state changes to remove the "silent waiting = did it break?" feeling.
- **Options:**
  1) Spoken state announcements ("Listening...", "Thinking...", "Speaking...")
  2) Silent earcons (short audio cues)
  3) Visual feedback only (not applicable for tray app)
- **Why:** Spoken announcements add latency and can be annoying with repeated use. Earcons provide quick, non-intrusive feedback without contaminating STT or slowing down the flow.
- **Implementation:** 
  - Listening chime: MP3 file (`Assets/Sounds/tap-notification-357575.mp3`) plays on tap-to-talk
  - Thinking chime: Synthetic dual-tone (880Hz→1320Hz) plays 400ms after STT ends if model hasn't responded
  - Cancel chime: Optional (off by default)

## 2026-01-13 — NAudio for MP3 playback
- **Decision:** Use NAudio NuGet package for MP3 earcon playback.
- **Context:** Needed to support MP3 files for earcons; `System.Media.SoundPlayer` only supports WAV.
- **Options:**
  1) NAudio (full MP3 support, mature library)
  2) Convert MP3 to WAV at build time
  3) Windows Media Player COM interop
- **Why:** NAudio is well-maintained, adds minimal dependency (~500KB), supports volume control, and is the de facto standard for .NET audio.
- **Follow-ups:** None required.

## 2026-01-13 — Cleartext API keys for friends-only distribution
- **Decision:** Ship with cleartext API keys in config files for friends-only builds.
- **Context:** Discussed whether to encrypt config files with embedded key to obscure API keys.
- **Options:**
  1) Cleartext config files (current)
  2) Encrypted config with embedded decryption key
  3) User-provided keys (each friend creates their own Azure account)
  4) Backend proxy with user authentication
- **Why:** Encrypted config with embedded key provides no real security (.NET binaries are trivially decompilable). For trusted friends, cleartext is simpler and honest. Real security requires architecture change (backend proxy + user auth).
- **Mitigations:** 
  - Set Azure spending limits
  - Rotate keys monthly
  - Explicitly label as "friends-only, don't share publicly"
- **Follow-ups:** See `product/backlog/future-secure-azure-architecture.md` for production-ready solution.
