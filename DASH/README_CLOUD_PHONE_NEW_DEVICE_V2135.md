# V2.1.35 — XCloudPhone new-device onboarding / Appium session diagnostic fix

## Root cause
- Previous configuration knows only `f19` for TikTok Terms. The newly attached device shows `f0x`.
- Previous code only entered first-run onboarding recovery after deliberately clearing TikTok data (`pm clear`); brand-new cloud phones can display Terms without that path.
- Appium `404 invalid session id` is separate from a missing Terms button; this patch adds an explicit log but cannot repair an externally terminated Appium session in-place.

## Patch
- Add exact selector `com.ss.android.ugc.trill:id/f0x` (ID + XPath), retain old `f19` and text fallback.
- After launching TikTok, observe the onboarding states *during* the 120-second UI wait (not after the old 90-second wait); also probe immediately if TikTok was already foreground. If the active TikTok-owned hierarchy shows Terms/Gender/Interests, enter onboarding recovery before trying Profile. Automatically handling the Terms button is limited to a positively recognized TikTok Terms screen; this button constitutes agreement to TikTok's terms. Only run with a company-authorized account and device.
- Log `NEW_DEVICE_ONBOARDING_DETECTED`, `NEW_DEVICE_ONBOARDING_COMPLETE` or `NEW_DEVICE_ONBOARDING_PENDING`; max observation 120 seconds.
- On failure, mark selected account for review without removing it from vault or repeating `pm clear`.
- If Appium returns 404 invalid session id, log `APPIUM_SESSION_LOST` to distinguish from missing UI controls.

## Testing
1. Close Appium Inspector and stop other device workers using the same ADB device.
2. Confirm `adb devices -l` shows `adb.xcloudphone.net:25153 device` and Appium server runs locally.
3. Open the manager vault and run **Phone 02 > TEST selected account**, not Phone 01.
4. Watch onboarding logs. Once Home/Profile appears, the existing account-identification workflow continues.
5. If 404 happens again, send the **Appium Server** last 30 lines, check the session was not deleted by Inspector; avoid competing sessions on the same device.

Note: Not compiled on Windows in this environment (dotnet SDK not installed). No physical-cloud device run performed here.
