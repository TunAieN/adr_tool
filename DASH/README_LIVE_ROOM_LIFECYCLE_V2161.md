# V2.1.61 — LIVE Room Lifecycle (additive, read-only browse mode)

## Origin

Derived directly from the user's V2.1.60 ZIP. Changes are intentionally confined to:

- `AndroidMultiDeviceV1/Core/LiveRoomLifecycleMonitor.cs` — **new** per-device room observation, viewer/host checks, verified transitions, per-room timer and consecutive unsuitable room count.
- `AndroidMultiDeviceV1/Core/AdbAndAppium.cs` — **added one method** `SwipeLiveRoomUpAsync` for ordinary LIVE viewing. Existing methods preserved.
- `AndroidMultiDeviceV1/LiveSearchDialog.cs` — **added one opt-in button**, `Theo dõi / Duyệt LIVE`. Existing `Tìm LIVE` and Phase 5 behaviors kept as-is.
- This README and separate build-only script.

**No changes to `LiveCommentOneShot.cs`, `LiveCommentAutoSender.cs`, account vault, session settings, phase2/phase3 OCR or Search engine.**

## How to use

1. Launch `BUILD_RUN.bat` as before. Open/connect a device, verify ADB Online.
2. Open an actual TikTok LIVE on that device manually (not the Search grid).
3. In the existing `Tìm LIVE` dialog, click **Theo dõi / Duyệt LIVE**.
4. The separate session observes LIVE, reads `tv_online_audience_num`, opens `k1s`/`k13` profile for `user_name` and `q2e`/`q16`, and closes the panel with Android Back.
5. If Viewer is below min or above configured max, ordinary read-only room browsing swipes to a **distinct, verified** next room. After 10 consecutive ineligible rooms, it invokes the existing keyword Search engine. An accepted room resets the consecutive ineligible counter.
6. Each accepted, verified *different host* starts a fresh `SessionDurationSeconds` clock at 0. A stale loading state, changing Viewer, or same host never resets the timer. When the timer elapses, read-only browsing may proceed to the next distinct room.
7. An unexpected room change while watching is checked by host identity twice before accepting a new room/timer.
8. Explicit mute or access restriction notices are recognized and logged; the monitor **stops**, captures diagnostics, and does not automate continued commenting after a room restriction.
9. `Dừng` cancels the active operation. Search and monitor buttons are mutually exclusive while a single Appium session owns the device, then return to normal.

## Monitoring / restrictions

This is intentionally a **separate opt-in navigation and observation mode**; it is NOT attached to, does not invoke, or modify automatic comment sending. Restriction-triggered hopping to resume mass comments was not implemented. The source includes a pure `LiveRoomUiReader.Parse(xml)` for other modules to *observe* a restriction without guessing from absent keyboard/Send.

If a LIVE host changes but the same username later hosts a fresh broadcast, identity alone cannot prove that a new Room ID exists. The monitor does **not** reset the clock on same-host UI reloads. When UI is ambiguous, it stops rather than guesses a host or clicks an unrelated video.

Detection of short-lived toasts is best-effort through XML; when a toast is not present in XML, screenshot + XML diagnostics are needed. The monitor can only observe when its dedicated mode is active; it does not run simultaneously with the original phase5 worker.

## Logging and diagnostics

`ROOM_MONITOR_START`, `ROOM_INVALID`, `ROOM_BROWSE_SWIPE_REQUESTED`, `ROOM_CHANGE_CONFIRMED`, `ROOM_AUTO_CHANGE_CONFIRMED`, `ROOM_TIMER_RESET`, `ROOM_SESSION_TIME_REACHED`, `ROOM_INVALID_LIMIT_REACHED`, `ROOM_KEYWORD_SEARCH_ACCEPTED`, `ROOM_RESTRICTION_CONFIRMED`.

Path: `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\live-lifecycle-YYYYMMDD-HHMMSS\`

## QA status

Source preservation/hash and ZIP integrity verified locally. No Windows .NET SDK or live XCloudPhone are available in the packaging environment; actual build and Appium runtime must be tested on Windows. Use the included `BUILD_CHECK_ONLY_V2161.bat` to compile without launching the application. If this fails, send the full compiler output; do not overwrite a known-good version.
