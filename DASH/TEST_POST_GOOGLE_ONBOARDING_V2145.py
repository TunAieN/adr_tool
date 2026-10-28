"""Offline regression checks for the V2.1.45 targeted login-state patch.
These are source-contract tests, not .NET compilation or Android instrumentation.
Run: python TEST_POST_GOOGLE_ONBOARDING_V2145.py
"""
from pathlib import Path
import json
import re

root=Path(__file__).parent
state=(root/'AndroidMultiDeviceV1/Core/TikTokStateManager.cs').read_text('utf-8')
login=(root/'AndroidMultiDeviceV1/Core/LoginWorkflow.cs').read_text('utf-8')
sel=json.loads((root/'AndroidMultiDeviceV1/Config/selectors.json').read_text('utf-8'))

# Original automation selectors still present.
assert 'profile' in sel and 'continueGoogle' in sel
for key in ['googleChooser','useAnotherAccount','useAnotherAccountLabel','googleEmail','googlePassword',
            'firstLaunchGenderFirst','firstLaunchGenderContinue','firstLaunchInterestSkip']:
    assert sel.get(key),key
# Post-auth setup runs BEFORE the normal identity verification.
assert 'CompletePostGoogleOnboardingAsync(\n                session,selectors' in login
assert login.index('CompletePostGoogleOnboardingAsync(') < login.index('// Google sign-in completing does not prove TikTok identity')
assert 'POST_GOOGLE_ONBOARDING_DETECTED' in state
assert 'IsOnboardingScreen(initial)' in state
assert 'case TikTokUiState.Onboarding:' in state
assert 'navigationRecoveryAttempted=false;' in state
assert 'HandleNewDeviceOnboardingIfPresentAsync(\n                    session,UiSelectors.Load()' in state
assert state.index('case TikTokUiState.Onboarding:') < state.index('var result=await login.LoginAsync(')
# No unbounded loop on popup: one Back and one retry, and never a saved account row.
assert 'GOOGLE_CHOOSER_OVERLAY_DETECTED' in login
assert 'GOOGLE_CHOOSER_REOPEN' in login
assert 'await session.BackAsync(ct);' in login
assert 'if(!chooser && !emailPage)' in login
assert 'useAnotherAccountLabel' in login
assert 'Never select a stored identity by row index.' in login
# Previous account-state safeguards still exist.
assert 'ResetTikTokForApprovedSwitchAsync' in state
assert 'TikTokUiState.OtherApprovedAccount' in state
assert 'TikTokUiState.ExpectedAccount' in state
assert 'Onboarding' in state
# Basic structural check: count braces outside comments/string literals roughly.
# This is NOT a C# compiler, deliberately do not call it a compile test.
def mask_comments_strings(src):
    return re.sub(r'//[^\n]*|/\*[\s\S]*?\*/|@?"(?:""|\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', ' ', src)
for filename,src in [('state',state),('login',login)]:
    masked=mask_comments_strings(src)
    assert masked.count('{')==masked.count('}'), filename
print('PASS: JSON valid, selectors preserved, post-Google onboarding and bounded chooser recovery present, basic structure OK')
print('NOTE: .NET compile and live Android tests not available in this environment')
