ANDROID MULTI-DEVICE V1.5.1 - PROFILE + FOREGROUND HOTFIX

BASE: Android Multi-Device V1.5 DELETE ACCOUNT (includes V1.4 Excel + username, delete/unassign, Multi-Device, Google login).

FIX 1: Profile tab primary XPath:
  //android.widget.FrameLayout[@content-desc="Profile"]/android.widget.ImageView
Username button exact resource-id / XPath:
  com.ss.android.ugc.trill:id/t_k
  //android.widget.Button[@resource-id="com.ss.android.ugc.trill:id/t_k"]

The detector gets actual text/content-desc from that Button (and nested nodes), normalizes @handle, then compares the whole username with expected TikTok username from username|email|password. It will NOT infer identity from arbitrary on-screen text. If several handles or an unapproved handle appear, phone pauses for review.

FIX 2: Appium can report com.android.launcher3 while TikTok is starting. Wait for confirmed foreground (up to 25 seconds), and cross-check the visible UI hierarchy for exact TikTok package before labelling NOT TIKTOK. After one safe TikTok restart, wait for the foreground rather than using a fixed 1.8-second sleep. Never clear app data.

FILES IN PATCH:
  AndroidMultiDeviceV1/Core/TikTokStateManager.cs
  AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs
  AndroidMultiDeviceV1/Config/selectors.json

INSTALL:
1. Close the app. Back up both project files and %LOCALAPPDATA%\AndroidMultiDeviceV1 (account vault).
2. Extract PATCH zip onto your existing V1.5 source folder, preserving subfolders; overwrite only 3 files.
3. Run BUILD_RUN.bat. Keep Appium running and do NOT keep an Inspector session open on the same phone.
4. Test one authorised device/account; press Start. Look for 'Preflight: ExpectedAccount' or a precise reason for WAIT_REVIEW.

SAFETY:
- An exact username match must be observed on the device before state ACTIVE.
- Only explicitly approved accounts assigned to that device can enter the controlled logout path.
- Google verification, unknown/restricted accounts, or ambiguous identity stop.
- No automatic bypass, no app clear-data, no account-vault migration.

STATUS: Static code/JSON checked on Linux; C# Windows/phone runtime test NOT performed.
