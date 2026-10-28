# V2.1.47 – Preflight Profile bottom-tab safety / Edit diagnostic

## Why
V2.1.46 logged PROFILE_PAGE_CONFIRMED using `tlh`, then clicked it and timed out waiting for `mud[2]`. Logs don't prove that it opened LIVE, only that navigation wasn't verified. Its preflight Profile locator list also contained broad text/XPath fallbacks that can match unrelated elements.

## Change
- Only recognize the **bottom** Profile tab in accessibility XML by its screen bounds (bottom 24%); don't use a random matching text node to authorize navigation.
- When clicking, use only the specific native bottom Profile FrameLayout/ImageView or Button XPath. Keep all old selector alternatives in `selectors.json` for other legacy workflows.
- Do not mistake a feed/LIVE view containing known LIVE room controls for the owner's Profile/Edit page.
- After tapping `tlh`, refuse to read identity from Feed/LIVE; confirm the Edit profile state and `mud[2]` before trusting a username.
- If the Edit page can't be identified in 15s, save local screenshots and XML under `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\profile-preflight-*`.
- No changes to Google Login, account vault, Name/Bio, avatar or LIVE Manager.

## Expected logs
- `PROFILE_BOTTOM_TAB_TAP_REQUESTED` when an actual bottom tab is tapped
- `PROFILE_PAGE_CONFIRMED` only if owner-page evidence appears
- `PROFILE_EDIT_ENTRY_FOUND` then `PROFILE_EDIT_USERNAME_FOUND` on success
- `PROFILE_EDIT_WRONG_DESTINATION` or `PROFILE_EDIT_USERNAME_NOT_FOUND` plus `PROFILE_PREFLIGHT_DIAGNOSTIC_SAVED` on failure

**Offline patched, no Windows/.NET 8 or live Android test in this environment.**
