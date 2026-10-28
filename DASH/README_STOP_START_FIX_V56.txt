ACCOUNT V5.6 - FIX MANUAL START AFTER STOP
==========================================
BASE: AccV55.zip (V5.5 FULL), NOT V4/V5.1.
PATCH: replace DASH/AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs.

Problem:
- Operator presses Stop, then ticks a fresh account and presses Start.
- GUI uses SwitchToSelectedAccountAsync -> StartUnderLock(fromSwitch:true).
- A stale Stop flag was interpreted as cancelling AUTOMATIC rotation.

Fix:
- Explicit selection now passes operatorInitiated=true, separate from auto handoff.
- Old Stop is cleared ONLY after the new worker + Appium reservation are created.
- If operator presses Stop while a manual account switch waits for the old
  worker to release its Appium session, a per-phone Stop version cancels the
  pending Start instead of clearing the NEW Stop request.
- Auto expiry handoff still respects Stop; Stop/Start of other devices unaffected.
- Invalid/wrong-device/unavailable selected accounts still rejected.
- No changes in LIVE, CAPTCHA, OCR, Phase5, Google Login, pm clear, vault.

Install:
1. Stop workers, exit application; back up source, settings.json,
   account-vault.json.
2. Unzip PATCH into the V5.5 source root containing DASH, overwrite.
3. Run DASH/BUILD_RUN.bat from the usual project location.
4. Verify with a test account: Stop Phone02; wait for Appium session release;
   select a fresh account of Phone02; Start; expect
   MANUAL_ACCOUNT_START_REQUESTED and MANUAL_ACCOUNT_START_ACCEPTED.
5. If Stop is clicked again during manual handoff expect
   MANUAL_ACCOUNT_START_CANCELLED_BY_NEW_STOP and NO new worker.

Tests: python DASH/TEST_MANUAL_START_AFTER_STOP_V56.py
Static tests do not replace a Windows .NET 8 build or XCloudPhone run.
