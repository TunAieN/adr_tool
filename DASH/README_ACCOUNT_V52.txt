ANDROID MULTI-DEVICE MANAGER — V5.2 START LIVE ONE BUTTON
=======================================================
BASE: AccV51.zip. Run BUILD_RUN.bat from DASH after extracting FULL or PATCH.

What changes:
- Start LIVE checks the REAL signed-in TikTok owner via verified Profile/Edit profile.
- If the signed-in account is in this phone's pool and has a known remaining time,
  Start LIVE continues it and preserves cumulative elapsed run time. No extra button.
- If it is a used legacy account with missing trustworthy run history, Start LIVE
  creates a NEW measured interval after successful owner verification. The older
  period remains explicitly UNKNOWN; the UI says '(mốc mới)' / 'từ mốc mới'.
  It is NOT claimed to be zero historic usage. The new configured session limit
  applies to this newly observed interval.
- If the signed-in account is known expired, Start LIVE hands over to the
  ORIGINAL normal Start flow. Only FRESH (never-used) accounts can be picked
  by the automatic queue. The existing reconciler owns pm clear after proof.
- Suspended, invalid, restricted, unknown/ambiguous usernames: fail safely and
  do NOT pm clear on unidentified accounts. Idle standalone LIVE without a
  local account pool remains timerless (unchanged behavior).
- Start LIVE from the device controls AND the selected-device menu follows
  this same logic. Account pool no longer asks the user to enter minutes.
- Cumulative time continues across normal Stop/Start for VERIFIED sessions;
  each device retains its independent lifecycle policy from V4.

Relevant log keys:
  LIVE_OWNER_VERIFIED; mode=VERIFIED_CURRENT_OWNER
  LIVE_ACCOUNT_SESSION_STARTED; mode=CURRENT_OWNER_RESUME / CURRENT_OWNER_FRESH
  ACCOUNT_LEGACY_HISTORY_UNKNOWN
  LIVE_OWNER_EXPIRED_AUTO_FRESH_HANDOFF
  LIVE_ACCOUNT_ROTATION_HANDOFF

Upgrade: STOP all workers, exit program, back up
  %LOCALAPPDATA%\AndroidMultiDeviceV1\settings.json
  %LOCALAPPDATA%\AndroidMultiDeviceV1\account-vault.json
  ... then unpack and run BUILD_RUN.bat.

Important: Build/runtime on Windows/.NET 8 and rented Android phones was NOT
available in the packaging environment. Validate with ONE test phone before
rolling out. Unknown historical time CANNOT be reconstructed accurately from
an EverUsed flag; V5.2 does not make up any prior duration.
