"""Offline regression tests for operator-provided Name UI changes.
No Appium session; no accounts accessed; no network calls.
"""
from pathlib import Path
import json
from lxml import etree
base=Path(__file__).parent
sel=json.loads((base/'AndroidMultiDeviceV1/Config/selectors.json').read_text('utf-8'))
src=(base/'AndroidMultiDeviceV1/Core/ProfileUpdates.cs').read_text('utf-8')
state=(base/'AndroidMultiDeviceV1/Core/TikTokStateManager.cs').read_text('utf-8')

def loc_match(key,xml):
    doc=etree.fromstring(xml.encode())
    return any(bool(doc.xpath(it['value'])) for it in sel[key] if it['by']=='xpath')
edit='''<hierarchy><android.widget.LinearLayout>
<android.widget.TextView text="Edit profile"/>
<android.widget.FrameLayout clickable="true"><android.widget.TextView text="Name"/>
<android.widget.TextView resource-id="com.ss.android.ugc.trill:id/mud" text="Person Name"/></android.widget.FrameLayout>
<android.widget.FrameLayout clickable="true"><android.widget.TextView text="Username"/>
<android.widget.TextView resource-id="com.ss.android.ugc.trill:id/mud" text="account_007"/></android.widget.FrameLayout>
<android.widget.TextView text="Bio"/>
<android.widget.TextView resource-id="com.ss.android.ugc.trill:id/tvContent" text="abc"/>
<android.widget.ImageView content-desc="Camera"/>
</android.widget.LinearLayout></hierarchy>'''
name_hw7='''<hierarchy><android.widget.TextView text="Name"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/hw7" text="old name"/>
<android.widget.ImageView content-desc="Close"/>
<android.widget.Button content-desc="Save"/></hierarchy>'''
name_lfn='''<hierarchy><android.widget.TextView text="Name"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/lfn" text="old name"/></hierarchy>'''
name_hvc='''<hierarchy><android.widget.TextView text="Name"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/hvc" text="old name"/></hierarchy>'''
bio_hw7='''<hierarchy><android.widget.TextView text="Bio"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/hw7" text="old bio"/></hierarchy>'''
wrong_username='''<hierarchy><android.widget.TextView text="Username"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/hw7" text="account_007"/></hierarchy>'''
assert loc_match('profileNameOpenCandidates',edit)
assert sel['profileNameOpenCandidates'][0]['value'].endswith(')[1]')
assert not any('id/mud' in item['value'] and item['value'].endswith(')[2]') for item in sel['profileNameOpenCandidates'])
assert loc_match('profileEditUsername',edit) and sel['profileEditUsername'][0]['value'].endswith(')[2]')
for xml in (name_hw7,name_lfn,name_hvc):
    assert loc_match('profileNameEditorInput',xml)
assert loc_match('profileNameEditorInputNew',name_hw7)
assert loc_match('profileBioEditorInput',bio_hw7)
assert loc_match('profileNameClear',name_hw7) and loc_match('profileEditorSave',name_hw7)
assert loc_match('profilePhotoCamera',edit)
assert 'PROFILE_UPDATE_USERNAME_SUBEDITOR_DETECTED' in src
assert 'PROFILE_UPDATE_USERNAME_SUBEDITOR_RETURNED' in src
assert 'PROFILE_NAME_EDITOR_READY' in src
assert 'HasExactUiText(source,"Username")' in src
assert 'HasExactUiText(source,"Name")' in src
assert 'PROFILE_UPDATE_EDIT_REUSED' in src
assert 'TryReadNewEditProfileUsername(xml,out var editHandle)' in state
assert 'session.ClickVisibleAnyAsync(selectors.Get("profileEditEntry"),ct)' in state
assert 'profileNameEditorInputNew' in src
assert 'profileBioEditorInput' in src
print('PASS: Name mud[1]; username mud[2] read-only; Name hw7/lfn/hvc; Bio hw7/hvc; selectors and guards')
print('NOTE: selector/unit tests only; a .NET 8 Windows build and Android run are still required')
