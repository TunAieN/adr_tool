# LIVE GD2 V2.1.21 — Long swipe candidate fix

- Fix: when scrolling 65%, if no previous tile remains visible, new cards are no longer rejected only for being in the upper row.
- Compare each candidate against recent preview history for the **same column**; require stable Person OCR, a distinct screenshot sketch and no matching previously observed viewer count. Viewer count alone is NOT a room identity.
- Conservative protection: when evidence is insufficient, keep PENDING. A changing live preview can still cause ambiguity and a screenshot/room-ID inspection is needed for guaranteed room uniqueness.
- Adds `longSwipeCandidates` and `longSwipeAccepted` to tracking log. `MeasuredShiftPixels=0` is not proof of no movement.
- Retains immediate STOP at 8 confirmed unique cards, and configurable swipe range 1–75%, timeout 240/600s.
- All preexisting project features are unchanged.

## Build
Windows / .NET 8 SDK: run `BUILD_RUN.bat`.

## Testing
Run the same keyword at 30%, 45%, and 65%; check NEW, SEEN, PENDING, longSwipeCandidates/Accepted and verify actual rooms manually. Especially check false positives from animated live previews. If still pending, collect screenshots before/after swipe and the UI hierarchy/XML.

## Validation
ZIP structural integrity and static source checks performed. .NET SDK/Appium/Android device are not available in the packaging environment; no compile or on-device claims are made.
