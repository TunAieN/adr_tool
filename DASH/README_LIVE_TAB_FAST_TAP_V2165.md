# V2.1.65 — Correct high-latency LIVE Search tab selection (minimal patch)

Source: V2.1.64.

## Confirmed reason for patch

At 12:17:20 the original tool had fresh XML confirming the LIVE category tab at (115,85), yet it issued FindElement (11.617s), Element displayed attribute (20.212s), and ElementClick (25.009s, timeout). These are redundant RPCs on a slow UiAutomator2/remote Android connection.

## Code change

Only `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs`: in `SelectAndConfirmLiveTabAsync`, use the already verified center of the LIVE Search header tab for a **single** `TapUiPointAsync` instead of `ClickVisibleAnyAsync(LiveResultsTab)` followed by a fallback.

All preconditions for `FindLiveResultsTab` remain in force, including real Search header, visible LIVE FrameLayout/TextView next to other category tabs, strip clipping checks, and screen-top bounds. The existing `WaitForAsync(IsLiveSearchGridReady,25)` verification of `selected=true` and GridView remains mandatory. A failed or timed-out tap stops safely. **No automatic retry after an indeterminate timeout**, avoiding an unintended second tap.

## Important limits

This removes redundant commands but cannot repair a stalled UiAutomator2 server, remote ADB latency or an overloaded rented phone. Earlier SourceAsync and Back took 10-13 seconds; record and compare those independently. Do not arbitrarily increase all timeouts to 60s.

No changes to account management, login, OCR/card count, candidate selection, Phase3, moderation status, LiveCommentOneShot.cs, LiveCommentAutoSender.cs, or room watchdog. No feature is disabled.

## Expected logs

```
LIVE_SEARCH_TAB_FOUND: method=FrameLayout/LIVE/TextView; selected=False; x=115; y=85
LIVE_SEARCH_TAB_TAP_REQUESTED: method=VerifiedTabBoundsFast; x=115; y=85; skip=FindElement,Displayed,ElementClick; chờ selected=true + GridView.
PERF_APPIUM: action=MOBILE_GESTURE; ms=...; ok=True
LIVE_SEARCH_TAB_SELECTED: selected=true; grid=True; category=LIVE.
```

## Verification

Run `BUILD_CHECK_ONLY_V2161.bat` (if present) on Windows and then `BUILD_RUN.bat`. Test only the LIVE Search step first. A Windows .NET 8 build and an XCloudPhone live test were not available in the packaging environment. Source static checks and ZIP integrity checks performed.
