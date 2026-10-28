# V2.1.58 — PHASE5: new LIVE send button vzb (one-shot test)

## Incident (2026-10-24)
- LIVE accepted; comment entry `h4s` was found.
- Exactly one draft `helo` was entered and verified in a visible EditText.
- `vxcPresent=False` on all three UI reads, so the existing one-shot operation stopped at `COMMENT_SEND_NOT_FOUND_AFTER_TEXT` without tapping.
- The device's current Send button was reported as:
  `//android.widget.ImageView[@resource-id="com.ss.android.ugc.trill:id/vzb"]`.

## Source changes
- `AndroidMultiDeviceV1/Core/LiveCommentOneShot.cs`: use strict `ImageView` resource-id checks for `vzb` first, then legacy `vxc`.
- Only use fresh, visible, enabled bounds captured after the entered text matches the selected draft.
- Report both IDs and `sendSelector` in post-input logs; do not tap any unknown Send candidate.
- Maintain a single tap in the user-triggered one-shot test, never retry Send, and label delivery `UNVERIFIED` pending external confirmation.
- `AndroidMultiDeviceV1/Core/LiveCommentUiProbe.cs`: diagnostics report `vzb` alongside `vxc`.
- No changes to automated repeated commenting, LIVE search, OCR, PHASE2, PHASE3, account vault or profile-update flows.

## Example expected signals
```
PHASE5 COMMENT_TEXT_VERIFY: MATCH
PHASE5 AFTER_TEXT_CHECK 1/3: textMatch=True; vzbPresent=True; vxcPresent=False; sendReady=True; sendSelector=vzb
PHASE5 SEND_TARGET_READY: selector=vzb; x=...; y=...
PHASE5 COMMENT_SEND_REQUESTED: ...; sendClicks=1; delivery=UNVERIFIED
```

If both selectors are absent/disabled, source XML and `send-after-text-missing.png` are saved in `diagnostics/phase5-one-shot-*`, without a guessed tap.

## Build/test limits
- Build on Windows with .NET 8: extract to a new directory, launch BUILD_RUN.bat.
- Only static checks and ZIP CRC verification were performed here: Windows/Android runtime testing was not available.
