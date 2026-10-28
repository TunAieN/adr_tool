# V2.1.64 – Appium performance diagnostics + LIVE monitor efficiency

Source: V2.1.63. This patch is **strictly additive** to the existing LIVE Search workflow.

## Changes

1. **Appium/ADB command timing.** `AppiumSession` can be configured with `AppiumPerformanceMonitor`. It emits `PERF_APPIUM` for commands lasting >=2,000 ms or for failed commands. Categories include SOURCE_XML, FIND_ELEMENT, TEXT_ENTRY, BACK, SCREENSHOT, MOBILE_GESTURE, etc. It does **not** log payloads, XPath values, element IDs, passwords or entered text. Every session writes a `perf-appium-*.json` summary on disposal with counts, average/max/total durations and failures. ADB commands also report `PERF_ADB` when >=2,000 ms or failed, using coarse categories DEVICES/CONNECT/SHELL/OTHER (never recording arguments or output).
2. **Room guard avoids one redundant full UI XML request.** After opening and closing the host profile, the already-verified returned LIVE snapshot is used to check Viewer and host status. This saves one network/UI round-trip during guard initialization and periodic audits. On uncertainty the guard still stops safely; no identity verification has been disabled.
3. **Per-room clock.** `LiveRoomFlowGuard` now uses the existing `LiveRoomTimer` instead of a duplicate Stopwatch implementation. Confirmed new hosts get a fresh clock; the same host does not reset. Periodic check time is anchored to the 60s room-clock boundaries, not to the conclusion of an already slow check. Actual checks may still start late while an Appium command is in progress because WebDriver operations on one device are sequential.
4. **All existing functionality preserved.** `LiveCommentOneShot.cs`, `LiveCommentAutoSender.cs`, `LiveSearchEngine.cs`, and `LiveManagerSettings.cs` are byte-for-byte unchanged compared with V2.1.63. No edit to comment scheduling or sending behavior. The separate monitor control is still present.

## Important limits

- An existing Phase5 duration timer remains separate. This patch **does not** reset the automatic-comment timer upon room change; the room guard stops on an unexpected/new room instead. This protects against applying previously authorized actions to another room without a new validation.
- Slow ADB/remote phone connectivity can still block a `SourceAsync()` call for as much as its existing 35 second ceiling. This patch instruments it rather than globally dropping timeouts and causing regressions.
- A mute/block restriction is treated as an instruction to stop interacting with the room, not an invitation to rotate rooms and try again.
- The patch has been statically checked and ZIP-tested; Windows .NET SDK/Cloud Phone runtime build has NOT been run here. Run `BUILD_CHECK_ONLY_V2161.bat` (if included) or `dotnet build` on Windows first.

## Expected log

    PERF_APPIUM: action=SOURCE_XML; ms=12600; ok=True
    PERF_APPIUM: action=TEXT_ENTRY; ms=48000; ok=True
    ROOM_FLOW_PERIODIC_CHECK_START: elapsed=60s; host=@...
    ROOM_FLOW_PERIODIC_CHECK_RESULT: host=@...; viewer=...
    PERF_APPIUM_SUMMARY_SAVED: ...\perf-appium-YYYYMMDD-HHMMSS-Phone_02.json; groups=...

The above values are illustrative, not measured performance results.
