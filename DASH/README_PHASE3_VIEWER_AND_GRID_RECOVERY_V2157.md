# V2.1.57 — PHASE3 Viewer and Search-grid recovery

## Fixed
- Kept the user-supplied LIVE viewer XPath exactly: `//android.widget.TextView[@resource-id="com.ss.android.ugc.trill:id/tv_online_audience_num"]`.
- After a candidate opens, allow up to five bounded reads with a few seconds for LIVE UI to settle; if UI indicates a LIVE, perform at most one direct Appium XPath + element text query if XML reading is empty.
- Detect a card that navigated away from results but is **not** a confirmed LIVE (`LIVE_SCREEN_UNVERIFIED`), rather than marking it accepted.
- Save `phase3/attempt-NN/viewer-diagnostic.json`, `live-entry.xml` and screenshot when count is unverified.
- After rejecting, poll for **selected LIVE tab plus GridView**, instead of assuming one Back + 800 ms is sufficient. Perform an additional Back only when a LIVE control is still visible.
- Return `LIVE_REJECTED` and proceed to the next ranked card only after verified LIVE Search grid recovery. Otherwise stop at `EXIT_UNVERIFIED` without blind tapping.

## Test
1. Unzip into a new folder and run `BUILD_RUN.bat` on Windows (not compiled in this environment).
2. Run LIVE Search on Phone 02.
3. Expected `PHASE3 VIEWER_ATTEMPT N/5` and possibly `PHASE3 VIEWER_DIRECT_XPATH`.
4. If no Viewer, check `PHASE3 RETURN_GRID_WAIT` and, if successful, `PHASE2 NEXT_CANDIDATE`.
5. If the workflow still stops, send `phase3/attempt-NN/live-entry.png`, `live-entry.xml`, `viewer-diagnostic.json`, and `after-exit.xml` (remove sensitive identifiers as necessary).

Note: A vanished search grid is not proof of an actual TikTok LIVE room. The code never invents viewer counts. Existing Phase1/Phase2, login, profile and Phase5 flows were not intentionally changed.
