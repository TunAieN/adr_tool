"""Offline selector regression checks. No Appium, credentials or network required."""
import json
from pathlib import Path
from lxml import etree
ROOT = Path(__file__).parent
selectors = json.loads((ROOT/'AndroidMultiDeviceV1/Config/selectors.json').read_text(encoding='utf8'))
source = (ROOT/'AndroidMultiDeviceV1/Core/ProfileUpdates.cs').read_text(encoding='utf8')

def xp(key):
    return [s['value'] for s in selectors[key] if s['by']=='xpath']

def matches(key, xml):
    doc=etree.fromstring(xml.encode())
    return any(bool(doc.xpath(x)) for x in xp(key))

edit = '''<hierarchy><android.widget.LinearLayout>
  <android.widget.TextView text="Edit profile"/>
  <android.widget.LinearLayout clickable="true"><android.widget.TextView text="Name"/>
    <android.widget.TextView resource-id="com.ss.android.ugc.trill:id/mud" text="Name text"/>
  </android.widget.LinearLayout>
  <android.widget.LinearLayout clickable="true"><android.widget.TextView text="Username"/>
    <android.widget.TextView resource-id="com.ss.android.ugc.trill:id/mud" text="username123"/>
  </android.widget.LinearLayout>
  <android.widget.TextView text="Bio"/>
  <android.widget.TextView resource-id="com.ss.android.ugc.trill:id/tvContent" text="Example bio"/>
  <android.widget.ImageView content-desc="Camera" />
</android.widget.LinearLayout></hierarchy>'''
name_new = '''<hierarchy><android.widget.TextView text="Name"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/lfn" text="Old name"/>
<android.widget.ImageView content-desc="Close"/>
<android.widget.Button content-desc="Save"/></hierarchy>'''
name_old = '''<hierarchy><android.widget.TextView text="Name"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/hvc"/>
<android.widget.Button content-desc="Save"/></hierarchy>'''
bio_new = '''<hierarchy><android.widget.TextView text="Bio"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/hw7"/>
<android.widget.Button content-desc="Save"/></hierarchy>'''
bio_old = '''<hierarchy><android.widget.TextView text="Bio"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/hvc"/>
<android.widget.Button content-desc="Save"/></hierarchy>'''
username_wrong = '''<hierarchy><android.widget.TextView text="Username"/>
<android.widget.EditText resource-id="com.ss.android.ugc.trill:id/lfn"/></hierarchy>'''
permission = '''<hierarchy><android.widget.Button resource-id="com.android.permissioncontroller:id/permission_allow_all_button" /></hierarchy>'''
assert matches('profileNameOpenCandidates', edit), 'new edit page has a name candidate'
assert matches('profileBioOpenCandidates', edit), 'new edit page has bio candidate'
assert matches('profileNameEditorInput', name_new) and matches('profileNameEditorInput', name_old)
assert matches('profileBioEditorInput', bio_new) and matches('profileBioEditorInput', bio_old)
assert matches('profileNameClear', name_new) and matches('profileEditorSave', name_new)
assert matches('profileEditorSave', bio_new)
assert matches('profilePhotoCamera', edit)
assert 'Upload photo' in str(selectors['profilePhotoUpload'])
assert 'com.android.permissioncontroller:id/permission_allow_all_button' in source
assert 'PROFILE_UPDATE_EDIT_REUSED' in source and 'PROFILE_UPDATE_HANDLE_RECHECK' in source
assert 'HasExactUiText(source,"Username")' in source and 'namePage &&' in source
assert 'profileNameEditorInputNew' in source and 'profileBioEditorInput' in source
assert 'profileGalleryItems' in source and 'GalleryPhotoMatcher.FindAsync' in source
# The user's current screenshot shows mud[2] is the username, not Name.
doc=etree.fromstring(edit.encode())
mud=doc.xpath('//android.widget.TextView[@resource-id="com.ss.android.ugc.trill:id/mud"]')
assert len(mud)==2 and mud[0].get('text')=='Name text' and mud[1].get('text')=='username123'
assert matches('profileNameEditorInputNew', username_wrong), 'mock: input ID is not enough; page-title guard is required'
print('PASS: new/old Name inputs, new/old Bio inputs, navigation guards, photo and permission selectors')
print('NOTE: offline XML regression only; requires Windows .NET build and real Appium test for end-to-end verification')
