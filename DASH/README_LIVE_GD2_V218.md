# TikTok LIVE Search V2.1.8 — viewer chip crop fix

This patch targets the **read-only LIVE Search screenshot OCR** in V2.1.7.

## Verified issue from supplied diagnostic

On the attached 352 × 624 image, the LIVE card viewer badges read:

- Card 1: `4.8K+` (approx. 4,800)
- Card 2: `5.6K+` (approx. 5,600)
- Card 3: `2.5K+` (approx. 2,500), **not `22.5K`**
- Card 4: `3.5K+` (approx. 3,500)

`UiAutomator` exposes card bounds, but not the badge text. V2.1.7 cropped from fixed tile offsets; for a card with a wider red LIVE label that included part of the white person glyph. OCR could recognize that shape as an extra leading `2`.

## Change

- Find the red LIVE chip on each screenshot tile; determine the red label right edge dynamically.
- Crop *the numeric portion only* after the icon. If the chip cannot be detected, report `BadgeNotFound` rather than guessing.
- Use page segmentation 7 and 8 plus contrast processing against that same region.
- Do not treat multiple preprocessings of one screenshot as independent confirmation. Outputs remain `OcrCandidate` until corroborated by an independent source or explicit operator verification.
- Align `iconPixels` to the newly found badge rather than a tile-relative constant.

## Install

Close the tool, back up V2.1.7 and replace only:

`AndroidMultiDeviceV1/Core/LiveVisualOcr.cs`

Run `BUILD_RUN.bat` on the user's Windows machine (requires .NET 8 and Tesseract English language files). The patch does not touch account vault, login flow or live commenting actions.

## Validation status

Four crop regions were checked visually on the supplied image, with one local batch Tesseract diagnostic on enlarged regions. **No .NET SDK/Windows compilation or XCloudPhone end-to-end test was possible in this environment.** OCR remains probabilistic and may fail on other TikTok layouts, different device sizes or animated badge backgrounds; the reader must fail closed.
