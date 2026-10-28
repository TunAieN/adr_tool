ANDROID MULTI-DEVICE MANAGER — ACCOUNT LIFECYCLE V5.1
====================================================
Base: AccountV5.zip. Use PatchV51 ONLY on AccountV5, not older V3/V4.

CHANGELOG
---------
1. AUTO: never picks accounts with EverUsed or LastStart, irrespective of old toggle.
2. MANUAL: "Chạy tiếp TK đã dùng" for EXACTLY ONE selected account in its own
   device's pool; the account must have known cumulative running time, remaining
   time >0, no invalid/restricted/security-review state, and a matching owner.
   - YES: current TikTok account is already logged in; verify Profile username,
     resume LIVE without pm clear.
   - NO: explicitly consent to original Google/Preflight login route which MAY
     pm clear the current phone data if it must switch accounts.
   - CANCEL: no work.
3. Encrypted vault records accumulated VERIFIED session seconds. Stop/interrupt
   preserves elapsed time; manual reuse spends only the remaining budget.
4. "Xác nhận phút đã chạy": used V5 legacy data without reliable timing needs
   a one-time operator entry of verified cumulative minutes. Never presumes 0.
   Cannot overwrite an already tracked duration. Stops are required.
5. If a session expires, the original TikTok/GMS pm clear is used; after BOTH
   clear actions succeed, credential purge delay starts. Paused sessions are NOT
   auto-purged just because their LastEnd is old.
6. Per-device settings and live worker clocks are preserved. Changing the active
   limit via Apply Now uses the cumulative usage, not a brand-new clock.
7. LIVE Search, OCR, Phase5 and LoginWorkflow source are unchanged.

UPGRADE
-------
1. Stop ALL workers and exit tool.
2. Back up your source, plus:
   %LOCALAPPDATA%\AndroidMultiDeviceV1\settings.json
   %LOCALAPPDATA%\AndroidMultiDeviceV1\account-vault.json
3. Extract P51.zip into the SAME directory that contains DASH, replacing files.
   Or use AccV51.zip for a full standalone source copy.
4. From DASH, run BUILD_RUN.bat with Windows .NET 8 SDK installed.
5. Select Phone in TAI KHOAN; tick exactly one used account.
6. If account duration is unknown, first "Xác nhận phút đã chạy" after checking
   history. Then click "Chạy tiếp TK đã dùng", choose YES / NO as above.
7. For automatic fresh-only processing, uncheck all accounts and Start the phone.

SAFETY / IMPORTANT
------------------
- This is source implementation only, not a verified Windows build or Android E2E.
- New cumulative timing starts on VERIFIED session; it does not infer old unknown
  V5 history. A process crash during ACTIVE forces a new duration review.
- Source-level regression: python TEST_ACCOUNT_LIFECYCLE_V51.py and independent
  existing dashboard/FAST/Profile/Gallery guards.
- Legacy V3/V4/V5 source-text tests contain outdated assertions about purge
  and clock signatures; failing those outdated checks is not a C# build failure.
- No credentials are printed into logs by these additions.
- Direct LIVE Start WITHOUT selecting manual mode still rejects already-used
  accounts. This is intentional; choose the explicit manual resume button.
