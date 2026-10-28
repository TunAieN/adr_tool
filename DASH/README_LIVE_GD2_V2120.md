# LIVE GD2 V2.1.20 – LONG SWIPE / STOP-AT-8 FIX

## Changes
- Reconcile long swipes that have no overlapping old card: evaluate a tile in the newly exposed lower row using historical preview sketches, a two-capture Person/viewer OCR candidate and independently different viewer count. This is a conservative heuristic, **not a guaranteed room ID**. It may leave PENDING, and live video image changes can still cause misclassification.
- Retain recent preview appearance sketches across up to 48 tile observations to reduce recounting visually similar rooms.
- As soon as `CardsPerBatch` verified-new items are added, rank the group and return immediately, even if the remaining timeout is positive. If none qualify, return without continuing to scan.
- Preserve manual swipe 1–75% and existing timeouts.

## Verify
1. Backup the prior program and unzip all files.
2. Run BUILD_RUN.bat on Windows with .NET 8 SDK. This environment does not have .NET SDK or Android/Appium available, so build and device tests are **not verified** here.
3. Run a single LIVE search using 30%, 45%, 65% swipes. Confirm progress NEW/SEEN/PENDING. Confirm a log line `ĐỦ 8/8; DỪNG NGAY` exactly on the eighth accepted card, and no subsequent swipes.
4. For any suspected missed/duplicate room, export screenshots, XML and logs for further comparison. Viewer OCR is approximate, not an identity.

This module only searches, compares and suggests. It does not open LIVE rooms or post comments.
