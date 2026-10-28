"""Source-contract regressions for account TEST preflight performance patch.
Run in project root: python TEST_FAST_PREFLIGHT.py
This is NOT a C# compile/Android functional test.
"""
from pathlib import Path
import re
R=Path(__file__).parent/'AndroidMultiDeviceV1'/'Core'
source={name:(R/name).read_text(encoding='utf-8-sig') for name in
        ['DeviceOrchestrator.cs','FastPreflightIdleScope.cs','TikTokStateManager.cs','LoginWorkflow.cs','LiveSearchIdleSafety.cs','LiveVisualOcr.cs']}
orchestrator=source['DeviceOrchestrator.cs']
idlescope=source['FastPreflightIdleScope.cs']
state=source['TikTokStateManager.cs']
login=source['LoginWorkflow.cs']
assert 'FastPreflightIdleScope.EnableAsync(' in orchestrator
assert 'await fastPreflight.RestoreAsync();' in orchestrator
assert orchestrator.index('await fastPreflight.RestoreAsync();') < orchestrator.index('await LiveVerifiedSessionHandoff.RunAsync(')
assert 'await using var fastPreflight' in orchestrator
assert 'GetIdleTimeoutAsync(ct)' in idlescope and 'SetIdleTimeoutAsync(1000,ct)' in idlescope
assert 'SetIdleTimeoutAsync(_original,timeout.Token)' in idlescope
assert 'RestoreAsync()' in idlescope
assert 'IsTikTokContactsPrompt' in state
assert 'permission_deny_button' in state and 'contacts' in state and 'TikTok' in state
assert 'FAST_POST_GOOGLE_CONTACTS_DENIED' in state
assert state.count('if(await DismissTikTokContactsPromptAsync(session,source,ct,log))') == 2
assert 'FAST_PROFILE_TAP' in state and 'BottomProfileTabPoint(ui)' in state
assert 'PROFILE_EDIT_USERNAME_FOUND' in state
assert 'IsValidHandle' in state and 'OtherApprovedAccount' in state
assert 'CLEAR_TIKTOK_DATA_THEN_GOOGLE_LOGIN' in state
assert 'ClearTikTokAppDataAsync' in state and 'ClearGooglePlayServicesDataAsync' in state
assert 'TimeSpan.FromSeconds(3)' in login and 'FAST_POST_GOOGLE_RESULT' in login
assert 'LOGIN' not in source['LiveSearchIdleSafety.cs']
assert 'LIVE_SEARCH_FLOW_ATTEMPT' not in idlescope
assert 'onboarding' in login.lower()
# Source punctuation guard, not a substitute for a compiler.
for f,t in source.items():
    cleaned=re.sub(r'//[^\n]*|/\*[\s\S]*?\*/|@?"(?:""|\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', ' ', t)
    assert cleaned.count('{')==cleaned.count('}'),f
print('PASS: scoped idle lifecycle, unchanged identity/pm-clear guards, bounded contacts handling, original LIVE ownership')
print('NOTE: must run .NET 8 build and physical Appium device tests on Windows')
