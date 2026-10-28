# V2.1.59 – Phase5 LIVE composer state fix

## Root cause
`LiveCommentOneShot.ExecuteAsync()` used `HasVisibleEditText` across the whole TikTok XML. A non-comment EditText caused `PHASE5 COMMENT_OPEN_ALREADY_ACTIVE`, so the intended LIVE comment row (`h4s` / `h42` / Type...) was never tapped and text entry targeted a broad `//android.widget.EditText` selector.

## Changes
- Resolve the LIVE comment opener and tap it once when it is visible, regardless of unrelated EditText elements.
- Consider comment composer already open only if a **focused** non-search EditText is identified near the lower overlay and the Type opener is absent.
- Re-read XML after tapping the opener, detect a distinct or newly focused comment editor; reject a no-change result.
- Bind input to the verified EditText by resource-id and current bounds, not a broad all-EditText XPath.
- Verify the entered text inside the resolved composer before looking for an enabled `vzb` (or legacy `vxc`) Send element.
- Preserve the existing one-shot, operator-triggered Send action. Never guess an unknown Send button or retry after tapping it.
- Save `before.xml`, `opened-*.xml`, `entered-*.xml`, and `phase5-stopped.png` if the UI cannot be confirmed; log exact editor resource-id/bounds and `PHASE5 COMPOSER_CHECK`.

## Expected log
```
PHASE5 COMPOSER_PRECHECK: active=False; typeTarget=READY
PHASE5 COMMENT_OPEN_RESOLVE: found=True; method=h4s
PHASE5 COMMENT_OPEN_TAP_REQUESTED: method=h4s; oneTapOnly=true
PHASE5 COMPOSER_CHECK 1/3: ready=True; ... transitioned=True
PHASE5 INPUT_UI_CHECK: ... composerVerified=True
PHASE5 COMMENT_TEXT_ENTRY_REQUESTED: ...
PHASE5 AFTER_TEXT_CHECK 1/3: textMatch=True; ...
PHASE5 SEND_TARGET_READY: selector=vzb
PHASE5 COMMENT_SEND_REQUESTED: sendClicks=1; delivery=UNVERIFIED
```

If the editor does not open, the operation stops with `COMMENT_COMPOSER_NOT_CONFIRMED`. A Send tap request is not proof that a comment was delivered.

## Build and verification
On Windows, extract the ZIP to a new directory and run `BUILD_RUN.bat`; connect only the intended Appium session. **This patch has not been compiled or tested on Android in this environment.**
