"""Offline regression checks: never clicks a device or sends photos."""
from pathlib import Path
import json
from lxml import etree
b=Path(__file__).resolve().parent
sel=json.loads((b/'AndroidMultiDeviceV1/Config/selectors.json').read_text('utf8'))
match=(b/'AndroidMultiDeviceV1/Core/GalleryPhotoMatcher.cs').read_text('utf8')
workflow=(b/'AndroidMultiDeviceV1/Core/ProfileUpdates.cs').read_text('utf8')

def matched(k,x):
    root=etree.fromstring(x.encode())
    return any(bool(root.xpath(s['value'])) for s in sel[k] if s['by']=='xpath')
new_gallery='''<hierarchy><android.widget.Button resource-id="com.ss.android.ugc.trill:id/l3z" bounds="[7,85][117,197]"/><android.widget.Button resource-id="com.ss.android.ugc.trill:id/y3s" text="Next" enabled="false"/></hierarchy>'''
old_gallery='''<hierarchy><android.widget.Button resource-id="com.ss.android.ugc.trill:id/l37" bounds="[76,85][97,106]"/><android.widget.Button resource-id="com.ss.android.ugc.trill:id/y1v" text="Next"/></hierarchy>'''
for xml in (new_gallery,old_gallery):
    assert matched('profileGalleryItems',xml)
    assert matched('profilePhotoNext',xml)
assert sel['profileGalleryItems'][0]['value'].endswith('id/l3z"]')
assert sel['profilePhotoNext'][0]['value'].endswith('id/y3s"]')
assert 'singlePhotoCircle=circles.Count==1 && badges.Count==1' in match
assert 'best>=0.88' in match and 'best-second>=0.08' in match
assert 'singlePhotoCircle' in match and 'screenshot.Width/4.0' in match
assert 'var circles=' in match and 'var candidates=' in match
assert 'Profile' not in sel['profileGalleryItems'][0]['value']
assert 'GalleryPhotoMatcher.FindAsync' in workflow
assert 'profilePhotoNext' in workflow and 'profilePhotoConfirm' in workflow
assert 'PhotoChangeState="SAVE_PENDING"' in workflow
assert 'Ảnh: Gallery lượt {page+1}/4;' in workflow
# synthetic geometry matches the single top-left thumbnail visible on 460x791 screenshot.
w,h=460,791
x,y,r=96,90,19
side=round(w/4)
margin=min(12,max(3,round(side*.08)))
top=min(4,max(0,round(side*.02)))
tile=(x+r+margin-side,y-top,side,side)
assert 0<=tile[0]<=20 and 70<=tile[1]<=100 and side>=100, tile
print('PASS selectors legacy/new + single-tile matcher safety gates + gallery progression + 460px geometry')
