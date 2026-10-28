# V2.1.56 — Phase 5 one-shot: wait for Send after entering text

## Root cause (2026-10-24 00:13)
- The LIVE comment composer opened with `h4s` and a visible EditText.
- `vxc=False` BEFORE any text was entered, so V2.1.55 exited as INPUT_UI_NOT_READY, sendClicks=0.
- Some UIs only display/enable Send after a non-empty draft is entered. Pre-input absence of `vxc` is not evidence of a failed input editor.

## Patch
- Require a visible EditText (not Send) before typing.
- Type exactly the operator-selected draft once.
- Read and verify EditText text and the visible/usable `vxc` in up to three bounded XML polls (500 ms between reads).
- Press `vxc` only once, and only after the text is exact and the Send icon is visible and enabled. No retries of the Send tap, no scheduled sending.
- If `vxc` still cannot be resolved, do not guess a button. Save `entered-01.xml`...`entered-03.xml` and `send-after-text-missing.png` in phase5-one-shot diagnostics.
- Reuse the original controls and room validation. Other phases, settings and vault logic untouched.

## New log signals
- `PHASE5 PRE_INPUT_READY: ... sendCheck=DEFERRED_UNTIL_TEXT`
- `PHASE5 COMMENT_TEXT_ENTRY_REQUESTED`
- `PHASE5 AFTER_TEXT_CHECK x/3: textMatch=True; vxcPresent=True; vxcReady=True`
- `PHASE5 SEND_TARGET_READY: vxc`
- `PHASE5 COMMENT_SEND_REQUESTED: ... delivery=UNVERIFIED`

## Build and real-device testing
Extract the ZIP into a NEW directory and run `BUILD_RUN.bat` on Windows with .NET 8 installed. This environment does not have dotnet; no Windows compilation or Android test was run. Local source assertions and ZIP CRC checks completed.
