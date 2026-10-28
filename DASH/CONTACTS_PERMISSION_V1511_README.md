# V1.5.11 — TikTok contacts/accounts permission (Don't allow)

Source base: ANDROID_MULTI_DEVICE_V1_GOOGLE_CHOOSER_V1510_FULL (V1.5.10).

## Changes
- Config/selectors.json: prefer exact Android 16 ID `permission_deny_and_dont_ask_again_button`, keep older `permission_deny_button` fallback.
- Core/LoginWorkflow.cs: wait up to 12 seconds for the optional TikTok contacts/accounts permission after Google login; click only if the displayed text contains TikTok plus contacts (or danh bạ); verify the button disappears. Do not click unrelated permissions.
- Core/TikTokStateManager.cs: before Profile verification, check once more for a late popup, including on retry/resume.

## Install
1. Close Manager; back up project directory and account vault (%%LOCALAPPDATA%%\AndroidMultiDeviceV1).
2. Extract PATCH ZIP at the root of your V1.5.10 project, keeping paths and overwriting the three files.
3. Build using BUILD_RUN.bat. Preserve account-vault.json and settings.json.
4. For Phone 01, start with a permitted test account; watch the logs for `Android contacts permission: đã nhấn Don't allow` followed by username verification.

## Safety
- This does not grant contacts/account access; clicking this particular button may suppress future requests for this permission.
- It does not prove login success. Profile username matching is still required.
- No compilation on Windows / Appium integration test was performed in this environment.
