# Acceptance checks — V2.1.8

1. On the provided 352×624 screenshot, the four red LIVE labels are individually detected and the numeric crops correspond to 4.8K+, 5.6K+, 2.5K+, 3.5K+.
2. Third card must NEVER be emitted as 22,500 on this screenshot; if OCR cannot parse the crop, it must be unknown.
3. No visible red LIVE badge => `BadgeNotFound`, no numeric guess.
4. Output from a single screenshot is `OcrCandidate`, not `OcrConfirmed`.
5. No room is opened or comment posted; UI debug code remains read-only.
6. Existing global account/login settings are unaffected.
7. Run BUILD_RUN.bat on Windows and test one Phone 01 session; compare all four viewers against screenshot before accepting.
