#!/usr/bin/env python3
"""V5.7 static and XML contract tests; not a substitute for .NET/phone E2E."""
from pathlib import Path
import re, xml.etree.ElementTree as ET
R=Path(__file__).parent/'AndroidMultiDeviceV1'/'Core'
nav=(R/'TikTokNavigationRecovery.cs').read_text(encoding='utf-8-sig')
engine=(R/'LiveSearchEngine.cs').read_text(encoding='utf-8-sig')
cycle=(R/'LiveAutonomousWorkflow.cs').read_text(encoding='utf-8-sig')
coord=(R/'LiveRoomContinuousCoordinator.cs').read_text(encoding='utf-8-sig')
owner=(R/'DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')
for v in ['"Find people"','"Search by name or username"','"Connect Now"','"Use QR code"','"Invite friends"']:
    assert v in nav,v
assert 'XDocument.Parse(xml)' in nav
assert 'for(int back=1;back<=5;back++)' in nav
assert 'await _adb.ForceStopTikTokAsync(_serial,ct)' in nav
assert 'await _adb.LaunchTikTokAsync(_serial,ct)' in nav
assert 'pmClear=false' in nav
assert 'ClearTikTokAppDataAsync' not in nav
assert 'ClearGooglePlayServicesDataAsync' not in nav
assert 'LiveSearchEngine.IsSearchRecoveryProtectedUi(xml)' in nav
assert 'TikTokNavigationRecovery.IsFindPeopleScreen(initial)' in engine
assert 'TikTokNavigationRecovery.IsFindPeopleScreen(xml)' in engine
assert 'searchNavigationStopReason="FIND_PEOPLE_RECOVERY_FAILED"' in engine
assert 'NAV_RECOVERY_CHECKPOINT' in coord
assert 'RestoreFindPeopleCheckpointAsync' in coord
assert 'catch(TikTokNavigationCheckpointException)' in coord
assert 'ReturnToKeywordSearchAsync(ct)' in coord
assert 'LIVE_OWNER_VERIFY' in owner and 'TikTokNavigationRecovery.IsFindPeopleScreen(source)' in owner
assert 'navigationRecoveryExhausted' in cycle
assert 'restartAttempt>CurrentRecoveryLimit' in cycle
# Positive and negative XML shapes representative of accessibility snapshots.
normal='<hierarchy><node text="Home"/><node text="Find people to follow"/></hierarchy>'
positive='<hierarchy><node text="Find people"/><node text="Search by name or username"/><node text="Connect Now"/><node text="Use QR code"/></hierarchy>'
negative='<hierarchy><node text="Find people"/><node text="LIVE"/></hierarchy>'
for sample in [normal,positive,negative]:ET.fromstring(sample)
# Cross-check the intended classifier invariant against a simple independent
# textual model (the C# production parser examines the same attributes).
def match(xml):
    root=ET.fromstring(xml)
    labels=[v.strip().casefold() for node in root.iter() for k in ('text','content-desc')
            if (v:=node.attrib.get(k))]
    return ('find people' in labels or 'tìm bạn bè' in labels or 'tìm người' in labels) and \
        bool({'search by name or username','connect now','use qr code','invite friends',
              'kết nối ngay','dùng mã qr','mời bạn bè'} & set(labels))
assert match(positive) and not match(normal) and not match(negative)
print('V5.7 NAVIGATION RECOVERY STATIC / XML-CONTRACT PASS')
