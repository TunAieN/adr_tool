# V2.1.62 — Independent LIVE monitor: 60-second room revalidation

Derived from V2.1.61; changes are intentionally restricted to `AndroidMultiDeviceV1/Core/LiveRoomLifecycleMonitor.cs` and this README.

**What changes in opt-in `Theo dõi / Duyệt LIVE` mode**

- Checks UI frequently (configured poll interval clamped to 400–1000 ms) to improve chance of reading transient mute/blocked toasts if exposed in XML. Recognizes notice text and accessibility `content-desc` near top of screen; does not treat comments farther down as a TikTok warning.
- Reopens the host profile at 60-second intervals and compares username, while re-reading `tv_online_audience_num` to evaluate viewer eligibility. Two consistent host reads required to prove a change; never resets time based solely on viewer fluctuations or a reload.
- Viewer outside configured range is checked twice before returning to room-validation flow.
- When TikTok temporarily shows neither host nor viewer during a natural transition, waits up to 6 UI snapshots for the next LIVE to render instead of immediately classifying it as a non-LIVE page.
- Rechecks host when the per-room clock expires, preventing an expired timer for the previous room from triggering a swipe out of a newly auto-changed room.
- Existing independent per-room clock and 10-consecutive-unqualified-room search fallback remain.

**Important limits**

- This is an independent `Theo dõi / Duyệt LIVE` mode, not an observer that runs during Phase 5. The original Phase 5 source files are preserved. A periodic monitor that is not active cannot detect a room switch in the auto-comment mode. Do not run competing Appium sessions on the same device.
- Toasts not exposed in Appium XML may still be missed. A screenshot taken exactly during the notification is needed to analyze those cases.
- A restriction is logged and interactions stop in that room; no automation to bypass the restriction or continue mass comments was added.
- Host username alone cannot prove a new LIVE session when the same host restarts their stream.

**Test**

1. Build from a new folder using `BUILD_CHECK_ONLY_V2161.bat` or `BUILD_RUN.bat`.
2. Open a LIVE manually, select `Theo dõi / Duyệt LIVE`, watch for `ROOM_PERIODIC_CHECK_START` and `ROOM_PERIODIC_CHECK_RESULT` after ~60s.
3. Change to another LIVE manually while monitor is active, and verify `ROOM_AUTO_CHANGE_CONFIRMED` followed by a new `ROOM_TIMER_RESET` in the outer cycle.
4. Recheck no existing features disappeared; only one C# file is changed.

No Windows .NET SDK or XCloudPhone integration test available in this environment; validate build/runtime on user's Windows machine.
