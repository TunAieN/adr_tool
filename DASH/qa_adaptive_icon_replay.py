#!/usr/bin/env python3
"""Offline pixel-by-pixel replay of .NET LiveVisualOcr.DetectBadgeIcon geometry.
Uses three user-provided ZIPs for 24 genuine Person icon instances and
synthetic Like masks as negatives. Does not launch Appium or tap any LIVE card.
"""
import zipfile, io, json, math, pathlib, collections
import numpy as np
from PIL import Image
ROOT=pathlib.Path('/mnt/data')
PersonMasks=[
['...............','.......##......','......####.....','......####.....','........#......','...............','......####.....','.....######....','.....######....','...............'],
['...............','......###......','......####.....','......####.....','.......##......','...............','......####.....','.....######....','.....######....','...............']]
LikeMasks=[
['...............','........##.....','.......###.....','......####.....','....#.######...','....#.######...','....#.######...','....#.######...','.......####....','...............'],
['.........#.....','........##.....','.......###.....','......######...','....#.######...','....#.######...','....#.######...','....#.######...','......#####....','...............']]
P=[np.array([[c=='#' for c in row] for row in m]) for m in PersonMasks]
L=[np.array([[c=='#' for c in row] for row in m]) for m in LikeMasks]
def score(o,t):
 common=np.logical_and(o,t).sum();return 2*common/(o.sum()+t.sum()) if (o.sum()+t.sum()) else 0

def head_shoulders(mask):
 return (sum(mask[2,x] or mask[3,x] for x in range(3,13))>=3 and
         sum(mask[6,x] or mask[7,x] for x in range(3,13))>=5 and
         sum(mask[5,x] for x in range(4,12))<=1)

def classify(img,rect):
 x,y,w,h=rect
 trimw=max(1,round(w*.125)); trimh=max(1,round(h*.14));
 shiftx=max(1,round(w*.125)); shifty=max(1,round(h*.07))
 # Python banker rounding differs only in .5 ties from C# Math.Round: none in test
 trial=[(0,0,0,0),(0,shifty,0,-trimh),(shiftx//2,shifty,0,-trimh),
        (shiftx,shifty,-trimw,-trimh),(0,shifty,-trimw,-trimh),
        (0,0,-trimw,0),(shiftx//2,0,-trimw,0),(shiftx,0,-trimw,0),
        (0,0,0,-trimh),(shiftx//2,0,0,-trimh)]
 arr=np.array(img)
 bestp=(0,None);bestl=(0,None)
 for j,(dx,dy,dw,dh) in enumerate(trial):
  xx=x+dx;yy=y+dy;ww=w+dw;hh=h+dh
  if ww<8 or hh<7 or xx<0 or yy<0 or xx+ww>img.width or yy+hh>img.height:continue
  o=np.zeros((10,15),dtype=bool)
  for row in range(10):
   for col in range(15):
    px=xx+min(ww-1, math.floor((col+.5)*ww/15)); py=yy+min(hh-1, math.floor((row+.5)*hh/10))
    o[row,col]=min(arr[py,px,:3])>=185
  ps=max(score(o,t) for t in P); ls=max(score(o,t) for t in L)
  if ps>=.72 and ps-ls>=.12 and head_shoulders(o) and ps>bestp[0]:bestp=(ps,j)
  if ls>=.72 and ls-ps>=.12 and ls>bestl[0]:bestl=(ls,j)
 if bestp[1] is not None and bestl[1] is None:return 'Person',bestp,bestl
 if bestl[1] is not None and bestp[1] is None:return 'Like',bestp,bestl
 return 'Unknown',bestp,bestl

def find_region(im,cards,index):
 tile=cards[index]; X,Y,W,H=(tile[k] for k in ('x','y','width','height'))
 arr=np.array(im)
 a=max(0,round(X+W*.02));b=min(im.width-1,round(X+W*.49));top=max(0,round(Y+2));bottom=min(im.height-1,round(Y+min(24,H-1)));need=max(7,round(W*.085));allowed=round(X+W*.105)
 candidates=[]
 for row in range(top,bottom+1):
  run=-1
  for col in range(a,b+2):
   r=False
   if col<=b:
    cr,cg,cb=map(int,arr[row,col,:3]);r=cr>160 and cg<160 and cr>cg*1.6 and cr>cb*1.1
   if r and run<0:run=col
   if not r and run>=0:
    length=col-run
    if run<=allowed and length>=need:candidates.append((row,col-1,length))
    run=-1
 if not candidates:return None
 longest=max(c[2] for c in candidates)
 row,right,_=sorted((c for c in candidates if c[2]>=longest-2),key=lambda c:(c[0],-c[2]))[0]
 left=right+max(1,round(W*.006));width=max(8,round(W*.09));height=max(10,round(W*.081))
 return (left,row,width,height)

rows=[]
for run in ('175016','175018','175101'):
 with zipfile.ZipFile(ROOT/f'live-scroll-20261026-{run}-1.zip') as z:
  for vp in range(1,4):
   path=next(x for x in z.namelist() if x.endswith(f'viewport-{vp:02d}.png'))
   im=Image.open(io.BytesIO(z.read(path))).convert('RGB');cards=json.loads(z.read(path.replace('.png','.json')))['cards']
   for i in ([0,1,2,3] if vp==1 else [2,3]):
    r=find_region(im,cards,i)
    pred,ps,ls=classify(im,r) if r else ('Unknown',(0,None),(0,None))
    rows.append({'device':run,'viewport':vp,'slot':i+1,'expected':'Person','observed':pred,'aligned_person_score':round(ps[0],3),'trial':ps[1],'aligned_like_score':round(ls[0],3)})

# Synthetic negative thumbs-up badges. These test that crop search does not
# implicitly convert a Like shape to Person; they do not replace real Like QA.
for mask_index,mask in enumerate(LikeMasks):
 for w in (15,16,21,26,35):
  for h in (12,14,18,28):
   for xshift in (0,1,2):
    template=np.array([[c=='#' for c in row] for row in mask],dtype=np.uint8)
    t=(Image.fromarray(template*255,'L').resize((w,h),Image.Resampling.NEAREST))
    im=Image.new('RGB',(w+30,h+24),(28,32,35))
    rgb=Image.merge('RGB',(t,t,t))
    im.paste(rgb,(5+xshift,6))
    # Test extractor assuming icon region remains anchored at x=5.
    pred,ps,ls=classify(im,(5,6,w,h))
    rows.append({'device':'synthetic-like','viewport':mask_index+1,'slot':f'{w}x{h}+{xshift}','expected':'LikeOrUnknown','observed':pred,'aligned_person_score':round(ps[0],3),'trial':ps[1],'aligned_like_score':round(ls[0],3)})

p=[r for r in rows if r['expected']=='Person']; like=[r for r in rows if r['expected']!='Person']
assert len(p)==24, len(p)
positive_ok=sum(r['observed']=='Person' for r in p)
negative_false=sum(r['observed']=='Person' for r in like)
summary={'positive_total':len(p),'positive_person':positive_ok,'synthetic_like_total':len(like),'synthetic_like_false_person':negative_false,'positive_by_device':{},'negative_by_class':dict(collections.Counter(x['observed'] for x in like)), 'limitations':['Cannot prove behavior for real Like images because no labeled real Like PNGs were provided','Cannot test Appium, Windows GDI+ or live navigation in this environment','This is a Python arithmetic replay matching C# decision geometry, not executable C# testing']}
for d in ('175016','175018','175101'):
 a=[r for r in p if r['device']==d];summary['positive_by_device'][d]=sum(r['observed']=='Person' for r in a)
print(json.dumps(summary,indent=2,ensure_ascii=False))
for r in p:
 if r['observed']!='Person':print('FALSE NEGATIVE',r)
for r in like:
 if r['observed']=='Person':print('FALSE PERSON',r)
(ROOT/'work_adaptive/qa_adaptive_results.json').write_text(json.dumps({'summary':summary,'cases':rows},indent=2,ensure_ascii=False))
assert positive_ok>=23,'Positive regression too low'
assert negative_false==0,'Synthetic Like misclassified as viewer'
