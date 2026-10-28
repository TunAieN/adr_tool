# Android Multi-Device V1.6.2.2 — TikTok first-launch onboarding

Builds upon V1.6.2.1. This update targets **only the first TikTok launch after a successful, explicitly requested `pm clear` on an approved device/account switch**.

## New steps, in order
1. Existing: handle the Android notification permission popup with Don't allow (only when the TikTok notification prompt is confirmed).
2. TikTok's Terms and Policies -> **Agree and continue** (TikTok resource-id `com.ss.android.ugc.trill:id/f19`).
3. Tell us more about yourself -> **first gender button** as specified by the operator (the supplied screenshot shows **Man** first), then **Continue**. The button is not inferred from user identity.
4. Interests onboarding -> **Skip** (look up by text first; the Compose XPath is a fallback).
5. Wait for Home/Profile/Login controls on **two consecutive reads**, then continue existing verification and Google login. TikTok login is not considered successful until the expected Profile username matches.

The manager observes each page before clicking. Bounded retries (up to two for transitions, 12 seconds apart) prevent click spamming. The entire first-launch sequence has a 120-second observation window, and failure stops the current device without re-clearing app data.

## Selectors in Config/selectors.json
- `firstLaunchTermsContinue`
- `firstLaunchGenderFirst`
- `firstLaunchGenderContinue`
- `firstLaunchInterestSkip`

These can be edited from the existing selectors configuration. The gender option is currently set to the *first* option, `Man`, matching the supplied instructions. Change the selector if a different choice is intended.

## Apply
1. Exit the Manager and back up the source tree plus `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Unzip the patch over the existing V1.6.2.1 project, preserving paths and replacing:
   - `AndroidMultiDeviceV1/Core/TikTokStateManager.cs`
   - `AndroidMultiDeviceV1/Config/selectors.json`
3. Run `BUILD_RUN.bat` on Windows with .NET 8 SDK and an Appium server.
4. On a cloud phone under your control, deliberately switch approved test accounts; observe the new `Thiết lập TikTok:` log stages.

**Warnings:** `pm clear` deletes TikTok local app data including local drafts. The Terms button indicates acceptance of TikTok's terms. This build automates that button only at the requested first-launch flow. It does not bypass CAPTCHA/2FA or Google verification. No comment or public post is sent.

**Validation:** JSON parsed; ZIP CRC checked; source updated and statically reviewed. **Not compiled with .NET or tested on Android here.**
