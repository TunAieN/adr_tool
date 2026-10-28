# LIVE Search V2.1.7 — OCR confidence hotfix (Stage 2, read-only)

## Why
V2.1.6 incorrectly labelled two Tesseract processing variants of **one screenshot** as `OcrConfirmed` and treated those results as eligible for automated viewer ranking. This can produce false positives, including implausible numbers.

## Changes
- Viewer OCR candidates are now **OcrCandidate** even if image preprocessings agree. Never OcrConfirmed from one screenshot.
- Reject contaminated OCR text containing extra digits/letters instead of parsing prefixes.
- OCR the numeric badge using both a tight and a wider ROI plus contrast as a diagnostic third variant.
- If variants yield **different numbers**, label `OcrConflict`, set Viewers=null, do not rank.
- Print short raw OCR results per card (`tight/wide/contrast`) and icon pixel evidence.
- If every card is only a candidate/unknown, save screenshot+XML and stop; don't select a LIVE on uncertain data.
- Keep multi-device, account vault, Login, and profile features unchanged. No comments or automatic room joining.

## Apply
Copy `AndroidMultiDeviceV1/Core/LiveVisualOcr.cs`, `Core/LiveSearchEngine.cs`, and `LiveSearchDialog.cs` into V2.1.6; or use FULL. Build using `BUILD_RUN.bat`.

## Verify
1. Phone 01 -> Search LIVE `liên quân`.
2. Inspect logs with `OCR thô tight=..., wide=..., contrast=...`.
3. If a value is `OcrCandidate`, do not treat as a verified Viewer; compare with saved screenshot.
4. Save diagnostic files inside `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics`.

## Test status
Static checks and ZIP integrity only. No .NET SDK in current Linux environment. No access to user's current remote Android screenshot. Exact OCR reliability on TikTok LIVE is NOT guaranteed.
