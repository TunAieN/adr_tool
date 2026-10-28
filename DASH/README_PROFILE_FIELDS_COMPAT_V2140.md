# V2.1.40 – Name Editor hw7 + return from Username editor

- New UI: Name entry is **mud[1]**; `mud[2]` is **username read-only for verification**, not a Name entry. Remove `mud[2]` from Name open candidates.
- New UI Name text box is `com.ss.android.ugc.trill:id/hw7`; keep `lfn` and `hvc` for other app versions.
- Bio may also use `hw7`; before setting Name validate exact Name/Edit name/Display name/Nickname page heading, excluding Username/Edit username/Edit profile.
- If profile-update starts inside a Username sub-editor, issue exactly one Back, verify Edit profile and expected `mud[2]`, then proceed. Preflight itself only READS mud[2] and does NOT click on it.
- Keep clear Close, save Save, Bio, avatar, Gallery, legacy owner-Profile route unchanged.

## Smoke test

1. ADB-connect new Phone 02; close Inspector sessions on same phone.
2. Build with `BUILD_RUN.bat`, open encrypted vault and test correct assigned account.
3. Logs: `PROFILE_UPDATE_HANDLE_RECHECK` → `PROFILE_UPDATE_EDIT_REUSED` → `Tên hiển thị: đã click ứng viên dòng Name 1` → `PROFILE_NAME_EDITOR_READY` → `PROFILE_NAME_INPUT_VERIFIED` → successful Save/verification if allowed by service.
4. If screen is unexpected, stop without sending Save, capture screenshot+XML of Name editor. Existing `SAVE_PENDING`/`COOLDOWN` ledgers still apply; do not reset account blindly.

Offline regression checks are not a Windows/.NET build and do not verify real-device success.
