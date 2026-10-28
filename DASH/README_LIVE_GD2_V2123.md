# V2.1.23 - Scan exactly 8 observed LIVE cards (4+2+2)

## Change
- TikTok Search with a 2-column x 2-row grid and `CardsPerBatch=8`: capture four slots in viewport 1; after the first scroll record ONLY slots 3 and 4 (bottom row); after the second scroll record ONLY slots 3 and 4 again. Hard stop after these 8 observations regardless of remaining time and whether OCR succeeded.
- Two upper slots of subsequent viewports are treated as overlap/SEEN for the sampling window, not added to the batch. This reflects the concrete screenshots supplied; it is not a claim that their room identity has been verified.
- Each observed card consumes one sample slot even if its Viewer is unknown. Such entries retain OCR evidence, and ranking uses only eligible readable Viewer counts.
- Stop does not auto-enter any LIVE room or comment. It offers a candidate if one satisfies the configured thresholds.
- Each viewport still saves png/xml/json in the diagnostics folder. State NEW in JSON denotes sampled/new-in-window, not globally verified unique; `SAMPLED` is logged for clarity.
- Non-2x2 layouts retain the previous conservative tracker; a 4+2+2 assumption is not applied when four grid slots are not present.

## Expected logs
- Viewport 1: SAMPLE 1..4, total=4/8
- Viewport 2: OVERLAP=2, SAMPLE 5..6, total=6/8
- Viewport 3: OVERLAP=2, SAMPLE 7..8, total=8/8, STOP immediately

## Validation
- Source-level checks and ZIP integrity only. This environment does not have dotnet, Appium, or an Android device; Windows compilation and end-to-end behavior have not been verified.
- Card sampling does not guarantee all eight are distinct room IDs if the UI did not scroll as expected; verify images and log if anomalies recur.
