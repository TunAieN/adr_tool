# V2.1.36 — CloudPhone first-run feed tutorial / Profile preflight

## Evidence & root cause
- Previous patch V2.1.35 handles Terms, gender and interests screens.
- This particular new Cloud Phone then presents a full-screen dark tutorial reading **Swipe up for more**, even though TikTok's bottom Home/Profile tab may be present in the UI tree.
- The old detector marked `profileOpened=true` as soon as Appium reported a successful click on the Profile icon. A tutorial overlay can consume that click instead, so the tool falsely logged "Đã vào Profile" and waited ~40 seconds for a username that was not visible.
- This is a UI navigation/first-launch tutorial problem, **not** proof of banned, bad or mismatched account credentials.

## Source changes
- `Core/TikTokStateManager.cs`: detect the Home/For You/Profile feed context and accessible swipe-up tutorial text when available. On a visible tutorial, attempt **one** bounded swipe up. If the tutorial is not exposed to UI XML but a Profile tap leaves the Home/For You feed unchanged for 3 seconds, attempt the same bounded swipe **once** and retry Profile.
- Do not claim Profile is open based on a successful tap. Require an actual owner-Profile username element; otherwise distinguish a logged-out Profile/login screen. At most two Profile taps after observation; do not tap repeatedly.
- If navigation still cannot be verified, return `PROFILE_NAVIGATION_NOT_CONFIRMED`, preserving the account state for technical review.
- `Core/AdbAndAppium.cs`: one guarded `mobile: dragGesture` in the screen's feed body, not toolbar/system navigation.
- V2.1.35 Terms/Gender/Interests processing remains unchanged. **No local app data reset, no credential mutations, no automatic account guessing.**

## Expected key log events
```
NEW_DEVICE_ONBOARDING_COMPLETE: đã thấy thanh điều hướng TikTok...
Preflight UI: Đang tìm tab Profile ...
Preflight UI: PROFILE_TAP_REQUESTED: lần 1/2; đang xác minh chuyển trang...
Preflight UI: NEW_DEVICE_FEED_TUTORIAL_DETECTED: Swipe up for more visible in XML...
# or, if overlay is not in XML:
Preflight UI: NEW_DEVICE_FEED_NAVIGATION_BLOCKED: Profile tap did not navigate; feed still visible...
Preflight UI: PROFILE_TAP_REQUESTED: lần 2/2; đang xác minh chuyển trang...
# Either login is visible and the normal account login proceeds, or:
Preflight UI: PROFILE_PAGE_CONFIRMED: đã đọc được nút username của Profile.
# If none of the above:
PROFILE_NAVIGATION_NOT_CONFIRMED: ... tutorialSwipe=True
```

## Windows smoke-test procedure
1. Close Appium Inspector to avoid competing sessions; keep Appium Server open.
2. Connect the new CloudPhone via ADB and refresh until the Phone 02 serial shows `device`.
3. Run `BUILD_RUN.bat`, unlock the vault and run **TEST selected account on Phone 02**.
4. Observe the entire startup tutorial until Profile or login screen opens. Check screenshot on device.
5. If still stuck, supply sanitized source XML and screenshot from the moment the **Swipe up for more** tutorial is visible, plus the last 40 program logs. Do not send credentials.

## Validation status
- Source patched, static checks and archive integrity verified.
- Cannot compile Windows .NET 8 / Appium or run this user's CloudPhone from the current Linux environment; requires Windows build and real-device confirmation.
