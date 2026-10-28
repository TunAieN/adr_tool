# V5.9 — Manual UI Retry + Verified LIVE after ADB rebind + Google Picker

Apply over **AccV58** source only. Back up `%LOCALAPPDATA%\AndroidMultiDeviceV1\account-vault.json` and `settings.json` first. Stop all workers and close the app, then replace the four C# files in PATCH, run `BUILD_RUN.bat` on Windows with .NET 8 SDK.

### Fixed paths

- **Start LIVE on an already-signed-in account**: if the TikTok username from **Edit profile** matches exactly one account in the correct phone's vault, a past `NEEDS_REVIEW` due to a recognized **technical UI/transport** failure no longer blocks it. Stale `ACTIVE`/`RESERVED` from a previous broken ADB session is settled conservatively only after strict username proof, retaining recorded elapsed time. These actions do not clear TikTok/GMS data.
- **Stop/choose account/Start**: direct selection of an account with `RETRY_PENDING` or `NEEDS_REVIEW` caused by a documented *technical UI* failure may retry Preflight for that exact account. The flag does not enter AUTO selection and does not turn EverUsed off. If the old attempt had an unmeasured duration, time is counted from a new verified observation and historical uncertainty is disclosed.
- **Google Choose an account**: keeping Google context checks, recognize the authenticated list even when its `Choose an account` heading scrolls out of view; click only an explicitly labelled `Add another account`/`Use another account` row, with a safe XML-bounds fallback; count scroll attempts truthfully and verify the subsequent email form. Never blindly tap or select a cached identity by row index.
- **Change ADB**: with old workers absent, a successful rebind reconciles leftover reservations on the moved account pool while preserving DeviceId, credentials, and verified timer history.

### Must remain blocked

`INVALID`, `RESTRICTED`, `DISABLED`, `ACCOUNT_CHECKPOINT`, explicit 2SV/security challenges, *unverified* identities, and accounts that have exhausted their verified session time are not overridden. AUTO remains `AVAILABLE` + never used only. Operator Stop blocks automatic rotation. Any confirmed CAPTCHA requiring real user verification still needs lawful manual action.

### New log markers

`EXPLICIT_TECH_UI_RETRY_ACCEPTED`, `LIVE_OWNER_TECH_REVIEW_RESUMED`, `LIVE_OWNER_ORPHANED_SESSION_RECONCILED`, `LIVE_OWNER_ELIGIBILITY_REJECTED`, `ADB_REBIND_STALE_SESSION_RECONCILED`, `GOOGLE_ACCOUNT_PICKER_TARGET_CLICKED` (method and real number of scrolls), `GOOGLE_ACCOUNT_PICKER_UI_UNCONFIRMED`.

### Validation

Run `python TEST_RETRY_REBIND_V59.py`. Source regression checks and ZIP integrity were run. **Windows .NET 8 build and XCloudPhone device E2E were not possible in this environment.** Test on one device before deploying to eight phones.
