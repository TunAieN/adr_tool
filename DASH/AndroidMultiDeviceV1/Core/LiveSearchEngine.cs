using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

public sealed record LiveSearchCard(string Keyword,string Title,string Creator,int? Viewers,
    string ViewerEvidence,string Fingerprint,string Note);

public readonly record struct LiveSearchTile(int X,int Y,int Width,int Height)
{
    public int Bottom=>Y+Height;
}

public sealed record LiveSearchReport(IReadOnlyList<LiveSearchCard> Cards,
    int KeywordsAttempted,int Scrolls,string FinishReason,LiveSearchCard? SelectedCandidate=null);

/// <summary>
/// Read-only extraction of search results. Never opens a room or submits content.
/// The person/viewer pictogram cannot ALWAYS be proven from an unlabeled ImageView:
/// only explicit accessibility metadata is trusted for a verified viewer count.
/// An unlabeled icon alongside a LIVE badge is reported as provisional.
/// </summary>
public static class LiveSearchCardReader
{
    readonly record struct Bounds(int X,int Y,int Width,int Height)
    {
        public int Right=>X+Width;
        public int Bottom=>Y+Height;
        public int MidX=>X+Width/2;
        public int MidY=>Y+Height/2;
        public bool Valid=>Width>0 && Height>0;
    }
    static Bounds Box(XElement e)
    {
        var raw=(string?)e.Attribute("bounds")??"";
        var m=Regex.Match(raw,@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
        if(!m.Success)return default;
        var a=m.Groups.Cast<Group>().Skip(1)
            .Select(g=>int.Parse(g.Value,CultureInfo.InvariantCulture)).ToArray();
        return new Bounds(a[0],a[1],a[2]-a[0],a[3]-a[1]);
    }
    static string Attr(XElement e,string attr)=>((string?)e.Attribute(attr)??"").Trim();
    static bool Is(XElement e,string cls)=>Attr(e,"class").Equals(cls,StringComparison.Ordinal) ||
        e.Name.LocalName.Equals(cls,StringComparison.Ordinal);
    static string Label(XElement e)
    {
        string t=Attr(e,"text");
        return t.Length>0?t:Attr(e,"content-desc");
    }
    static bool IsGrid(XElement e)=>Is(e,"android.widget.GridView");
    static bool IsLiveLabel(string value)=>Regex.IsMatch(value,@"^LIVE(?:\s+\S+)?$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static bool ViewerNamed(XElement node)
    {
        string metadata=Attr(node,"content-desc")+" "+Attr(node,"resource-id");
        return Regex.IsMatch(metadata,@"(viewer|audience|watching|người xem|đang xem|online|person|people)",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    }
    static int? ParseViewers(string text)
    {
        // TikTok badge can show 483, 1.7K, 12.6K+, 2,4K+ or 2.450.
        string raw=text.Trim().TrimEnd('+','＋').Trim();
        var m=Regex.Match(raw,@"^(\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*([KkMm])?$",
            RegexOptions.CultureInvariant);
        if(!m.Success)return null;
        string digits=m.Groups[1].Value;
        bool abbreviated=m.Groups[2].Success;
        if(!abbreviated && Regex.IsMatch(digits,@"^\d{1,3}(?:[.,]\d{3})+$"))
            digits=digits.Replace(".","").Replace(",","");
        else digits=digits.Replace(',','.');
        if(!double.TryParse(digits,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double n))return null;
        if(abbreviated)n*=m.Groups[2].Value.Equals("M",StringComparison.OrdinalIgnoreCase)?1_000_000:1_000;
        return n>=0 && n<=100_000_000?(int)Math.Round(n):null;
    }
    // Do not require an exposed text="LIVE" inside every thumbnail. On some
    // TikTok screens only the LIVE *tab* is exposed as text; the thumbnails are
    // image-backed nodes that have no accessible LIVE badge at all.
    static bool LooksLikeTile(XElement node,Bounds grid)
    {
        var b=Box(node);
        if(!b.Valid || !grid.Valid || grid.Width<160)return false;
        int minWidth=Math.Max(65,grid.Width*27/100);
        int maxWidth=grid.Width*72/100;
        return b.Width>=minWidth && b.Width<=maxWidth &&
            b.Height>=75 && b.Height<=Math.Max(500,grid.Height*2) &&
            b.MidX>=grid.X && b.MidX<=grid.Right &&
            b.Bottom>grid.Y+15 && b.Y<grid.Bottom-10;
    }
    static List<XElement> FindTiles(XElement grid)
    {
        var g=Box(grid);
        // Take OUTERMOST half-grid-sized nodes. Their internal photo overlays,
        // badges and labels can be nested any number of levels down.
        var candidates=grid.Descendants().Where(n=>LooksLikeTile(n,g)).ToList();
        var roots=candidates.Where(n=>!n.Ancestors().Any(a=>
            !ReferenceEquals(a,grid) && a.AncestorsAndSelf().Contains(grid) && LooksLikeTile(a,g)))
            .OrderBy(n=>Box(n).Y).ThenBy(n=>Box(n).X).ToList();
        var found=new List<XElement>();
        foreach(var item in roots)
        {
            var b=Box(item);
            if(found.Any(previous=>{var a=Box(previous);
                return Math.Abs(a.X-b.X)<12 && Math.Abs(a.Y-b.Y)<15 &&
                    Math.Abs(a.Width-b.Width)<15;
            }))continue;
            found.Add(item);
        }
        return found;
    }
    static string? CombinedViewers(string text)
    {
        var m=Regex.Match(text.Trim(),@"^LIVE\s+([\d.,]+\s*[KkMm]?\+?)$",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return m.Success?m.Groups[1].Value:null;
    }
    static LiveSearchCard MakeCard(string keyword,XElement root,List<XElement> nodes)
    {
        var tile=Box(root);
        // Include siblings placed just below an image tile (title/creator).
        var labels=nodes.Where(n=>{
                var b=Box(n);
                return b.Valid && b.MidX>=tile.X && b.MidX<=tile.Right &&
                    b.Y>=tile.Y-5 && b.Y<=tile.Bottom+58 && Label(n).Length>0;
            }).Select(n=>(node:n,text:Label(n),box:Box(n)))
            .GroupBy(t=>(t.text,t.box.X,t.box.Y))
            .Select(g=>g.First()).ToList();
        var numbers=new List<(int count,int certainty,int y)>();
        foreach(var t in labels)
        {
            string value=CombinedViewers(t.text)??t.text;
            var n=ParseViewers(value);
            if(!n.HasValue)continue;
            // Viewer must be located on the upper area of the tile, or its
            // accessibility metadata must explicitly say viewer/audience.
            bool named=ViewerNamed(t.node);
            if(!named && (t.box.Y>tile.Y+Math.Min(95,Math.Max(40,tile.Height/3)) ||
                t.box.Y<tile.Y-7))continue;
            bool viewerIcon=nodes.Where(a=>Is(a,"android.widget.ImageView") && ViewerNamed(a))
                .Any(a=>{var b=Box(a);
                    return b.Valid && b.X>=tile.X && b.X<=tile.Right &&
                        Math.Abs(b.MidY-t.box.MidY)<22;
                });
            numbers.Add((n.Value,named||viewerIcon?2:1,t.box.Y));
        }
        var top=numbers.OrderByDescending(n=>n.certainty).ThenBy(n=>n.y).ToList();
        int? viewer=top.Count>0 && top.Where(x=>x.certainty==top[0].certainty)
            .Select(x=>x.count).Distinct().Count()==1?top[0].count:null;
        string evidence=viewer.HasValue?(top[0].certainty==2?"Verified":"BadgeCandidate"):
            "Không đọc được badge người xem";
        var secondary=labels.Where(t=>t.text.Length<=130 && !IsLiveLabel(t.text) &&
                ParseViewers(t.text) is null && CombinedViewers(t.text) is null &&
                !string.Equals(t.text,"LIVE",StringComparison.OrdinalIgnoreCase))
            .OrderBy(t=>t.box.Y).Select(t=>t.text).Distinct(StringComparer.Ordinal).ToList();
        string title=secondary.FirstOrDefault()??"Chưa đọc được tiêu đề";
        string creator=secondary.Skip(1).FirstOrDefault()??"Chưa đọc được chủ phòng";
        // If a card is purely image-backed, its content cannot be identified
        // reliably from XML. We deliberately do not pretend a coordinate is
        // a stable room identifier across scrolling.
        string fingerprint=secondary.Count>0
            ?string.Join("|",secondary.Take(3)).Trim().ToLowerInvariant()
            :$"chua-ro-card-{tile.X/12}-{tile.Y/12}-{viewer?.ToString()??"?"}";
        string note=evidence switch {
            "Verified"=>"Số Viewer có metadata xác minh",
            "BadgeCandidate"=>"Số trên vùng card; icon người chưa được xác minh trong XML",
            _=>"Đã nhận diện ô card, nhưng TikTok chưa cung cấp số Viewer qua XML"
        };
        return new LiveSearchCard(keyword,title,creator,viewer,evidence,fingerprint,note);
    }
    public static IReadOnlyList<LiveSearchCard> Read(string xml,string keyword)
    {
        XDocument doc;
        try{doc=XDocument.Parse(xml);}catch(System.Xml.XmlException){return [];}
        var nodes=doc.Descendants().ToList();
        var cards=new List<LiveSearchCard>();
        foreach(var grid in nodes.Where(IsGrid).OrderByDescending(g=>Box(g).Width*Box(g).Height))
        {
            if(!Box(grid).Valid)continue;
            foreach(var root in FindTiles(grid))cards.Add(MakeCard(keyword,root,nodes));
            if(cards.Count>0)break;
        }
        return cards;
    }
    // Same order as Read(): card rectangles from actual Android UI hierarchy.
    public static IReadOnlyList<LiveSearchTile> TileBounds(string xml)
    {
        try
        {
            var grids=XDocument.Parse(xml).Descendants().Where(IsGrid)
                .OrderByDescending(g=>Box(g).Width*Box(g).Height);
            foreach(var grid in grids)
            {
                var roots=FindTiles(grid);
                if(roots.Count==0)continue;
                return roots.Select(n=>{var b=Box(n);
                    return new LiveSearchTile(b.X,b.Y,b.Width,b.Height);
                }).ToList();
            }
        }
        catch(System.Xml.XmlException){}
        return [];
    }

    public static string Diagnose(string xml)
    {
        try
        {
            var nodes=XDocument.Parse(xml).Descendants().ToList();
            var grids=nodes.Where(IsGrid).ToList();
            var badges=nodes.Where(n=>IsLiveLabel(Label(n))).ToList();
            string gridSummary=string.Join(";",grids.Take(2).Select(g=>{
                var b=Box(g);return $"({b.X},{b.Y},{b.Width},{b.Height})";
            }));
            string roots=string.Join(";",grids.Take(1).SelectMany(FindTiles).Take(8).Select(n=>{
                var b=Box(n);return $"{Attr(n,"class").Split('.').Last()}@({b.X},{b.Y},{b.Width},{b.Height})";
            }));
            string gridChildren=string.Join(";",grids.Take(1).SelectMany(g=>g.Elements()).Take(8).Select(n=>{
                var b=Box(n);return $"{Attr(n,"class").Split('.').Last()}@({b.X},{b.Y},{b.Width},{b.Height})";
            }));
            string badgeSummary=string.Join(";",badges.Take(4).Select(n=>{
                var b=Box(n);return $"{Label(n)}@({b.X},{b.Y},{b.Width},{b.Height})";
            }));
            var gridLabels=grids.SelectMany(g=>g.DescendantsAndSelf())
                .Where(n=>Label(n).Length>0).ToList();
            string labelSummary=string.Join(";",gridLabels.Take(14).Select(n=>{
                var b=Box(n);return $"{Label(n)}@({b.X},{b.Y})";
            }));
            return $"GridView={grids.Count}, FrameLayout={nodes.Count(n=>Is(n,"android.widget.FrameLayout"))}, " +
                $"nhãn LIVE trong XML={badges.Count}, vùng GridView={gridSummary}, " +
                $"ô card cấu trúc={grids.SelectMany(FindTiles).Count()}, " +
                $"card={roots}; phần tử trực tiếp={gridChildren}; nhãn={badgeSummary}; " +
                $"nhãn thực trong GridView={gridLabels.Count}: {labelSummary}";
        }
        catch{return "XML chưa đọc được";}
    }
}

/// <summary>
/// Assigns a stable, viewport-local identity to each LIVE tile across small
/// upward drags. The page's Y coordinates change while the same room remains
/// visible, so neither a screen slot nor OCR title is a safe room identifier.
/// The tracker deliberately refuses to infer identity after a large jump.
/// </summary>
internal sealed class LiveCardScrollTracker
{
    // Viewer and screen coordinates are NOT unique room identities. A row may
    // shift up by one full card height even if dragGesture requested only 30%.
    // Track the actual preview pixels and reconcile borderline matches with
    // the previous viewport's OCR, without counting unproven tiles as new.
    readonly record struct Observed(int Id,LiveSearchTile Tile,byte[]? Appearance);
    List<Observed> _previous=[];
    List<Observed> _before=[];
    readonly List<Observed> _recentHistory=[];
    int _nextId=1;
    // Only pixel/ID matches of known tiles can provide a reliable actual
    // scroll offset. Unmatched animations do NOT count as motion evidence.
    public int MeasuredShiftPixels { get; private set; }
    public int LongSwipeCandidates { get; private set; }
    public int LongSwipeAccepted { get; private set; }

    static int MidX(LiveSearchTile t)=>t.X+t.Width/2;
    static bool SameColumn(LiveSearchTile a,LiveSearchTile b)=>
        Math.Abs(MidX(a)-MidX(b))<=26 && Math.Abs(a.Width-b.Width)<=32;

    static int viewportMotionLimit(IReadOnlyList<LiveSearchTile> tiles)=>
        tiles.Count==0?430:Math.Max(430,tiles.Max(t=>t.Bottom)*85/100);

    // Fixed-size color sketch from the TOP image area. The end of a tile may
    // be clipped by the viewport, but its top ~100px is also present when it
    // moves into the preceding row. Skip the first 26px containing the badge.
    static byte[]? Appearance(Bitmap bitmap,LiveSearchTile tile,double sx,double sy)
    {
        int top=tile.Y+28;
        int available=Math.Min(78,tile.Height-34);
        if(tile.Width<85 || available<36)return null;
        var bytes=new byte[12*6*3];int n=0;
        for(int y=0;y<6;y++)for(int x=0;x<12;x++)
        {
            int px=(int)Math.Round((tile.X+10+(tile.Width-20)*(x+0.5)/12.0)*sx);
            int py=(int)Math.Round((top+available*(y+0.5)/6.0)*sy);
            if(px<2 || px>=bitmap.Width-2 || py<2 || py>=bitmap.Height-2)return null;
            // Averages reduce JPEG/video noise while retaining visual layout.
            int r=0,g=0,b=0;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {var c=bitmap.GetPixel(px+dx,py+dy);r+=c.R;g+=c.G;b+=c.B;}
            bytes[n++]=(byte)(r/9);bytes[n++]=(byte)(g/9);bytes[n++]=(byte)(b/9);
        }
        return bytes;
    }
    static double Distance(byte[]? a,byte[]? b)
    {
        if(a is null || b is null || a.Length!=b.Length)return 1;
        long sum=0;
        for(int i=0;i<a.Length;i++)sum+=Math.Abs((int)a[i]-b[i]);
        return sum/(255.0*a.Length);
    }
    public (IReadOnlyList<int> IDs,bool Moved,IReadOnlySet<int> Ambiguous)
        Match(IReadOnlyList<LiveSearchTile> tiles,byte[] png,string xml)
    {
        using var ms=new MemoryStream(png);
        using var bitmap=new Bitmap(ms);
        int w=bitmap.Width,h=bitmap.Height;
        try
        {
            var root=XDocument.Parse(xml).Root;
            if(int.TryParse((string?)root?.Attribute("width"),out var rw) && rw>0)w=rw;
            if(int.TryParse((string?)root?.Attribute("height"),out var rh) && rh>0)h=rh;
        }
        catch(System.Xml.XmlException){}
        var sketches=tiles.Select(t=>Appearance(bitmap,t,bitmap.Width/(double)w,bitmap.Height/(double)h)).ToArray();
        _before=_previous.ToList();
        // Keep historical appearance sketches across long swipes, when no old
        // tile remains on screen to estimate movement from overlapping rows.
        _recentHistory.AddRange(_before);
        if(_recentHistory.Count>48)
            _recentHistory.RemoveRange(0,_recentHistory.Count-48);
        var used=new HashSet<int>();
        var current=new List<Observed>(tiles.Count);
        var ambiguous=new HashSet<int>();
        bool moved=false;
        for(int i=0;i<tiles.Count;i++)
        {
            var tile=tiles[i];
            // Search every candidate in this column, INCLUDING one full row
            // above. Do not select the geometrically nearest slot first.
            var possible=_before.Where(p=>!used.Contains(p.Id) && SameColumn(p.Tile,tile) &&
                tile.Y<=p.Tile.Y+22 && p.Tile.Y-tile.Y<=Math.Max(430,h*75/100))
                .Select(p=>(item:p,score:Distance(p.Appearance,sketches[i])))
                .OrderBy(p=>p.score).ToArray();
            var best=possible.FirstOrDefault();
            bool strong=possible.Length>0 && best.score<=0.105 &&
                (possible.Length==1 || possible[1].score-best.score>=0.025);
            int id;
            if(strong)
            {
                id=best.item.Id;
                used.Add(id);
                if(Math.Abs(best.item.Tile.Y-tile.Y)>=4) moved=true;
            }
            else
            {
                id=_nextId++;
                // Ambiguous visual identity requires OCR reconciliation below;
                // otherwise a displaced old tile could be counted twice.
                if(_before.Count>0)ambiguous.Add(i);
                if(_before.Count>0)moved=true;
            }
            current.Add(new Observed(id,tile,sketches[i]));
        }
        if(_before.Any(p=>!used.Contains(p.Id)))moved=true;
        _previous=current;
        return (current.Select(p=>p.Id).ToArray(),moved,ambiguous);
    }

    // A recent video thumbnail is not a stable room ID: its pixels may change
    // even when the audience stays unchanged. First restore old IDs using the
    // same-column/row-motion evidence; then independently assess whether each
    // genuinely new tile entered through the bottom of the visible GridView.
    // A card which cannot be classified remains PENDING in the caller's cache.
    public IReadOnlyDictionary<int,int> ReconcilePreviousViewport(
        IReadOnlyList<LiveSearchTile> tiles,IReadOnlyList<int> ids,
        IReadOnlyList<LiveSearchCard> cards,IReadOnlyDictionary<int,LiveSearchCard> cache,
        IReadOnlySet<int> indices,out IReadOnlySet<int> unresolved,
        out IReadOnlySet<int> provenNew)
    {
        LongSwipeCandidates=0;
        LongSwipeAccepted=0;
        var candidateAliases=new Dictionary<int,int>();
        var blocked=new HashSet<int>();
        var claimed=ids.Where((_,i)=>!indices.Contains(i)).ToHashSet();
        foreach(int i in indices)
        {
            if(i>=tiles.Count || i>=cards.Count)continue;
            var card=cards[i];
            // Match an old card by viewer ONLY in conjunction with its column
            // and independent pixel/paired-row evidence below. Equal viewer
            // counts alone do not establish a room identity.
            if(!card.Viewers.HasValue)continue;
            var options=_before.Where(old=>!claimed.Contains(old.Id) &&
                 SameColumn(old.Tile,tiles[i]) &&
                 old.Tile.Y>=tiles[i].Y-22 &&
                 old.Tile.Y-tiles[i].Y<=Math.Max(430,tiles.Max(t=>t.Bottom)*85/100) &&
                 cache.TryGetValue(old.Id,out var earlier) &&
                 earlier.Viewers.HasValue &&
                 Math.Abs(earlier.Viewers.Value-card.Viewers.Value)<=
                     Math.Max(3,(int)Math.Round(earlier.Viewers.Value*0.06)))
                 .OrderBy(old=>Math.Abs(old.Tile.Y-tiles[i].Y)).ToArray();
            if(options.Length==1)
            {
                candidateAliases[i]=options[0].Id;
                claimed.Add(options[0].Id);
            }
            else if(options.Length>1)blocked.Add(i);
        }
        var aliases=new Dictionary<int,int>();
        foreach(var pair in candidateAliases)
        {
            int i=pair.Key;
            var old=_before.First(o=>o.Id==pair.Value);
            double similarity=Distance(old.Appearance,_previous[i].Appearance);
            bool paired=candidateAliases.Any(x=>x.Key!=i &&
                Math.Abs(tiles[x.Key].Y-tiles[i].Y)<32 &&
                !SameColumn(tiles[x.Key],tiles[i]));
            if(similarity<=0.13 || paired)aliases[i]=pair.Value;
            else blocked.Add(i);
        }
        foreach(var pair in aliases)
        {
            var item=_previous[pair.Key];
            _previous[pair.Key]=item with {Id=pair.Value};
            blocked.Remove(pair.Key);
        }
        // Overlapping 2x2 viewport: the previous LOWER row becomes the
        // current UPPER row. Match BOTH columns together, rather than
        // assuming four wholly new cards after a drag. Never use viewers as ID.
        if(_before.Count==4 && tiles.Count==4 && aliases.Count==0)
        {
            var oldLower=_before.OrderByDescending(o=>o.Tile.Y).Take(2).ToArray();
            var nowUpper=Enumerable.Range(0,4).OrderBy(i=>tiles[i].Y).Take(2).ToArray();
            var pairs=nowUpper.Select(i=>new {
                Index=i,
                Old=oldLower.FirstOrDefault(o=>SameColumn(o.Tile,tiles[i])),
            }).ToArray();
            if(pairs.All(p=>p.Old.Id>0) && pairs.Select(p=>p.Old.Id).Distinct().Count()==2)
            {
                var scores=pairs.Select(p=>Distance(p.Old.Appearance,_previous[p.Index].Appearance)).ToArray();
                if(scores.All(d=>d<=0.18))
                {
                    foreach(var pair in pairs)
                    {
                        aliases[pair.Index]=pair.Old.Id;
                        var now=_previous[pair.Index];
                        _previous[pair.Index]=now with { Id=pair.Old.Id };
                        blocked.Remove(pair.Index);
                    }
                }
            }
        }
        // A matched older row gives an actual scrolling displacement. This
        // is better evidence than the requested drag percentage, as TikTok may
        // accelerate a short drag or snap to the nearest row boundary.
        var oldById=_before.ToDictionary(x=>x.Id);
        var motions=new List<int>();
        for(int i=0;i<ids.Count;i++)
        {
            int id=aliases.TryGetValue(i,out var restored)?restored:ids[i];
            if(!oldById.TryGetValue(id,out var previous) || !cache.ContainsKey(id))continue;
            int dy=previous.Tile.Y-tiles[i].Y;
            if(dy>=25 && dy<=Math.Max(430,viewportMotionLimit(tiles)))motions.Add(dy);
        }
        int shift=motions.Count==0?0:motions.OrderBy(x=>x).ElementAt(motions.Count/2);
        MeasuredShiftPixels=shift;
        int viewportBottom=tiles.Count==0?0:Math.Max(tiles.Max(t=>t.Bottom),
            _before.Count==0?0:_before.Max(o=>o.Tile.Bottom));
        // Only after an independently verified top-row overlap may the
        // two lower-row tiles be tested as entries exposed by the scroll.
        var currentTop=Enumerable.Range(0,tiles.Count).OrderBy(i=>tiles[i].Y).Take(2).ToHashSet();
        bool confirmedOverlap=tiles.Count==4 && _before.Count==4 &&
            currentTop.Count(i=>aliases.ContainsKey(i) || oldById.ContainsKey(ids[i]))==2;
        var previousIds=_before.Select(o=>o.Id).ToHashSet();
        var novel=new HashSet<int>();
        foreach(int i in indices)
        {
            if(aliases.ContainsKey(i) || blocked.Contains(i))continue;
            // A previously pending tile matching its old ID does not become a
            // NEW room merely because it has been seen for a second time.
            if(oldById.ContainsKey(ids[i])){blocked.Add(i);continue;}
            if(confirmedOverlap && !currentTop.Contains(i) &&
                !previousIds.Contains(ids[i]))
            {
                // Require a different preview from the prior four cards.
                // A dynamic video is still only provisional without stable overlap.
                double closest=_before.Where(o=>SameColumn(o.Tile,tiles[i]))
                    .Select(o=>Distance(o.Appearance,_previous[i].Appearance))
                    .DefaultIfEmpty(1).Min();
                if(closest>=0.15) { novel.Add(i); continue; }
            }
            if(shift<25)
            {
                // A long swipe can replace the entire viewport, so there is
                // no shared tile from which to estimate a numeric displacement.
                // In that case a lower-row-only rule incorrectly blocks new
                // rooms appearing in the upper row. Compare with the entire
                // recent preview history, including provisional observations.
                // This is a conservative candidate check, not an assertion
                // that differing viewer numbers by themselves identify rooms.
                var current=cards[i];
                double nearestEver=_recentHistory
                    .Where(old=>SameColumn(old.Tile,tiles[i]))
                    .Select(old=>Distance(old.Appearance,_previous[i].Appearance))
                    .DefaultIfEmpty(1).Min();
                bool stablePersonOcr=current.Viewers.HasValue &&
                    current.ViewerEvidence is ("Verified" or "OcrRepeatedCandidate");
                bool notKnownAudience=current.Viewers.HasValue &&
                    !cache.Values.Any(old=>old.Viewers.HasValue &&
                        Math.Abs(old.Viewers.Value-current.Viewers.Value)<=
                        Math.Max(3,(int)Math.Round(old.Viewers.Value*0.06)));
                bool sameScreenDuplicate=Enumerable.Range(0,cards.Count).Any(j=>
                    j!=i && j<tiles.Count && SameColumn(tiles[j],tiles[i]) &&
                    cards[j].Viewers is int otherViewers && current.Viewers is int currentViewers &&
                    Math.Abs(otherViewers-currentViewers)<=3 &&
                    Distance(_previous[j].Appearance,_previous[i].Appearance)<0.13);
                bool previewDistinct=nearestEver>=0.18 && !sameScreenDuplicate;
                // The tracking result must also show the viewport changed.
                // A preview change alone is insufficient because LIVE video
                // thumbnails animate while staying in the same room.
                bool completelyReplaced=_before.Count>0 &&
                    !_previous.Any(now=>_before.Any(old=>old.Id==now.Id));
                if(completelyReplaced)LongSwipeCandidates++;
                if(completelyReplaced && stablePersonOcr && notKnownAudience &&
                   previewDistinct)
                {
                    LongSwipeAccepted++;
                    novel.Add(i);
                    continue;
                }
                blocked.Add(i);
                continue;
            }
            var t=tiles[i];
            // Only accept a card which entered in the region exposed at the
            // BOTTOM of a genuinely upward-moving list; cropped top cards
            // cannot be certified as new.
            bool bottomEntry=t.Y >= viewportBottom-shift-26;
            // A similar preview in the preceding viewport could be the SAME
            // room with an unreadable badge, even if the OCR number changed.
            double nearest=_before.Where(x=>SameColumn(x.Tile,t))
                .Select(x=>Distance(x.Appearance,_previous[i].Appearance))
                .DefaultIfEmpty(1).Min();
            bool distinct=nearest>=0.19;
            if(bottomEntry && distinct) novel.Add(i);
            else blocked.Add(i);
        }
        // Prevent a PENDING ID from leaking into the NEW set on future pages.
        // The caller maintains all provisional IDs until they can be resolved.
        unresolved=blocked;
        provenNew=novel;
        return aliases;
    }

}

/// <summary>
/// Stage 2 only: TikTok Search => LIVE tab => read GridView cards, scroll, switch keyword.
/// No LIVE joining, no comments, no automated public interaction.
/// </summary>
public sealed class LiveSearchEngine(AppiumSession session,LiveManagerSettings config,
    Action<string> log,Action<LiveSearchCard> onCard,AdbClient adb,string deviceSerial)
{
    // Both Search layouts are in circulation. The new Cloud Phone uses
    // ka6 -> hwj -> tv_search_textview; keep k_g/hvp for existing devices.
    static readonly UiLocator[] SearchButtons=[
        new("id","com.ss.android.ugc.trill:id/ka6"),
        new("xpath","//android.widget.ImageView[@resource-id='com.ss.android.ugc.trill:id/ka6']"),
        new("id","com.ss.android.ugc.trill:id/k_g"),
        new("xpath","//android.widget.ImageView[@resource-id='com.ss.android.ugc.trill:id/k_g']")
    ];
    static readonly UiLocator[] SearchInputs=[
        new("id","com.ss.android.ugc.trill:id/hwj"),
        new("xpath","//android.widget.EditText[@resource-id='com.ss.android.ugc.trill:id/hwj']"),
        new("id","com.ss.android.ugc.trill:id/hvp"),
        new("xpath","//android.widget.EditText[@resource-id='com.ss.android.ugc.trill:id/hvp']")
    ];
    static readonly UiLocator[] SearchSubmit=[
        new("id","com.ss.android.ugc.trill:id/tv_search_textview"),
        new("xpath","//android.widget.Button[@resource-id='com.ss.android.ugc.trill:id/tv_search_textview']")
    ];
    static readonly UiLocator SystemNotificationPermissionDeny=new("xpath",
        "//android.widget.Button[@resource-id=\"com.android.permissioncontroller:id/permission_deny_and_dont_ask_again_button\" or @resource-id=\"com.android.permissioncontroller:id/permission_deny_button\"]");
    // Only click the LIVE search CATEGORY tab, never a LIVE label on a video,
    // room, card or badge. This exact parent/child XPath was read in Inspector.
    static readonly UiLocator[] LiveResultsTab=[
        new("xpath","//android.widget.FrameLayout[@content-desc='LIVE']/android.widget.TextView[@text='LIVE']")
    ];
    readonly record struct LiveTabPosition(int X,int Y,bool Selected,string Method);
    readonly record struct LiveSearchTabStrip(int X,int Y,int Width,int Height)
    {
        public int Right => X + Width;
    }
    // The category strip can be horizontally scrolled. On smaller devices
    // LIVE is offscreen until the user drags the q1m bar to the left.
    // Work ONLY within the horizontal Search category bar, not over a video.
    static LiveSearchTabStrip? FindLiveSearchTabStrip(string xml)
    {
        try
        {
            var nodes=XDocument.Parse(xml).Descendants().ToList();
            foreach(var node in nodes.Where(n =>
                n.Name.LocalName=="android.widget.HorizontalScrollView" &&
                string.Equals((string?)n.Attribute("resource-id"),
                    "com.ss.android.ugc.trill:id/q1m",StringComparison.Ordinal)))
            {
                if(string.Equals((string?)node.Attribute("displayed"),"false",StringComparison.OrdinalIgnoreCase))
                    continue;
                // Inspector XPath: //HorizontalScrollView[@resource-id='...:q1m']/LinearLayout
                if(!node.Elements().Any(n=>n.Name.LocalName=="android.widget.LinearLayout"))continue;
                var b=ReadBounds(node);
                if(b.HasValue && b.Value.Width>=170 && b.Value.Height>=22 &&
                   b.Value.Y>=0 && b.Value.Y<350)
                    return new LiveSearchTabStrip(b.Value.X,b.Value.Y,b.Value.Width,b.Value.Height);
            }
        }
        catch(System.Xml.XmlException) { /* transient Android hierarchy */ }
        return null;
    }
    static string SearchTabStripSignature(string xml)
    {
        try
        {
            var node=XDocument.Parse(xml).Descendants().FirstOrDefault(n=>
                n.Name.LocalName=="android.widget.HorizontalScrollView" &&
                string.Equals((string?)n.Attribute("resource-id"),
                    "com.ss.android.ugc.trill:id/q1m",StringComparison.Ordinal));
            if(node is null)return "missing";
            return string.Join("|",node.Descendants().Where(n=>
                n.Name.LocalName=="android.widget.TextView" &&
                !string.IsNullOrWhiteSpace((string?)n.Attribute("text")))
                .Select(n=>$"{(string?)n.Attribute("text")}:{(string?)n.Attribute("bounds")}:{(string?)n.Attribute("displayed")}"));
        }
        catch(System.Xml.XmlException) { return "invalid"; }
    }
    static (int X,int Y,int Width,int Height)? ReadBounds(XElement node)
    {
        var b=Regex.Match((string?)node.Attribute("bounds")??"",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
        if(!b.Success)return null;
        int x=int.Parse(b.Groups[1].Value,CultureInfo.InvariantCulture);
        int y=int.Parse(b.Groups[2].Value,CultureInfo.InvariantCulture);
        int right=int.Parse(b.Groups[3].Value,CultureInfo.InvariantCulture);
        int bottom=int.Parse(b.Groups[4].Value,CultureInfo.InvariantCulture);
        return right>x && bottom>y ? (x,y,right-x,bottom-y) : null;
    }
    static bool IsUiText(XElement e,string label)=>
        string.Equals((string?)e.Attribute("text"),label,StringComparison.OrdinalIgnoreCase);
    static bool IsSearchResultsSiblingTab(XElement node,int tabY)
    {
        if(!new[]{"Top","Users","Videos","Video","Photos","Shop","Sounds","Tất cả","Người dùng"}
            .Any(text=>IsUiText(node,text)))return false;
        var rect=ReadBounds(node);
        return rect.HasValue && Math.Abs(rect.Value.Y-tabY)<=45 &&
               !string.Equals((string?)node.Attribute("displayed"),"false",StringComparison.OrdinalIgnoreCase);
    }
    // LIVE results may contain many labels 'LIVE'. Accept only a true search
    // tab near the TOP next to another search category (Top/Users/etc).
    // A selected tab is verified through @selected, never GridView alone.
    static LiveTabPosition? FindLiveResultsTab(string xml)
    {
        try
        {
            var document=XDocument.Parse(xml);
            var nodes=document.Descendants().ToList();
            int screenH=int.TryParse((string?)document.Root?.Attribute("height"),
                NumberStyles.Integer,CultureInfo.InvariantCulture,out var height)?height:0;
            if(screenH<300)
                screenH=nodes.Select(ReadBounds).Where(b=>b.HasValue)
                    .Select(b=>b!.Value.Y+b.Value.Height).DefaultIfEmpty(0).Max();
            if(screenH<300)return null;
            static bool Visible(XElement node)=>
                !string.Equals((string?)node.Attribute("displayed"),"false",StringComparison.OrdinalIgnoreCase);
            static bool Selected(XElement node)=>
                string.Equals((string?)node.Attribute("selected"),"true",StringComparison.OrdinalIgnoreCase);
            foreach(var frame in nodes.Where(n=>
                n.Name.LocalName=="android.widget.FrameLayout" &&
                (string?)n.Attribute("content-desc")=="LIVE" && Visible(n)))
            {
                var child=frame.Elements().FirstOrDefault(n=>
                    n.Name.LocalName=="android.widget.TextView" && IsUiText(n,"LIVE") && Visible(n));
                if(child is null)continue;
                var rect=ReadBounds(frame);
                if(!rect.HasValue)continue;
                var r=rect.Value;
                if(r.Y>screenH*32/100 || r.Y<0)continue;
                // Offscreen tabs may still be exposed in source, sometimes even
                // with displayed=true. Never tap their stale/beyond-screen bounds.
                var strip=FindLiveSearchTabStrip(xml);
                if(strip.HasValue && (r.X<strip.Value.X+2 ||
                   r.X+r.Width>strip.Value.Right-2))continue;
                if(!nodes.Any(n=>IsSearchResultsSiblingTab(n,r.Y)))continue;
                return new LiveTabPosition(r.X+r.Width/2,r.Y+r.Height/2,
                    Selected(frame)||Selected(child),"FrameLayout/LIVE/TextView");
            }
            // Legacy tab layout: standalone LIVE TextView alongside Top/Users
            // in the search header. This is not a global @text='LIVE' locator.
            foreach(var textNode in nodes.Where(n=>
                n.Name.LocalName=="android.widget.TextView" && IsUiText(n,"LIVE") && Visible(n)))
            {
                var rect=ReadBounds(textNode);
                if(!rect.HasValue)continue;
                var r=rect.Value;
                if(r.Y>screenH*32/100)continue;
                var strip=FindLiveSearchTabStrip(xml);
                if(strip.HasValue && (r.X<strip.Value.X+2 ||
                   r.X+r.Width>strip.Value.Right-2))continue;
                if(!nodes.Any(n=>IsSearchResultsSiblingTab(n,r.Y)))continue;
                return new LiveTabPosition(r.X+r.Width/2,r.Y+r.Height/2,
                    Selected(textNode)||Selected(textNode.Parent??textNode),"LegacyHeader/TextView");
            }
        }
        catch(System.Xml.XmlException) { /* transient Appium source during transition */ }
        return null;
    }
    static bool IsLiveSearchGridReady(string xml)=>
        FindLiveResultsTab(xml) is { Selected:true } &&
        xml.Contains("android.widget.GridView",StringComparison.Ordinal);
    // LIVE Search can start on Edit profile, Gallery, LIVE or another sub-page.
    // Recover a TikTok-owned Home screen before looking for the Search control.
    // Each Back is bounded and followed by a FRESH UI snapshot; never back
    // out of login, Google OAuth, onboarding or a security challenge.
    const int MaxSearchNavigationBack=5;
    bool searchNavigationBlocked;
    string searchNavigationStopReason="";
    static readonly UiLocator[] SearchRecoveryBack=[
        new("xpath","//android.widget.Button[@content-desc='Back to previous screen']")
    ];
    static bool HasSearchResourceInXml(string xml,string resource)=>xml.Contains(
        "resource-id=\"com.ss.android.ugc.trill:id/"+resource+"\"",StringComparison.Ordinal);
    static bool HasSearchInputInXml(string xml)=>
        HasSearchResourceInXml(xml,"hwj") || HasSearchResourceInXml(xml,"hvp");
    static bool HasSearchButtonInXml(string xml)=>
        HasSearchResourceInXml(xml,"ka6") || HasSearchResourceInXml(xml,"k_g");
    static string SearchButtonDetected(string xml)=>
        HasSearchResourceInXml(xml,"ka6")?"ka6":
        HasSearchResourceInXml(xml,"k_g")?"k_g":"none";
    static string SearchInputDetected(string xml)=>
        HasSearchResourceInXml(xml,"hwj")?"hwj":
        HasSearchResourceInXml(xml,"hvp")?"hvp":"none";
    // Log only a selector that Appium actually clicked, not merely one
    // whose resource id appeared in the last XML snapshot.
    async Task<bool> ClickSearchButtonAsync(string xml,CancellationToken ct)
    {
        if(HasSearchResourceInXml(xml,"ka6") &&
           await session.ClickVisibleAnyAsync(SearchButtons.Take(2),ct))
        {
            log("LIVE_SEARCH_BUTTON_CLICKED: id=ka6");
            return true;
        }
        if(HasSearchResourceInXml(xml,"k_g") &&
           await session.ClickVisibleAnyAsync(SearchButtons.Skip(2),ct))
        {
            log("LIVE_SEARCH_BUTTON_CLICKED: id=k_g");
            return true;
        }
        return false;
    }

    // Only a BOTTOM-LEFT Home control is actionable. A label from a video,
    // popup or another tab must never be treated as Home navigation.
    static (int X,int Y)? FindBottomHomeCenter(string xml)
    {
        try
        {
            var nodes=XDocument.Parse(xml).Descendants().ToList();
            int screenRight=0,screenBottom=0;
            foreach(var node in nodes)
            {
                var matches=Regex.Match((string?)node.Attribute("bounds")??"",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
                if(!matches.Success)continue;
                screenRight=Math.Max(screenRight,int.Parse(matches.Groups[3].Value,CultureInfo.InvariantCulture));
                screenBottom=Math.Max(screenBottom,int.Parse(matches.Groups[4].Value,CultureInfo.InvariantCulture));
            }
            if(screenRight<150 || screenBottom<300)return null;
            foreach(var node in nodes)
            {
                if((string?)node.Attribute("displayed")=="false")continue;
                string label=((string?)node.Attribute("content-desc")??(string?)node.Attribute("text")??"").Trim();
                if(!label.Equals("Home",StringComparison.OrdinalIgnoreCase) &&
                   !label.Equals("Trang chủ",StringComparison.OrdinalIgnoreCase))continue;
                var bounds=Regex.Match((string?)node.Attribute("bounds")??"",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
                if(!bounds.Success)continue;
                int left=int.Parse(bounds.Groups[1].Value,CultureInfo.InvariantCulture);
                int top=int.Parse(bounds.Groups[2].Value,CultureInfo.InvariantCulture);
                int right=int.Parse(bounds.Groups[3].Value,CultureInfo.InvariantCulture);
                int bottom=int.Parse(bounds.Groups[4].Value,CultureInfo.InvariantCulture);
                int x=(left+right)/2,y=(top+bottom)/2;
                if(right>left && bottom>top && y>=screenBottom*75/100 &&
                   x<=screenRight*38/100 && x>=0)return (x,y);
            }
        }
        catch(System.Xml.XmlException) { /* wait for a complete hierarchy */ }
        return null;
    }
    internal static bool IsSearchRecoveryProtectedUi(string xml)
    {
        // In these states, Android Back can abandon an OAuth, 2FA, captcha,
        // consent or onboarding flow; leave it to the account worker.
        if(TikTokForegroundProbe.IsTikTokLoginMethodsScreen(xml) ||
           TikTokForegroundProbe.IsLoggedOutWelcomeBack(xml) ||
           TikTokForegroundProbe.IsOnboardingScreen(xml))return true;
        string[] blockingTexts=["Choose an account", "Sign in with Google", "Terms and Policies",
            "Tell us more about yourself", "Verify it's you", "verification code",
            "Verify your identity", "CAPTCHA", "Security verification"];
        return blockingTexts.Any(t=>xml.Contains(t,StringComparison.OrdinalIgnoreCase));
    }
    async Task SaveSearchNavigationDiagnosticsAsync(string xml,string keyword,string reason,CancellationToken ct)
    {
        try
        {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AndroidMultiDeviceV1","diagnostics",session.DeviceKey,"live-search-navigation-"+
                DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory,"screen.xml"),xml,ct);
            await File.WriteAllTextAsync(Path.Combine(directory,"reason.txt"),
                $"Keyword={keyword}{Environment.NewLine}Reason={reason}{Environment.NewLine}",ct);
            try { await File.WriteAllBytesAsync(Path.Combine(directory,"screen.png"),
                await session.ScreenshotPngAsync(ct),ct); }
            catch(OperationCanceledException){throw;}
            catch(Exception e){log($"LIVE_SEARCH_NAV_DIAGNOSTIC_SCREENSHOT_FAILED: {e.GetType().Name}");}
            log($"LIVE_SEARCH_NAV_DIAGNOSTIC_SAVED: {directory}");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {log($"LIVE_SEARCH_NAV_DIAGNOSTIC_FAILED: {e.GetType().Name}");}
    }
    async Task<bool> RecoverSearchEntryAsync(string keyword,CancellationToken ct)
    {
        string last="";
        int backs=0;
        bool homeTapped=false;
        // 5 Back gestures PLUS a final read after the fifth Back.
        for(int pass=0;pass<MaxSearchNavigationBack+3;pass++)
        {
            ct.ThrowIfCancellationRequested();
            string xml=await ReadUiAsync(ct);
            last=xml;
            if(TikTokNavigationRecovery.IsFindPeopleScreen(xml))
            {
                var nav=new TikTokNavigationRecovery(session,adb,deviceSerial,log);
                if(!await nav.RecoverFindPeopleAsync(ct,"SEARCH_NAVIGATION"))
                {
                    searchNavigationStopReason="FIND_PEOPLE_RECOVERY_FAILED";
                    break;
                }
                log("WORKFLOW_RESUMED: phase=LIVE_SEARCH; checkpoint=PHASE1; reason=FIND_PEOPLE");
                xml=await ReadUiAsync(ct);
                last=xml;
            }
            // This dialog ignores Android Back and hides the Home Search icon.
            // Dismiss it deliberately, then restart this pass without consuming
            // a Back attempt so the same Home screen is evaluated again.
            if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(session,log,ct,xml))
            {
                log($"LIVE_SEARCH_POLICY_RECOVERY_RETRY: popup dismissed; back={backs}/5; đọc lại Search icon.");
                homeTapped=false;
                continue;
            }
            var captcha=await LivePopupHandler.DismissVerifyCaptchaAsync(
                session,log,ct,xml,"PHASE1_SEARCH");
            if(captcha.Status==CaptchaDismissStatus.Dismissed)
            {
                log($"LIVE_SEARCH_CAPTCHA_RECOVERY_RETRY: X dismissed; back={backs}/5; đọc lại Search icon.");
                homeTapped=false;
                continue;
            }
            if(captcha.Status==CaptchaDismissStatus.Failed)
            {
                last=captcha.Source;
                searchNavigationStopReason="LOGIN_OR_SECURITY_OR_ONBOARDING";
                log("LIVE_SEARCH_CAPTCHA_FAILED: không dùng AndroidBack; yêu cầu FALLBACK_APP_RESTART.");
                break;
            }
            string package=await session.CurrentPackageAsync(ct);
            if(package!=TikTokForegroundProbe.TikTokPackage ||
               !TikTokForegroundProbe.SourceHasTikTokUi(xml))
            {
                searchNavigationStopReason=$"NOT_TIKTOK_FOREGROUND ({package})";
                break;
            }
            if(IsSearchRecoveryProtectedUi(xml))
            {
                searchNavigationStopReason="LOGIN_OR_SECURITY_OR_ONBOARDING";
                break;
            }
            // Already on the search form; do not navigate away.
            if(HasSearchInputInXml(xml))
            {log($"LIVE_SEARCH_RECOVERY_READY: input={SearchInputDetected(xml)}; back={backs}/5");return true;}
            // If Search is visible on a sub-page, first use a known Home tab
            // where available, instead of guessing what Search belongs to.
            var home=FindBottomHomeCenter(xml);
            if(home.HasValue && !homeTapped)
            {
                await session.TapUiPointAsync(home.Value.X,home.Value.Y,ct);
                homeTapped=true;
                log($"LIVE_SEARCH_HOME_TAP_REQUESTED: x={home.Value.X}; y={home.Value.Y}; back={backs}/5; chờ Home.");
                await DelayAsync(1100,ct);
                continue;
            }
            if(HasSearchButtonInXml(xml))
            {
                log($"LIVE_SEARCH_RECOVERY_READY: button={SearchButtonDetected(xml)}; homeTapped={homeTapped}; back={backs}/5");
                return true;
            }
            // A bottom Home control still exists after tapping it. There is
            // nothing safer to back into, since Back from Home exits TikTok.
            if(home.HasValue && homeTapped)
            {
                // Do not exit Home via Back. Search may appear after a slow
                // feed redraw; give it a bounded wait, then save diagnostics.
                bool ready=await WaitForAsync(
                    source=>HasSearchInputInXml(source) || HasSearchButtonInXml(source),8,ct);
                xml=await ReadUiAsync(ct);last=xml;
                if(ready && (HasSearchInputInXml(xml) || HasSearchButtonInXml(xml)))
                {
                    log($"LIVE_SEARCH_RECOVERY_READY: Home loaded; button={SearchButtonDetected(xml)}; input={SearchInputDetected(xml)}; back={backs}/5");
                    return true;
                }
                searchNavigationStopReason="HOME_SEARCH_BUTTON_NOT_FOUND";
                break;
            }
            if(backs>=MaxSearchNavigationBack)
            {searchNavigationStopReason="BACK_LIMIT_5";break;}
            bool xpathClicked=false;
            if(xml.Contains("content-desc=\"Back to previous screen\"",StringComparison.Ordinal))
                xpathClicked=await session.ClickVisibleAnyAsync(SearchRecoveryBack,ct);
            if(!xpathClicked) await session.BackAsync(ct);
            backs++;
            log($"LIVE_SEARCH_RECOVERY_BACK_REQUESTED: {backs}/5; method={(xpathClicked?"XPath":"AndroidBack")}; đọc lại UI.");
            await DelayAsync(950,ct);
        }
        if(string.IsNullOrWhiteSpace(searchNavigationStopReason))
            searchNavigationStopReason="RECOVERY_NOT_READY";
        searchNavigationBlocked=true;
        log($"LIVE_SEARCH_RECOVERY_STOP: {searchNavigationStopReason}; back={backs}/5; không click Search phỏng đoán.");
        if(last.Length>0)await SaveSearchNavigationDiagnosticsAsync(last,keyword,searchNavigationStopReason,ct);
        return false;
    }
    static async Task DelayAsync(int ms,CancellationToken ct)=>await Task.Delay(ms,ct);
    async Task HandleSystemPermissionPopupAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            string? deny=await session.FindAsync(SystemNotificationPermissionDeny,ct,2);
            if(deny is null)return;
            log("SYSTEM_PERMISSION_POPUP_DETECTED: Android notification permission; action=DENY.");
            await session.ClickElementAsync(deny,ct);
            await DelayAsync(650,ct);
            log("SYSTEM_PERMISSION_POPUP_DISMISSED: Don't allow clicked; continue keyword search.");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
        catch(Exception ex)
        {
            // The prompt is optional. A 2-second query timeout or a stale OS
            // element must not abort an otherwise valid keyword search.
            log($"SYSTEM_PERMISSION_POPUP_SKIPPED: {ex.GetType().Name}; continue search.");
        }
    }
    async Task<string> ReadUiAsync(CancellationToken ct)
    {
        var source=await session.SourceAsync(ct);
        if(TikTokForegroundProbe.IsTikTokNotificationPrompt(source))
        {
            log("TikTok hiển thị yêu cầu thông báo: chọn Don't allow.");
            bool dismissed=await TikTokForegroundProbe.DismissTikTokNotificationPromptAsync(session,source,ct,log);
            if(dismissed){await DelayAsync(1000,ct);source=await session.SourceAsync(ct);}
        }
        return source;
    }
    async Task<bool> WaitForAsync(Func<string,bool> ready,int timeoutSeconds,CancellationToken ct)
    {
        var until=DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while(DateTimeOffset.UtcNow<until)
        {
            ct.ThrowIfCancellationRequested();
            var source=await ReadUiAsync(ct);
            if(ready(source))return true;
            await DelayAsync(900,ct);
        }
        return false;
    }
    static bool HasGrid(string xml)=>xml.Contains("android.widget.GridView",StringComparison.Ordinal);
    async Task<bool> SearchOneKeywordAsync(string keyword,CancellationToken ct)
    {
        log($"Từ khóa: {keyword} — mở TikTok Search.");
        // A TikTok subpage may be left on screen by the previous room/phase.
        // Recover in the SAME worker; never rotate keywords while stranded.
        string initial=await ReadUiAsync(ct);
        if(TikTokNavigationRecovery.IsFindPeopleScreen(initial))
        {
            var nav=new TikTokNavigationRecovery(session,adb,deviceSerial,log);
            if(!await nav.RecoverFindPeopleAsync(ct,"LIVE_SEARCH_ENTRY"))
            {
                searchNavigationBlocked=true;
                searchNavigationStopReason="FIND_PEOPLE_RECOVERY_FAILED";
                log("LIVE_SEARCH_ENTRY_BLOCKED: Find people recovery exhausted; fail to session supervisor.");
                return false;
            }
            log("WORKFLOW_RESUMED: phase=LIVE_SEARCH; checkpoint=PHASE1; reason=FIND_PEOPLE");
            initial=await ReadUiAsync(ct);
        }
        // Handle a notification permission sheet even if it was already open
        // before the operator started Search; it can block the Search button.
        bool inputReady=HasSearchInputInXml(initial);
        if(!inputReady)
        {
            bool clicked=HasSearchButtonInXml(initial) &&
                await ClickSearchButtonAsync(initial,ct);
            if(clicked)await HandleSystemPermissionPopupAsync(ct);
            if(!clicked)
            {
                log("LIVE_SEARCH_ENTRY_MISSING: chưa thấy ka6/k_g/hwj/hvp; kiểm tra trang Home và thử Back tối đa 5 lần.");
                if(!await RecoverSearchEntryAsync(keyword,ct))return false;
                var recovered=await ReadUiAsync(ct);
                inputReady=HasSearchInputInXml(recovered);
                if(!inputReady)
                {
                    clicked=HasSearchButtonInXml(recovered) &&
                        await ClickSearchButtonAsync(recovered,ct);
                    if(clicked)await HandleSystemPermissionPopupAsync(ct);
                    if(!clicked)
                    {
                        searchNavigationBlocked=true;
                        searchNavigationStopReason="SEARCH_BUTTON_NOT_CLICKABLE_AFTER_RECOVERY";
                        log("LIVE_SEARCH_RECOVERY_STOP: tìm lại ka6/k_g nhưng không click được; không chuyển sang từ khóa khác.");
                        await SaveSearchNavigationDiagnosticsAsync(recovered,keyword,searchNavigationStopReason,ct);
                        return false;
                    }
                }
            }
        }
        if(!await WaitForAsync(HasSearchInputInXml,20,ct))
        {log("LIVE_SEARCH_INPUT_NOT_FOUND: chưa xuất hiện hwj/hvp sau khi nhấn Search.");return false;}
        var searchFormXml=await ReadUiAsync(ct);
        log($"LIVE_SEARCH_INPUT_READY: id={SearchInputDetected(searchFormXml)}");
        await session.ClearAnyAsync(SearchInputs,ct);
        if(!await session.SetTextAnyAsync(SearchInputs,keyword,ct))
        {log("LIVE_SEARCH_INPUT_FAILED: không nhập được vào ô tìm kiếm hwj/hvp.");return false;}
        await HandleSystemPermissionPopupAsync(ct);
        await DelayAsync(500,ct);
        if(!await session.ClickVisibleAnyAsync(SearchSubmit,ct))
        {log("Không tìm được nút Search Submit (tv_search_textview).");return false;}
        await HandleSystemPermissionPopupAsync(ct);
        // LIVE can be horizontally offscreen to the right of Sounds/Shop.
        // Only scroll the q1m Search CATEGORY bar; not the results/video grid.
        LiveTabPosition? liveTab=null;
        string tabsXml=await ReadUiAsync(ct);
        const int maxCategorySwipes=3;
        int categorySwipes=0;
        // Give the results bar a brief chance to materialize after Submit.
        for(int readyAttempt=0;readyAttempt<3 &&
            FindLiveResultsTab(tabsXml) is null &&
            FindLiveSearchTabStrip(tabsXml) is null;readyAttempt++)
        {
            await DelayAsync(900,ct);
            tabsXml=await ReadUiAsync(ct);
        }
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            liveTab=FindLiveResultsTab(tabsXml);
            if(liveTab.HasValue)break;
            var strip=FindLiveSearchTabStrip(tabsXml);
            if(!strip.HasValue || categorySwipes>=maxCategorySwipes ||
               IsSearchRecoveryProtectedUi(tabsXml))break;
            categorySwipes++;
            string before=SearchTabStripSignature(tabsXml);
            log($"LIVE_SEARCH_TAB_BAR_SWIPE_REQUESTED: {categorySwipes}/{maxCategorySwipes}; " +
                $"resource=q1m; direction=LEFT; bounds=[{strip.Value.X},{strip.Value.Y},"+
                $"{strip.Value.Width},{strip.Value.Height}].");
            await session.SwipeLiveSearchCategoryTabsLeftAsync(
                strip.Value.X,strip.Value.Y,strip.Value.Width,strip.Value.Height,ct);
            await DelayAsync(850,ct);
            tabsXml=await ReadUiAsync(ct);
            string after=SearchTabStripSignature(tabsXml);
            log($"LIVE_SEARCH_TAB_BAR_SWIPE_CHECK: {categorySwipes}/{maxCategorySwipes}; " +
                $"changed={before!=after}; tabVisible={FindLiveResultsTab(tabsXml).HasValue}.");
        }
        if(!liveTab.HasValue)
        {
            string reason=FindLiveSearchTabStrip(tabsXml) is null?
                "LIVE_SEARCH_TAB_BAR_NOT_FOUND":"LIVE_SEARCH_TAB_NOT_FOUND_AFTER_HORIZONTAL_SWIPES";
            searchNavigationBlocked=true;
            searchNavigationStopReason=reason;
            log($"LIVE_SEARCH_TAB_NOT_FOUND: {reason}; horizontalSwipes={categorySwipes}/{maxCategorySwipes}; " +
                "không chọn video từ Top.");
            await SaveSearchNavigationDiagnosticsAsync(tabsXml,keyword,searchNavigationStopReason,ct);
            return false;
        }
        return await SelectAndConfirmLiveTabAsync(liveTab.Value,keyword,ct);
    }

    async Task<bool> SelectAndConfirmLiveTabAsync(LiveTabPosition tab,string keyword,CancellationToken ct)
    {
        log($"LIVE_SEARCH_TAB_FOUND: method={tab.Method}; selected={tab.Selected}; x={tab.X}; y={tab.Y}");
        if(!tab.Selected)
        {
            // The exact LIVE Search category tab has ALREADY been verified from
            // fresh Appium XML by FindLiveResultsTab (header position, visible,
            // nearby sibling tabs, and containment in q1m when available).
            // Do not perform FindElement -> Displayed -> ElementClick again:
            // those are 3 extra UiAutomator2 RPCs on high-latency cloud phones.
            // A single coordinate tap uses the proven tab center; the existing
            // selected=true + GridView assertion below remains mandatory.
            if(tab.X<=0 || tab.Y<=0)
                throw new InvalidOperationException("LIVE Search tab không có tọa độ hợp lệ; từ chối nhấn phỏng đoán.");
            log($"LIVE_SEARCH_TAB_TAP_REQUESTED: method=VerifiedTabBoundsFast; x={tab.X}; y={tab.Y}; " +
                "skip=FindElement,Displayed,ElementClick; chờ selected=true + GridView.");
            await session.TapUiPointAsync(tab.X,tab.Y,ct);
        }
        // The LIVE header tab (selected=true) AND GridView must both appear.
        // A Top-page video containing the word LIVE is never sufficient.
        bool selected=await WaitForAsync(xml=>IsLiveSearchGridReady(xml),25,ct);
        if(!selected)
        {
            string xml=await ReadUiAsync(ct);
            searchNavigationBlocked=true;
            searchNavigationStopReason="LIVE_TAB_NOT_CONFIRMED";
            log($"LIVE_SEARCH_TAB_NOT_CONFIRMED: tabSelected={FindLiveResultsTab(xml)?.Selected}; grid={HasGrid(xml)}; không quét nhầm Top hoặc phòng LIVE.");
            await SaveSearchNavigationDiagnosticsAsync(xml,keyword,searchNavigationStopReason,ct);
            return false;
        }
        log("LIVE_SEARCH_TAB_SELECTED: selected=true; grid=True; category=LIVE.");
        log($"Đã vào kết quả LIVE cho “{keyword}”.");
        return true;
    }
    // Missing the LIVE CATEGORY in Search is sometimes a temporary TikTok UI
    // condition: Top/Videos/Photos may load without LIVE even after scrolling q1m.
    // Do not treat a thumbnail's red LIVE badge as a selectable Search tab.
    // Four TOTAL attempts = the initial Search + up to three app restarts.
    // All attempts share the current keyword/overall time budget and cancellation.
    const int MaxLiveTabSearchAttempts=4;
    static bool IsMissingLiveCategory(string reason)=>reason is
        "LIVE_SEARCH_TAB_BAR_NOT_FOUND" or "LIVE_SEARCH_TAB_NOT_FOUND_AFTER_HORIZONTAL_SWIPES";

    async Task<bool> RelaunchTikTokForMissingLiveTabAsync(string keyword,int completedAttempt,
        CancellationToken ct)
    {
        const string tiktokPackage=TikTokForegroundProbe.TikTokPackage;
        string ui="";
        try
        {
            ct.ThrowIfCancellationRequested();
            string package=await session.CurrentPackageAsync(ct);
            ui=await ReadUiAsync(ct);
            if(package!=tiktokPackage || IsSearchRecoveryProtectedUi(ui))
            {
                searchNavigationBlocked=true;
                searchNavigationStopReason=package!=tiktokPackage?
                    "LIVE_TAB_RESTART_FOREGROUND_NOT_TIKTOK":
                    "LIVE_TAB_RESTART_PROTECTED_UI";
                log($"LIVE_SEARCH_APP_RESTART_BLOCKED: attempt={completedAttempt}/{MaxLiveTabSearchAttempts}; " +
                    $"reason={searchNavigationStopReason}; foreground={package}; không đóng OAuth/CAPTCHA/Login.");
                await SaveSearchNavigationDiagnosticsAsync(ui,keyword,searchNavigationStopReason,ct);
                return false;
            }
            // Never clear TikTok data, credentials, or the account vault.
            // Restart the SAME ADB device, keeping the Appium session alive.
            log($"LIVE_SEARCH_APP_RESTART_REQUESTED: afterAttempt={completedAttempt}/{MaxLiveTabSearchAttempts}; " +
                "ADB force-stop TikTok (không xóa dữ liệu).");
            await adb.RunAsync(ct,"-s",deviceSerial,"shell","am","force-stop",tiktokPackage);
            await DelayAsync(850,ct);
            string launch=await adb.LaunchTikTokAsync(deviceSerial,ct);
            if(launch.Contains("Events injected: 0",StringComparison.OrdinalIgnoreCase) ||
               launch.Contains("monkey aborted",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ADB monkey không khởi động được TikTok.");
            log($"LIVE_SEARCH_APP_RELAUNCH_REQUESTED: nextAttempt={completedAttempt+1}/{MaxLiveTabSearchAttempts}; " +
                "chờ TikTok trở lại foreground.");
            for(int check=1;check<=12;check++)
            {
                ct.ThrowIfCancellationRequested();
                await DelayAsync(check==1?1450:1150,ct);
                string foreground=await session.CurrentPackageAsync(ct);
                ui=await ReadUiAsync(ct);
                if(IsSearchRecoveryProtectedUi(ui))
                {
                    searchNavigationBlocked=true;
                    searchNavigationStopReason="LIVE_TAB_RESTART_PROTECTED_UI";
                    log("LIVE_SEARCH_APP_RESTART_BLOCKED: Login/CAPTCHA/Onboarding xuất hiện sau mở lại; " +
                        "dừng để xử lý thủ công.");
                    await SaveSearchNavigationDiagnosticsAsync(ui,keyword,searchNavigationStopReason,ct);
                    return false;
                }
                if(foreground==tiktokPackage && TikTokForegroundProbe.SourceHasTikTokUi(ui))
                {
                    log($"LIVE_SEARCH_APP_RELAUNCH_READY: nextAttempt={completedAttempt+1}/{MaxLiveTabSearchAttempts}; " +
                        $"check={check}/12; trở lại luồng Search từ đầu.");
                    return true;
                }
            }
            searchNavigationBlocked=true;
            searchNavigationStopReason="LIVE_TAB_RESTART_UI_NOT_READY";
            log("LIVE_SEARCH_APP_RELAUNCH_FAILED: sau khi mở lại, chưa thấy giao diện TikTok sẵn sàng.");
            await SaveSearchNavigationDiagnosticsAsync(ui,keyword,searchNavigationStopReason,ct);
            return false;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(ex is InvalidOperationException or TimeoutException or System.IO.IOException)
        {
            searchNavigationBlocked=true;
            searchNavigationStopReason="LIVE_TAB_RESTART_ADB_OR_APPIUM_ERROR";
            string message=ex.Message.Replace('\r',' ').Replace('\n',' ');
            if(message.Length>220)message=message[..220];
            log($"LIVE_SEARCH_APP_RESTART_FAILED: {ex.GetType().Name}: {message}; dừng an toàn.");
            if(ui.Length>0)await SaveSearchNavigationDiagnosticsAsync(ui,keyword,searchNavigationStopReason,ct);
            throw new InvalidOperationException(
                "TikTok/Appium recovery failed while locating the LIVE tab.",ex);
        }
    }

    async Task<bool> SearchOneKeywordWithMissingTabRetriesAsync(string keyword,CancellationToken ct)
    {
        for(int attempt=1;attempt<=MaxLiveTabSearchAttempts;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            searchNavigationBlocked=false;
            searchNavigationStopReason="";
            log($"LIVE_SEARCH_FLOW_ATTEMPT: {attempt}/{MaxLiveTabSearchAttempts}; keyword=“{keyword}”.");
            if(await SearchOneKeywordAsync(keyword,ct))
            {
                log($"LIVE_SEARCH_FLOW_SUCCESS: attempt={attempt}/{MaxLiveTabSearchAttempts}; " +
                    "đã xác nhận tab LIVE, không đóng/mở TikTok thêm.");
                return true;
            }
            // The restart policy applies ONLY to a genuinely missing LIVE tab
            // after all q1m horizontal swipes, not to Login, Search-entry errors,
            // or a tab that exists but cannot be verified as selected.
            if(!searchNavigationBlocked || !IsMissingLiveCategory(searchNavigationStopReason))return false;
            if(attempt==MaxLiveTabSearchAttempts)
            {
                searchNavigationStopReason="LIVE_TAB_MISSING_AFTER_4_ATTEMPTS";
                log("LIVE_SEARCH_RETRY_LIMIT_REACHED: 4/4 lần tìm kiếm không thấy tab LIVE; " +
                    "đã thử vuốt ngang ở mỗi lần; dừng, không click video từ Top.");
                return false;
            }
            // A slow network can reveal the LIVE tab a moment after the last
            // horizontal swipe. Read a fresh source BEFORE force-stop and, if
            // it has appeared, select it without restarting TikTok at all.
            await DelayAsync(900,ct);
            string latest=await ReadUiAsync(ct);
            var lateTab=FindLiveResultsTab(latest);
            if(lateTab.HasValue)
            {
                log($"LIVE_SEARCH_TAB_LATE_APPEARED: attempt={attempt}/{MaxLiveTabSearchAttempts}; " +
                    "đã thấy tab LIVE trước khi restart; không đóng TikTok.");
                if(await SelectAndConfirmLiveTabAsync(lateTab.Value,keyword,ct))
                {
                    log($"LIVE_SEARCH_FLOW_SUCCESS: attempt={attempt}/{MaxLiveTabSearchAttempts}; " +
                        "LIVE xuất hiện trễ; không cần mở lại ứng dụng.");
                    return true;
                }
                return false; // visible-but-unconfirmed is NOT a missing-tab case
            }
            log($"LIVE_SEARCH_MISSING_TAB_RETRY: {attempt}/{MaxLiveTabSearchAttempts}; " +
                "đã vuốt ngang nhưng không thấy tab LIVE; đóng/mở lại TikTok rồi tìm lại từ đầu.");
            if(!await RelaunchTikTokForMissingLiveTabAsync(keyword,attempt,ct))return false;
        }
        return false;
    }

    // Read until 3 matching card-tree snapshots. A slow scroll alone is not
    // evidence that lazy-loaded UI is ready. No scroll occurs in this method.
    async Task<(string source,IReadOnlyList<LiveSearchCard> cards,bool stable)> ReadSettledAsync(
        string keyword,CancellationToken ct)
    {
        string last="";
        string source="";
        IReadOnlyList<LiveSearchCard> cards=[];
        int stableReads=0;
        for(int attempt=0;attempt<9;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            source=await ReadUiAsync(ct);
            cards=LiveSearchCardReader.Read(source,keyword);
            // Viewer numbers can change during a LIVE. Do not interpret an
            // updated audience count as a moving/loading card grid.
            string signature=string.Join(";",cards.Select(c=>
                c.Fingerprint.StartsWith("chua-ro-card-",StringComparison.Ordinal)
                    ? "unlabelled-tile"
                    : c.Fingerprint+":"+c.Title));
            if(cards.Count>0 && signature==last)stableReads++;
            else stableReads=cards.Count>0?1:0;
            if(stableReads>=3)return (source,cards,true);
            last=signature;
            await DelayAsync(1500,ct);
        }
        return (source,cards,false);
    }

    // Save the EXACT screenshot for the XML viewport. Do not ship it anywhere.
    // Source and screenshot can contain personal information; the operator
    // should review them before sharing for debugging.
    async Task SaveViewerDiagnosticsAsync(string keyword,string xml,CancellationToken ct)
    {
        string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidMultiDeviceV1","diagnostics",session.DeviceKey);
        try
        {
            Directory.CreateDirectory(dir);
            var xmlPath=Path.Combine(dir,"live-search-last-ui.xml");
            var pngPath=Path.Combine(dir,"live-search-last-screen.png");
            var txtPath=Path.Combine(dir,"live-search-last-diagnosis.txt");
            await File.WriteAllTextAsync(xmlPath,xml,ct);
            await File.WriteAllTextAsync(txtPath,
                $"Keyword: {keyword}{Environment.NewLine}" +
                LiveSearchCardReader.Diagnose(xml)+Environment.NewLine,ct);
            try
            {
                byte[] screenshot=await session.ScreenshotPngAsync(ct);
                await File.WriteAllBytesAsync(pngPath,screenshot,ct);
                log($"Chẩn đoán LIVE: XML={xmlPath}; Ảnh={pngPath}; Chi tiết={txtPath}.");
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex)
            {
                log($"Chẩn đoán LIVE: đã lưu XML nhưng chưa lấy được screenshot ({ex.GetType().Name}).");
            }
            log("Các file chỉ lưu trên PC; kiểm tra thông tin riêng tư trước khi chia sẻ.");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            log($"Chẩn đoán LIVE: không ghi được tệp ({ex.GetType().Name}).");
        }
    }

    static async Task SaveViewportSnapshotAsync(string directory,int page,
        string xml,byte[] png,IReadOnlyList<LiveSearchTile> tiles,
        IReadOnlyList<int> ids,IReadOnlyList<LiveSearchCard> cards,
        IReadOnlySet<int> fresh,IReadOnlySet<int> pending,CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        string prefix=$"viewport-{page+1:D2}";
        await File.WriteAllBytesAsync(Path.Combine(directory,prefix+".png"),png,ct);
        await File.WriteAllTextAsync(Path.Combine(directory,prefix+".xml"),xml,ct);
        var entries=Enumerable.Range(0,Math.Min(tiles.Count,cards.Count))
            .Select(i=>new {slot=i+1,trackId=ids[i],x=tiles[i].X,y=tiles[i].Y,
                width=tiles[i].Width,height=tiles[i].Height,
                state=fresh.Contains(i)?"NEW":pending.Contains(i)?"PENDING":"SEEN",
                viewer=cards[i].Viewers,evidence=cards[i].ViewerEvidence,
                title=cards[i].Title,creator=cards[i].Creator});
        await File.WriteAllTextAsync(Path.Combine(directory,prefix+".json"),
            JsonSerializer.Serialize(new {viewport=page+1,cards=entries},
                new JsonSerializerOptions{WriteIndented=true}),ct);
    }

    // Phase 3: read only visible UI metadata. Do not infer a room ID from the host name.
    static readonly UiLocator[] LiveAudience=[new("xpath",
        "//android.widget.TextView[@resource-id='com.ss.android.ugc.trill:id/tv_online_audience_num']")];
    // Support both the updated LIVE host avatar (k1s) and the older layout (k13).
    // Never use a generic ImageView: it could open an unrelated viewer's profile.
    static readonly UiLocator[] LiveHostOpen=[
        new("xpath","//android.widget.ImageView[@resource-id='com.ss.android.ugc.trill:id/k1s']"),
        new("xpath","//android.widget.ImageView[@resource-id='com.ss.android.ugc.trill:id/k13']")
    ];
    static readonly UiLocator[] LiveExit=[new("xpath",
        "//android.widget.ImageView[@content-desc='@2131842704']")];
    const string AudienceId="com.ss.android.ugc.trill:id/tv_online_audience_num";
    const string HostNameNewId="com.ss.android.ugc.trill:id/q2e";
    const string HostNameOldId="com.ss.android.ugc.trill:id/q16";
    const string UsernameId="com.ss.android.ugc.trill:id/user_name";
    static string? XmlText(string xml,string id)
    {
        try
        {
            return XDocument.Parse(xml).Descendants()
                .Where(x=>string.Equals((string?)x.Attribute("resource-id"),id,StringComparison.Ordinal))
                .Select(x=>(string?)x.Attribute("text"))
                .FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))?.Trim();
        }
        catch(System.Xml.XmlException){return null;}
    }
    // Use only fresh XML from the active LIVE, never a historical screenshot or
    // remembered coordinate. Validate displayed bounds within Android viewport.
    // The element coordinates are read from *this run's* LIVE XML, not from
    // an earlier screenshot. Prefer k1s, fall back to k13. Return the matched
    // resource ID so diagnostics identify exactly which layout was used.
    static (int X,int Y,string Resource)? HostButtonCenter(string xml)
    {
        try
        {
            var root=XDocument.Parse(xml);
            foreach(var resource in new[]{"k1s","k13"})
            {
                string id="com.ss.android.ugc.trill:id/"+resource;
                var node=root.Descendants().FirstOrDefault(x=>
                    (string?)x.Attribute("resource-id")==id &&
                    (string?)x.Attribute("displayed")=="true");
                var m=Regex.Match((string?)node?.Attribute("bounds")??"",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
                if(!m.Success)continue;
                int x1=int.Parse(m.Groups[1].Value), y1=int.Parse(m.Groups[2].Value);
                int x2=int.Parse(m.Groups[3].Value), y2=int.Parse(m.Groups[4].Value);
                if(x2-x1<8 || y2-y1<8 || x1<0 || y1<0 || x2>1440 || y2>3200)continue;
                return ((x1+x2)/2,(y1+y2)/2,resource);
            }
            return null;
        }
        catch(System.Xml.XmlException){return null;}
    }
    static int? ParseLiveCount(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return null;
        var match=Regex.Match(raw.Trim(),@"^(?<n>\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*(?<unit>[KkMm])?\+?$",RegexOptions.CultureInvariant);
        if(!match.Success)return null;
        var number=match.Groups["n"].Value;
        bool units=match.Groups["unit"].Success;
        if(!units && Regex.IsMatch(number,@"^\d{1,3}(?:[.,]\d{3})+$"))number=number.Replace(".","").Replace(",","");
        else number=number.Replace(',','.');
        if(!double.TryParse(number,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))return null;
        if(units)value*=match.Groups["unit"].Value.Equals("M",StringComparison.OrdinalIgnoreCase)?1000000:1000;
        return value>=0 && value<=100000000?(int)Math.Round(value):null;
    }
    static string NormalizeHost(string value)=>value.Trim().TrimStart('@').ToLowerInvariant();
    async Task SavePhase3Async(string dir,string prefix,string xml,CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir,prefix+".xml"),xml,ct);
        await File.WriteAllBytesAsync(Path.Combine(dir,prefix+".png"),await session.ScreenshotPngAsync(ct),ct);
    }
    async Task<string> ValidateOpenedLiveAsync(string captureDirectory,string keyword,int candidateAttempt,
        string? expectedHostUsername,CancellationToken ct)
    {
        string dir=Path.Combine(captureDirectory,"phase3",$"attempt-{candidateAttempt:D2}");
        Directory.CreateDirectory(dir);
        var total=Stopwatch.StartNew();
        var stage=Stopwatch.StartNew();
        void Mark(string key)=>log($"PHASE3 TIMING {key}: step={stage.ElapsedMilliseconds}ms total={total.ElapsedMilliseconds}ms");
        string xml="";string? audienceRaw=null;int? viewers=null;
        log("PHASE3 START: đọc viewer thực tế trong phòng LIVE.");
        await LivePopupHandler.DismissVirtualItemsPoliciesAsync(session,log,ct);
        // A changed search result (or a sponsored video) may leave the grid
        // without actually loading a LIVE room. Allow bounded UI settling and
        // verify the SAME viewer element through Appium if XML alone is empty.
        bool audienceNodeSeen=false;
        bool hostNodeSeen=false;
        bool directAudienceTried=false;
        int captchaDismissals=0;
        for(int viewerAttempt=1;viewerAttempt<=5;viewerAttempt++)
        {
            stage.Restart();
            xml=await session.SourceAsync(ct);
            audienceRaw=XmlText(xml,AudienceId);
            viewers=ParseLiveCount(audienceRaw);
            audienceNodeSeen=xml.Contains(AudienceId,StringComparison.Ordinal);
            hostNodeSeen=HostButtonCenter(xml).HasValue;
            if(!viewers.HasValue && !audienceNodeSeen && !hostNodeSeen && captchaDismissals<3)
            {
                var captcha=await LivePopupHandler.DismissVerifyCaptchaAsync(
                    session,log,ct,xml,"PHASE3");
                if(captcha.Status==CaptchaDismissStatus.Failed)
                {
                    await SavePhase3Async(dir,"captcha-dismiss-failed",captcha.Source,ct);
                    log("PHASE3 CAPTCHA_DISMISS_FAILED: không Back/click tiếp; trả FAILED cho workflow recovery.");
                    return "CAPTCHA_DISMISS_FAILED";
                }
                if(captcha.Status==CaptchaDismissStatus.Dismissed)
                {
                    captchaDismissals++;
                    xml=captcha.Source;
                    // Restart the five-attempt viewer budget for this same room.
                    viewerAttempt=0;
                    audienceNodeSeen=false;
                    hostNodeSeen=false;
                    directAudienceTried=false;
                    continue;
                }
            }
            if(!viewers.HasValue && !directAudienceTried && viewerAttempt>=3 &&
               (audienceNodeSeen || hostNodeSeen))
            {
                // A single direct lookup, not a succession of expensive XPath
                // calls on a page which may not be a LIVE room at all.
                directAudienceTried=true;
                var audienceElement=await session.FindAsync(LiveAudience[0],ct);
                if(audienceElement is not null)
                {
                    string directText=await session.ElementTextAsync(audienceElement,ct);
                    var directCount=ParseLiveCount(directText);
                    log($"PHASE3 VIEWER_DIRECT_XPATH: element=FOUND; " +
                        $"raw='{directText}'; parsed={(directCount?.ToString()??"?")}.");
                    if(directCount.HasValue)
                    {
                        audienceRaw=directText;
                        viewers=directCount;
                    }
                }
                else log("PHASE3 VIEWER_DIRECT_XPATH: element=MISSING; không đoán Viewer.");
            }
            log($"PHASE3 VIEWER_ATTEMPT {viewerAttempt}/5: raw='{audienceRaw??""}' " +
                $"parsed={(viewers?.ToString()??"?")} sourceMs={stage.ElapsedMilliseconds}; " +
                $"audienceNode={audienceNodeSeen}; hostNode={hostNodeSeen}; grid={HasGrid(xml)}.");
            if(viewers.HasValue)break;
            if(HasGrid(xml))break; // tap may never have opened a LIVE
            if(viewerAttempt<5)await DelayAsync(viewerAttempt<=2?1200:1800,ct);
        }
        stage.Restart();
        await SavePhase3Async(dir,"live-entry",xml,ct);
        await File.WriteAllTextAsync(Path.Combine(dir,"viewer-diagnostic.json"),
            JsonSerializer.Serialize(new {audienceId=AudienceId,audienceRaw,
                audienceNodeSeen,hostNodeSeen,directAudienceTried,
                gridPresent=HasGrid(xml),selectedLiveGrid=IsLiveSearchGridReady(xml),
                parsedViewers=viewers,checkedAt=DateTimeOffset.Now},
                new JsonSerializerOptions{WriteIndented=true}),ct);
        Mark("SAVE_ENTRY");
        if(!viewers.HasValue)
        {
            string missingReason=HasGrid(xml)?"GRID_STILL_VISIBLE":
                hostNodeSeen || audienceNodeSeen?"LIVE_VIEWER_UNVERIFIED":"LIVE_SCREEN_UNVERIFIED";
            log($"PHASE3 {missingReason}: viewer XPath={AudienceId}; " +
                $"raw='{audienceRaw??""}'; đã lưu live-entry và viewer-diagnostic; không tự coi là đạt.");
            return await RejectLiveAsync(dir,missingReason,ct);
        }
        log($"PHASE3 LIVE_INFO_READING: Viewer={viewers.Value:N0} từ tv_online_audience_num.");
        if(viewers.Value<config.MinimumViewers || (config.MaximumViewers>0 && viewers.Value>config.MaximumViewers))
        {
            log($"PHASE3 LIVE_CONDITION_FAILED: Viewer={viewers.Value:N0} ngoài [{config.MinimumViewers:N0}, {(config.MaximumViewers==0?"∞":config.MaximumViewers.ToString("N0"))}].");
            return await RejectLiveAsync(dir,"LIVE_CONDITION_FAILED",ct);
        }
        stage.Restart();
        log("PHASE3 PROFILE_CLICK_START: tìm nút chủ LIVE k1s hoặc k13.");
        bool opened=false;
        bool fastTapAttempted=false;
        var hostPoint=HostButtonCenter(xml);
        if(hostPoint.HasValue)
        {
            try
            {
                fastTapAttempted=true;
                await session.TapUiPointAsync(hostPoint.Value.X,hostPoint.Value.Y,ct);
                opened=true;
                log($"PHASE3 PROFILE_TAP_FAST: selector={hostPoint.Value.Resource}; sourceBounds=({hostPoint.Value.X},{hostPoint.Value.Y}); nhấn 1 lần.");
            }
            catch(TimeoutException e){ log($"PHASE3 PROFILE_TAP_FAST_TIMEOUT: {e.Message}; không nhấn lặp để tránh mở/đóng hồ sơ."); }
        }
        if(!opened && !fastTapAttempted)
        {
            log("PHASE3 PROFILE_CLICK_FALLBACK: XPath k1s -> k13 (chỉ khi tap nhanh chưa thực hiện).");
            opened=await session.ClickVisibleAnyAsync(LiveHostOpen,ct);
        }
        Mark("PROFILE_CLICK");
        if(!opened)
        {
            log("PHASE3 LIVE_IDENTITY_UNVERIFIED: không click được k1s/k13; đã lưu ảnh/XML live-entry.");
            return await RejectLiveAsync(dir,"LIVE_IDENTITY_UNVERIFIED",ct);
        }
        string? username=null;string? hostName=null;string profileXml="";
        // Three bounded attempts, not five. A full Appium XML tree can take several seconds.
        for(int attempt=1;attempt<=3;attempt++)
        {
            if(attempt>1)await DelayAsync(400,ct);
            stage.Restart();
            profileXml=await session.SourceAsync(ct);
            // The puzzle can appear only after tapping the host avatar and
            // cover the Profile sheet. Close its exact X and verify removal
            // before reading user_name/q2e; never use Android Back blindly.
            var profileCaptcha=await LivePopupHandler.DismissVerifyCaptchaAsync(
                session,log,ct,profileXml,"PHASE3_PROFILE");
            if(profileCaptcha.Status==CaptchaDismissStatus.Failed)
            {
                await SavePhase3Async(dir,"captcha-profile-dismiss-failed",profileCaptcha.Source,ct);
                log("PHASE3_PROFILE CAPTCHA_DISMISS_FAILED: không AndroidBack; trả FAILED cho app restart.");
                return "CAPTCHA_DISMISS_FAILED";
            }
            profileXml=profileCaptcha.Source;
            username=XmlText(profileXml,UsernameId);
            // New layout q2e, then old q16. Username remains user_name in both.
            string? newName=XmlText(profileXml,HostNameNewId);
            hostName=!string.IsNullOrWhiteSpace(newName)?newName:XmlText(profileXml,HostNameOldId);
            string nameSource=!string.IsNullOrWhiteSpace(newName)?"q2e":
                !string.IsNullOrWhiteSpace(hostName)?"q16":"none";
            log($"PHASE3 PROFILE_READ {attempt}/3: username={(string.IsNullOrWhiteSpace(username)?"MISSING":"FOUND")}, name={(string.IsNullOrWhiteSpace(hostName)?"MISSING":"FOUND")}, nameSource={nameSource}, sourceMs={stage.ElapsedMilliseconds}");
            if(!string.IsNullOrWhiteSpace(username))break;
        }
        stage.Restart();
        await SavePhase3Async(dir,"host-profile",profileXml,ct);
        Mark("SAVE_PROFILE");
        // Back once, and verify it only closed the host profile overlay.
        stage.Restart();
        log("PHASE3 PROFILE_CLOSE_START: Android Back.");
        await session.BackAsync(ct);
        Mark("PROFILE_BACK");
        string returned="";
        bool backToLive=false;
        for(int attempt=1;attempt<=2;attempt++)
        {
            stage.Restart();
            // Direct element lookup is cheaper than serializing the entire accessibility tree.
            var liveElement=await session.FindAsync(LiveAudience[0],ct);
            backToLive=liveElement is not null;
            log($"PHASE3 LIVE_RETURN_CHECK {attempt}/2: live={backToLive}, lookupMs={stage.ElapsedMilliseconds}, method=DirectXPath");
            if(!backToLive && attempt==2)
            {
                // Diagnostic fallback, not an additional Back or blind click.
                stage.Restart();
                returned=await session.SourceAsync(ct);
                backToLive=XmlText(returned,AudienceId) is not null;
                log($"PHASE3 LIVE_RETURN_FALLBACK: live={backToLive}, sourceMs={stage.ElapsedMilliseconds}");
            }
            if(backToLive)break;
            if(attempt==1)await DelayAsync(400,ct);
        }
        stage.Restart();
        if(string.IsNullOrEmpty(returned))
        {
            // The fast check does not require full XML: record method explicitly.
            await File.WriteAllTextAsync(Path.Combine(dir,"live-return-check.json"),
                JsonSerializer.Serialize(new {backToLive,method="DirectXPath",fullSourceSkipped=true,
                    checkedAt=DateTimeOffset.Now},new JsonSerializerOptions{WriteIndented=true}),ct);
            await File.WriteAllBytesAsync(Path.Combine(dir,"live-return.png"),await session.ScreenshotPngAsync(ct),ct);
        }
        else await SavePhase3Async(dir,"live-return",returned,ct);
        Mark("SAVE_RETURN");
        if(!backToLive)
        {
            log("PHASE3 LIVE_IDENTITY_UNVERIFIED: sau Back chưa xác nhận về phòng LIVE.");
            return "PHASE3_RECHECK_REQUIRED";
        }
        var normalized=NormalizeHost(username??"");
        if(normalized.Length==0)
        {
            log("PHASE3 LIVE_IDENTITY_UNVERIFIED: thiếu username chủ phòng.");
            return await RejectLiveAsync(dir,"LIVE_IDENTITY_UNVERIFIED",ct);
        }
        string expectedNormalized=NormalizeHost(expectedHostUsername??"");
        if(expectedNormalized.Length>0 &&
           !string.Equals(normalized,expectedNormalized,StringComparison.OrdinalIgnoreCase))
        {
            log($"PHASE3 LIVE_HOST_MISMATCH: expected=@{expectedNormalized}; actual=@{normalized}; " +
                "Chrome web source did not match the room opened on Android.");
            return await RejectLiveAsync(dir,"LIVE_HOST_MISMATCH",ct);
        }
        if(LiveRoomHistory.TryGetActive(normalized,config.RoomCooldownMinutes,out TimeSpan cooldownAge))
        {
            log($"ROOM_COOLDOWN_ACTIVE: Phòng @{normalized} mới tương tác " +
                $"{cooldownAge.TotalMinutes:F1} phút trước, bỏ qua.");
            return await RejectLiveAsync(dir,"ROOM_COOLDOWN_ACTIVE",ct);
        }
        var record=new {keyword,viewerCount=viewers.Value,viewerRaw=audienceRaw,viewerSource="AppiumText",
            hostName,hostUsername="@"+normalized,liveRoomId=(string?)null,
            identityLevel="HOST_USERNAME",roomIdVerified=false,status="LIVE_ACCEPTED",
            phase3DurationMs=total.ElapsedMilliseconds,enteredAt=DateTimeOffset.Now};
        stage.Restart();
        await File.WriteAllTextAsync(Path.Combine(dir,"live-room.json"),
            JsonSerializer.Serialize(record,new JsonSerializerOptions{WriteIndented=true}),ct);
        Mark("SAVE_RECORD");
        log($"PHASE3 LIVE_ACCEPTED: Viewer={viewers.Value:N0}; host=@{normalized}; tên={hostName??"?"}; giữ phòng; elapsed={total.ElapsedMilliseconds}ms.");
        return "LIVE_ACCEPTED";
    }
    async Task<string> RejectLiveAsync(string dir,string reason,CancellationToken ct)
    {
        await File.WriteAllTextAsync(Path.Combine(dir,"result.json"),
            JsonSerializer.Serialize(new {status=reason,time=DateTimeOffset.Now},new JsonSerializerOptions{WriteIndented=true}),ct);
        // The screen may already be a LIVE Search grid. Do not press Back in
        // that case; it could navigate outside the user's search results.
        string beforeExit=await session.SourceAsync(ct);
        if(IsLiveSearchGridReady(beforeExit))
        {
            log($"PHASE3 LIVE_REJECTED: {reason}; lưới LIVE đã hiện, không Back thêm.");
            return "LIVE_REJECTED: "+reason;
        }
        // First exit: use an explicit room exit control if available. Otherwise
        // press Android Back once (never tap a remembered screen coordinate).
        bool explicitExit=await session.ClickVisibleAnyAsync(LiveExit,ct);
        if(!explicitExit)await session.BackAsync(ct);
        string after="";
        for(int gridCheck=1;gridCheck<=4;gridCheck++)
        {
            await DelayAsync(gridCheck==1?700:900,ct);
            after=await session.SourceAsync(ct);
            bool gridReady=IsLiveSearchGridReady(after);
            log($"PHASE3 RETURN_GRID_WAIT {gridCheck}/4: grid={gridReady}; " +
                $"searchGrid={HasGrid(after)}; exitMethod={(explicitExit?"XPath":"AndroidBack")}.");
            if(gridReady)
            {
                await SavePhase3Async(dir,"after-exit",after,ct);
                log($"PHASE3 LIVE_REJECTED: {reason}; đã xác nhận tab LIVE và lưới kết quả.");
                return "LIVE_REJECTED: "+reason;
            }
        }
        // An extra Back is permitted ONLY when the live viewer/host control is
        // still present. Never repeatedly Back from a Google/security screen,
        // the Home feed, or an unknown destination.
        bool liveStillVisible=after.Contains(AudienceId,StringComparison.Ordinal) ||
            HostButtonCenter(after).HasValue;
        if(liveStillVisible)
        {
            log("PHASE3 RETURN_GRID_RECOVERY: còn thấy UI LIVE; thử Android Back thêm một lần.");
            await session.BackAsync(ct);
            for(int gridCheck=1;gridCheck<=3;gridCheck++)
            {
                await DelayAsync(900,ct);
                after=await session.SourceAsync(ct);
                if(IsLiveSearchGridReady(after))
                {
                    await SavePhase3Async(dir,"after-exit",after,ct);
                    log($"PHASE3 LIVE_REJECTED: {reason}; quay về lưới sau một Back bổ sung.");
                    return "LIVE_REJECTED: "+reason;
                }
            }
        }
        await SavePhase3Async(dir,"after-exit",after,ct);
        log($"PHASE3 EXIT_UNVERIFIED: {reason}; chưa thấy tab LIVE+GridView, " +
            "không tiếp tục chọn card hoặc Back phỏng đoán.");
        return "EXIT_UNVERIFIED: "+reason;
    }

    // Phase 2: search-result capture coordinates only locate an observation, not a room ID.
    // Fail closed if the original card cannot be verified from a fresh screenshot.
    sealed record Phase2ViewportAnchor(int Slot,LiveSearchCard Card,LiveSearchTile Tile);
    sealed record TargetObservation(LiveSearchCard Card,int Page,int Slot,
        LiveSearchTile Tile,byte[] Screenshot,
        IReadOnlyList<Phase2ViewportAnchor> ViewportAnchors);

    // A rejected room must return to a verified search grid before the next
    // ranked observation is tried. Track the approximate page reached by the
    // bounded swipe sequence, not the old page from the first search capture.
    sealed record Phase2AttemptResult(string Status,int GridPage,bool GridVerified);


    static bool Useful(string text) => !string.IsNullOrWhiteSpace(text) &&
        !text.StartsWith("Chưa đọc",StringComparison.OrdinalIgnoreCase) &&
        !text.StartsWith("OCR (",StringComparison.OrdinalIgnoreCase);

    static double CompareRegion(byte[] a,LiveSearchTile ra,byte[] b,LiveSearchTile rb)
    {
        using var ma=new MemoryStream(a);
        using var mb=new MemoryStream(b);
        using var ia=new Bitmap(ma);
        using var ib=new Bitmap(mb);
        // Animated LIVE video frames are not persistent room identities. This
        // only accepts very close frames, with a strong runner-up margin.
        const int n=12;
        double sum=0;
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            int ax=Math.Clamp(ra.X+(int)(ra.Width*(0.12+(x+0.5)*0.76/n)),0,ia.Width-1);
            int ay=Math.Clamp(ra.Y+(int)(ra.Height*(0.12+(y+0.5)*0.72/n)),0,ia.Height-1);
            int bx=Math.Clamp(rb.X+(int)(rb.Width*(0.12+(x+0.5)*0.76/n)),0,ib.Width-1);
            int by=Math.Clamp(rb.Y+(int)(rb.Height*(0.12+(y+0.5)*0.72/n)),0,ib.Height-1);
            var ca=ia.GetPixel(ax,ay);var cb=ib.GetPixel(bx,by);
            sum+=(Math.Abs(ca.R-cb.R)+Math.Abs(ca.G-cb.G)+Math.Abs(ca.B-cb.B))/765.0;
        }
        return sum/(n*n);
    }

    // A subtitle/title is more stable than the video pixels, but OCR alone is
    // unreliable. Cross-check independent title + audience evidence on TWO
    // fresh images before accepting any coordinate.
    static string NormalizePhase2Title(string value)
    {
        value=Regex.Replace(value??"",@"^OCR\s*\(ước lượng\)\s*:\s*","",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        var normalized=value.Normalize(NormalizationForm.FormD);
        var buffer=new StringBuilder(normalized.Length);
        foreach(char c in normalized)
        {
            if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue;
            buffer.Append(char.ToLowerInvariant(c=='đ'?'d':c));
        }
        return Regex.Replace(buffer.ToString(),@"[^a-z0-9]+"," ").Trim();
    }

    static double Phase2TitleSimilarity(string a,string b)
    {
        a=NormalizePhase2Title(a);b=NormalizePhase2Title(b);
        if(a.Length<10 || b.Length<10 || !a.Contains(' ') || !b.Contains(' '))return 0;
        // Bounded Levenshtein for imperfect OCR of stable overlay titles.
        var previous=Enumerable.Range(0,b.Length+1).ToArray();
        var current=new int[b.Length+1];
        for(int i=1;i<=a.Length;i++)
        {
            current[0]=i;
            for(int j=1;j<=b.Length;j++)
                current[j]=Math.Min(Math.Min(current[j-1]+1,previous[j]+1),
                    previous[j-1]+(a[i-1]==b[j-1]?0:1));
            (previous,current)=(current,previous);
        }
        return 1.0-previous[b.Length]/(double)Math.Max(a.Length,b.Length);
    }

    static bool Phase2AudienceCompatible(int? expected,int? actual)
    {
        if(!expected.HasValue || !actual.HasValue)return false;
        return Math.Abs((long)expected.Value-actual.Value)<=
            Math.Max(5,(long)Math.Ceiling(expected.Value*0.05));
    }

    static bool Phase2CreatorMatches(string expected,string actual)
    {
        if(!Useful(expected) || !Useful(actual))return false;
        string a=NormalizePhase2Title(expected),b=NormalizePhase2Title(actual);
        return a.Length>=3 && string.Equals(a,b,StringComparison.Ordinal);
    }

    static bool Phase2GeometryMatches(LiveSearchTile expected,LiveSearchTile actual) =>
        Math.Abs(expected.X-actual.X)<=14 && Math.Abs(expected.Y-actual.Y)<=24 &&
        Math.Abs(expected.Width-actual.Width)<=18 && Math.Abs(expected.Height-actual.Height)<=30;

    // Tier 2: re-read the exact recorded slot twice. A stable creator is a
    // strong identity; otherwise require title plus Viewer within 5%.
    // Viewer alone is never enough because adjacent rooms can have similar counts.
    async Task<LiveSearchTile?> TryPhase2MetadataRecheckAsync(TargetObservation target,
        int step,string directory,string xml,IReadOnlyList<LiveSearchTile> tiles,
        CancellationToken ct)
    {
        if(tiles.Count!=4 || target.Slot<0 || target.Slot>=tiles.Count ||
           !Phase2GeometryMatches(target.Tile,tiles[target.Slot]))
        {
            log($"PHASE2 MATCH_ATTEMPT: Tier 2 (Metadata) skipped; step={step}; " +
                "recorded slot geometry is not stable.");
            return null;
        }
        var raw=LiveSearchCardReader.Read(xml,target.Card.Keyword);
        if(raw.Count!=tiles.Count)
        {
            log($"PHASE2 MATCH_ATTEMPT: Tier 2 (Metadata) skipped; step={step}; " +
                "grid/card count changed.");
            return null;
        }

        log($"PHASE2 MATCH_ATTEMPT: Tier 2 (Metadata) checking viewer/creator " +
            $"at viewport={target.Page+1}, slot={target.Slot+1}, bounds=" +
            $"({tiles[target.Slot].X},{tiles[target.Slot].Y},{tiles[target.Slot].Width},{tiles[target.Slot].Height}).");
        var exactSlot=new HashSet<int>{target.Slot};
        var first=await LiveVisualOcr.TryEnhanceAsync(session,xml,raw,s=>{},ct,
            readTitles:true,onlyIndices:exactSlot,
            diagnosticDirectory:Path.Combine(directory,$"metadata-{step:D2}-first"));
        if(first is null)
        {
            log($"PHASE2 MATCH_ATTEMPT: Tier 2 (Metadata) failed; step={step}; OCR unavailable.");
            return null;
        }
        var firstCard=first[target.Slot];
        bool firstCreator=Phase2CreatorMatches(target.Card.Creator,firstCard.Creator);
        double firstTitle=Phase2TitleSimilarity(target.Card.Title,firstCard.Title);
        bool firstViewer=Phase2AudienceCompatible(target.Card.Viewers,firstCard.Viewers);
        bool firstMatch=firstCreator || (firstTitle>=0.78 && firstViewer);
        await File.WriteAllTextAsync(Path.Combine(directory,$"metadata-{step:D2}-first.json"),
            JsonSerializer.Serialize(new {slot=target.Slot+1,step,
                referenceTitle=target.Card.Title,referenceCreator=target.Card.Creator,
                referenceViewer=target.Card.Viewers,currentTitle=firstCard.Title,
                currentCreator=firstCard.Creator,currentViewer=firstCard.Viewers,
                creatorMatch=firstCreator,titleScore=firstTitle,viewerWithin5Percent=firstViewer,
                accepted=firstMatch},
                new JsonSerializerOptions{WriteIndented=true}),ct);
        log($"PHASE2 METADATA_RECHECK step={step}: slot={target.Slot+1}, " +
            $"creatorMatch={firstCreator}, titleScore={firstTitle:F3}, " +
            $"viewerWithin5Percent={firstViewer}, viewer={firstCard.Viewers?.ToString()??"?"}.");
        if(!firstMatch)return null;

        await DelayAsync(1000,ct);
        var secondXml=await session.SourceAsync(ct);
        var secondTiles=LiveSearchCardReader.TileBounds(secondXml);
        var secondRaw=LiveSearchCardReader.Read(secondXml,target.Card.Keyword);
        if(secondTiles.Count!=tiles.Count || secondRaw.Count!=tiles.Count ||
           !Phase2GeometryMatches(tiles[target.Slot],secondTiles[target.Slot]))
        {
            log($"PHASE2 METADATA_RECHECK_MOVED: slot={target.Slot+1}; lưới chuyển vị trí, không tap.");
            return null;
        }
        var second=await LiveVisualOcr.TryEnhanceAsync(session,secondXml,secondRaw,s=>{},ct,
            readTitles:true,onlyIndices:exactSlot,
            diagnosticDirectory:Path.Combine(directory,$"metadata-{step:D2}-second"));
        if(second is null)return null;
        var secondCard=second[target.Slot];
        bool secondCreator=Phase2CreatorMatches(target.Card.Creator,secondCard.Creator);
        double secondTitle=Phase2TitleSimilarity(target.Card.Title,secondCard.Title);
        bool secondViewer=Phase2AudienceCompatible(target.Card.Viewers,secondCard.Viewers);
        bool secondMatch=secondCreator || (secondTitle>=0.78 && secondViewer);
        log($"PHASE2 METADATA_CONFIRM step={step}: slot={target.Slot+1}, " +
            $"creatorMatch={secondCreator}, titleScore={secondTitle:F3}, " +
            $"viewerWithin5Percent={secondViewer}, secondViewer={secondCard.Viewers?.ToString()??"?"}.");
        await File.WriteAllTextAsync(Path.Combine(directory,$"metadata-{step:D2}-confirm.json"),
            JsonSerializer.Serialize(new {slot=target.Slot+1,titleScore=secondTitle,
                creatorMatch=secondCreator,viewerWithin5Percent=secondViewer,
                viewer=secondCard.Viewers,title=secondCard.Title,creator=secondCard.Creator,
                accepted=secondMatch},new JsonSerializerOptions{WriteIndented=true}),ct);
        if(!secondMatch)return null;

        var finalXml=await session.SourceAsync(ct);
        var finalTiles=LiveSearchCardReader.TileBounds(finalXml);
        if(finalTiles.Count!=tiles.Count ||
           !Phase2GeometryMatches(secondTiles[target.Slot],finalTiles[target.Slot]))return null;
        log($"PHASE2 TARGET_METADATA_VERIFIED: step={step}, slot={target.Slot+1}; " +
            "exactSlot=true; stableReads=2; viewerTolerance=5%.");
        return finalTiles[target.Slot];
    }

    static bool Phase2AnchorMatches(LiveSearchCard expected,LiveSearchCard actual)
    {
        if(Phase2CreatorMatches(expected.Creator,actual.Creator))return true;
        double title=Phase2TitleSimilarity(expected.Title,actual.Title);
        return title>=0.82 && Phase2AudienceCompatible(expected.Viewers,actual.Viewers);
    }

    // Tier 3 never trusts viewport arithmetic by itself. It permits the stored
    // target-slot coordinate only when at least two OTHER cards anchor the same
    // 2x2 viewport in two independent reads and every tile keeps its geometry.
    async Task<LiveSearchTile?> TryPhase2SpatialRecheckAsync(TargetObservation target,
        int step,string directory,string xml,IReadOnlyList<LiveSearchTile> tiles,
        CancellationToken ct)
    {
        if(tiles.Count!=4 || target.ViewportAnchors.Count!=4 ||
           target.Slot<0 || target.Slot>=tiles.Count)
        {
            log($"PHASE2 MATCH_ATTEMPT: Tier 3 (Spatial) skipped; step={step}; " +
                "complete 2x2 viewport history unavailable.");
            return null;
        }
        if(target.ViewportAnchors.Any(anchor=>anchor.Slot<0 || anchor.Slot>=tiles.Count ||
           !Phase2GeometryMatches(anchor.Tile,tiles[anchor.Slot])))
        {
            log($"PHASE2 MATCH_ATTEMPT: Tier 3 (Spatial) failed; step={step}; " +
                "recorded grid geometry mismatch.");
            return null;
        }

        var raw=LiveSearchCardReader.Read(xml,target.Card.Keyword);
        if(raw.Count!=tiles.Count)return null;
        var neighborSlots=target.ViewportAnchors.Where(anchor=>anchor.Slot!=target.Slot)
            .Select(anchor=>anchor.Slot).ToHashSet();
        log($"PHASE2 MATCH_ATTEMPT: Tier 3 (Spatial) checking viewport anchors; " +
            $"viewport={target.Page+1}; targetSlot={target.Slot+1}; " +
            $"neighborSlots={string.Join(',',neighborSlots.Select(i=>i+1))}.");
        var first=await LiveVisualOcr.TryEnhanceAsync(session,xml,raw,s=>{},ct,
            readTitles:true,onlyIndices:neighborSlots,
            diagnosticDirectory:Path.Combine(directory,$"spatial-{step:D2}-first"));
        if(first is null)return null;
        var firstAnchors=target.ViewportAnchors.Where(anchor=>anchor.Slot!=target.Slot &&
            Phase2AnchorMatches(anchor.Card,first[anchor.Slot]))
            .Select(anchor=>anchor.Slot).ToHashSet();
        log($"PHASE2 SPATIAL_ANCHORS_FIRST: matched={firstAnchors.Count}/3; " +
            $"slots={string.Join(',',firstAnchors.Select(i=>i+1))}.");
        if(firstAnchors.Count<2)return null;

        await DelayAsync(700,ct);
        string secondXml=await session.SourceAsync(ct);
        var secondTiles=LiveSearchCardReader.TileBounds(secondXml);
        var secondRaw=LiveSearchCardReader.Read(secondXml,target.Card.Keyword);
        if(secondTiles.Count!=tiles.Count || secondRaw.Count!=tiles.Count ||
           Enumerable.Range(0,tiles.Count).Any(i=>
               !Phase2GeometryMatches(tiles[i],secondTiles[i])))return null;
        var second=await LiveVisualOcr.TryEnhanceAsync(session,secondXml,secondRaw,s=>{},ct,
            readTitles:true,onlyIndices:neighborSlots,
            diagnosticDirectory:Path.Combine(directory,$"spatial-{step:D2}-second"));
        if(second is null)return null;
        var stableAnchors=target.ViewportAnchors.Where(anchor=>anchor.Slot!=target.Slot &&
            firstAnchors.Contains(anchor.Slot) &&
            Phase2AnchorMatches(anchor.Card,second[anchor.Slot]))
            .Select(anchor=>anchor.Slot).ToHashSet();
        log($"PHASE2 SPATIAL_ANCHORS_CONFIRM: matched={stableAnchors.Count}/3; " +
            $"slots={string.Join(',',stableAnchors.Select(i=>i+1))}; stableReads=2.");
        if(stableAnchors.Count<2)return null;

        string finalXml=await session.SourceAsync(ct);
        var finalTiles=LiveSearchCardReader.TileBounds(finalXml);
        if(finalTiles.Count!=tiles.Count || Enumerable.Range(0,tiles.Count).Any(i=>
           !Phase2GeometryMatches(secondTiles[i],finalTiles[i])))return null;
        log($"PHASE2 TARGET_SPATIAL_VERIFIED: step={step}; viewport={target.Page+1}; " +
            $"slot={target.Slot+1}; anchors={stableAnchors.Count}; stableReads=2.");
        return finalTiles[target.Slot];
    }

    async Task<Phase2AttemptResult> FindAndOpenSelectedAsync(TargetObservation target,
        string directory,int startPage,int rank,CancellationToken ct)
    {
        log($"PHASE2 TARGET_FOUND: rank={rank}, viewport={target.Page+1}, slot={target.Slot+1}, " +
            $"Viewer={target.Card.Viewers?.ToString()??"?"}; đối chiếu lại từ XML/ảnh hiện tại.");
        string phaseDirectory=Path.Combine(directory,"phase2",$"attempt-{rank:D2}");
        Directory.CreateDirectory(phaseDirectory);
        int gridPage=Math.Clamp(startPage,0,2);
        int expectedMoves=Math.Abs(gridPage-target.Page);
        int swipeLimit=expectedMoves+2; // bounded recovery if the grid shifted
        int direction=gridPage>target.Page?-1:gridPage<target.Page?+1:0;
        // direction -1 means going UP the result list (swipe Down), +1 means
        // going DOWN the result list (swipe Up). At the same page, try one
        // page in each direction only if image proof cannot be obtained.

        async Task<Phase2AttemptResult> OpenVerifiedAsync(LiveSearchTile tile,string evidence)
        {
            int tapX=tile.X+tile.Width/2;
            int tapY=tile.Y+Math.Min(tile.Height-18,Math.Max(70,tile.Height/2));
            log($"PHASE2 TARGET_VERIFIED: rank={rank}; evidence={evidence}; tap=({tapX},{tapY}).");
            await session.TapLiveCardAtAsync(tapX,tapY,ct);
            await DelayAsync(1500,ct);
            var after=await session.SourceAsync(ct);
            var afterImage=await session.ScreenshotPngAsync(ct);
            await File.WriteAllBytesAsync(Path.Combine(phaseDirectory,"after-tap.png"),afterImage,ct);
            await File.WriteAllTextAsync(Path.Combine(phaseDirectory,"after-tap.xml"),after,ct);
            if(!HasGrid(after))
            {
                log("PHASE2 TARGET_OPENED: đã chuyển khỏi lưới kết quả; danh tính phòng trong LIVE cần xác minh thêm.");
                var validation=await ValidateOpenedLiveAsync(directory,target.Card.Keyword,rank,
                    null,ct);
                // Only LIVE_REJECTED proves that Phase3 returned to the grid.
                // Never try another card if the Appium session is on an unknown
                // page or the overlay could still be open.
                bool gridReady=validation.StartsWith("LIVE_REJECTED:",StringComparison.Ordinal);
                if(gridReady)
                {
                    var afterReject=await session.SourceAsync(ct);
                    gridReady=HasGrid(afterReject);
                    log($"PHASE2 RETURN_GRID_CHECK: rank={rank}; grid={gridReady}; " +
                        $"estimatedViewport={gridPage+1}.");
                }
                return new Phase2AttemptResult("Đã mở LIVE; "+validation,gridPage,gridReady);
            }
            log("PHASE2 TARGET_NOT_OPENED: sau tap vẫn ở lưới; không thử bấm liên tục.");
            return new Phase2AttemptResult("Đã thử mở card nhưng chưa xác nhận được chuyển trang",gridPage,true);
        }

        for(int step=0;step<=swipeLimit;step++)
        {
            ct.ThrowIfCancellationRequested();
            var xml=await session.SourceAsync(ct);
            if(!HasGrid(xml))
            {
                log($"PHASE2 GRID_LOST: rank={rank}; không thể chọn card tiếp theo.");
                return new Phase2AttemptResult("Không còn lưới LIVE Search",gridPage,false);
            }
            var tiles=LiveSearchCardReader.TileBounds(xml);
            var image=await session.ScreenshotPngAsync(ct);
            await File.WriteAllBytesAsync(Path.Combine(phaseDirectory,$"recheck-{step:D2}.png"),image,ct);
            await File.WriteAllTextAsync(Path.Combine(phaseDirectory,$"recheck-{step:D2}.xml"),xml,ct);
            var matches=tiles.Select((tile,i)=>new {tile,i,
                score=CompareRegion(target.Screenshot,target.Tile,image,tile)})
                .OrderBy(c=>c.score).ToArray();
            var best=matches.FirstOrDefault();
            double next=matches.Skip(1).Select(c=>c.score).DefaultIfEmpty(1.0).First();
            await File.WriteAllTextAsync(Path.Combine(phaseDirectory,$"recheck-{step:D2}.json"),
                JsonSerializer.Serialize(new {rank,targetPage=target.Page+1,targetSlot=target.Slot+1,
                    gridPageEstimated=gridPage+1,step,expectedMoves,
                    candidates=matches.Select(m=>new {slot=m.i+1,visualDistance=m.score})},
                    new JsonSerializerOptions {WriteIndented=true}),ct);
            log($"PHASE2 RECHECK rank={rank} step={step}: best={best?.i+1}, " +
                $"score={best?.score:F3}, runnerUp={next:F3}, page~={gridPage+1}.");
            if(best is not null && best.score<=0.105 && next-best.score>=0.035 &&
               best.tile.Height>=110)
                return await OpenVerifiedAsync(best.tile,"PREVIEW_MATCH");
            log($"PHASE2 MATCH_ATTEMPT: Tier 1 (Image) failed; " +
                $"distance={best?.score:F3}; threshold<=0.105; " +
                $"runnerUpMargin={(best is null?0:next-best.score):F3}.");
            if(gridPage==target.Page && step>=expectedMoves &&
               step<=expectedMoves+1 && tiles.Count==4)
            {
                log($"PHASE2 MATCH_ATTEMPT: Tier 2 (Metadata) started; rank={rank}; " +
                    "dynamic preview did not pass strict image proof.");
                var metadataTile=await TryPhase2MetadataRecheckAsync(target,step,
                    phaseDirectory,xml,tiles,ct);
                if(metadataTile.HasValue && metadataTile.Value.Height>=110)
                    return await OpenVerifiedAsync(metadataTile.Value,"METADATA_MATCH");
                log($"PHASE2 MATCH_ATTEMPT: Tier 2 (Metadata) failed; rank={rank}; step={step}.");

                // Spatial fallback runs only once at the exact expected viewport.
                // It requires two neighboring anchors twice; viewport arithmetic
                // and the stored target coordinate alone can never authorize a tap.
                if(step==expectedMoves)
                {
                    var spatialTile=await TryPhase2SpatialRecheckAsync(target,step,
                        phaseDirectory,xml,tiles,ct);
                    if(spatialTile.HasValue && spatialTile.Value.Height>=110)
                        return await OpenVerifiedAsync(spatialTile.Value,"SPATIAL_MATCH");
                    log($"PHASE2 MATCH_ATTEMPT: Tier 3 (Spatial) failed; rank={rank}; " +
                        $"step={step}; no blind tap.");
                }
            }
            if(step>=swipeLimit)break;
            if(direction==0)
            {
                // Extra scan when the chosen card may have shifted one row.
                // Initial search ended at the bottom (page 3); if already on
                // the target page, check upward first, then down if needed.
                direction=gridPage==0?+1:-1;
            }
            else if(step>=expectedMoves+1)
            {
                // We already looked one page beyond the expected location.
                // Do not keep dragging further into unrelated results.
                break;
            }
            log($"PHASE2 REPOSITION: rank={rank}; move={(direction<0?"BACKWARD":"FORWARD")}; " +
                $"step={step+1}/{swipeLimit}; estimatedPage={gridPage+1}.");
            if(direction<0)await session.SwipeLiveSearchDownAsync(ct,config.SearchSwipePercent);
            else await session.SwipeLiveSearchUpAsync(ct,config.SearchSwipePercent);
            gridPage=Math.Clamp(gridPage+direction,0,2);
            await DelayAsync(config.SearchAfterSwipeWaitMilliseconds,ct);
        }
        bool onGrid=HasGrid(await session.SourceAsync(ct));
        log($"PHASE2 TARGET_NOT_FOUND: rank={rank}; không đủ chứng cứ ảnh hoặc metadata; " +
            $"grid={onGrid}; không tap phỏng đoán.");
        return new Phase2AttemptResult("Không định vị chắc chắn card đã chọn; không mở LIVE",gridPage,onGrid);
    }

    static bool IsCandidate(LiveSearchCard c) =>
        c.Viewers.HasValue && c.ViewerEvidence is
            ("Verified" or "OcrRepeatedCandidate" or "OcrCandidate");

    bool WithinThreshold(LiveSearchCard c) => IsCandidate(c) &&
        c.Viewers!.Value >= config.MinimumViewers &&
        (config.MaximumViewers == 0 || c.Viewers.Value <= config.MaximumViewers);

    /// <summary>
    /// Search/compare-only ranking. OCR approximations are intentionally NOT
    /// treated as a verified audience count; this does not join a LIVE room.
    /// </summary>
    LiveSearchCard? BestInBatch(IReadOnlyList<LiveSearchCard> batch,string keyword)
    {
        var eligible=batch.Where(WithinThreshold)
            .OrderByDescending(c=>c.Viewers).ToList();
        int unreadable=batch.Count(c=>c.Viewers is null &&
            c.ViewerEvidence!="IconLikeIgnored");
        int likes=batch.Count(c=>c.ViewerEvidence=="IconLikeIgnored");
        int outside=batch.Count(c=>IsCandidate(c) && !WithinThreshold(c));
        log($"{keyword}: đánh giá {batch.Count} card ĐÃ THU THẬP (ID có thể chưa xác minh); " +
            $"đạt ngưỡng={eligible.Count}, Viewer không đọc được={unreadable}, " +
            $"Like bị bỏ qua={likes}, ngoài ngưỡng={outside}. " +
            "Card lỗi OCR không chặn việc so sánh các card hợp lệ.");
        if(eligible.Count==0)return null;
        var best=eligible[0];
        log($"{keyword}: GỢI Ý phòng Viewer cao nhất trong nhóm: {best.Viewers:N0} " +
            $"(bằng chứng={best.ViewerEvidence}, {best.Title}). Chỉ hiển thị tham khảo; KHÔNG tự mở phòng.");
        return best;
    }

    public static bool IsLiveSearchGridXml(string xml)=>IsLiveSearchGridReady(xml);

    async Task<LiveSearchReport> RunTargetedFirstCardAsync(CancellationToken ct,
        string keyword,string expectedHostUsername)
    {
        var cards=new List<LiveSearchCard>();
        string expected=NormalizeHost(expectedHostUsername);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(config.PerKeywordTimeoutSeconds,
            config.TotalSearchTimeoutSeconds)));
        try
        {
            log($"TARGETED_FIRST_CARD_START: search=@{expected}; source=CHROME_CDP_QUEUE; " +
                "scanCards=false; ocrRanking=false; pickSlot=1.");
            if(!await SearchOneKeywordWithMissingTabRetriesAsync(keyword,timeout.Token))
            {
                string reason=searchNavigationBlocked
                    ? "TARGETED_FIRST_CARD_SEARCH_BLOCKED: "+searchNavigationStopReason
                    : "TARGETED_FIRST_CARD_SEARCH_FAILED";
                return new LiveSearchReport(cards,1,0,reason);
            }

            var (xml,raw,stable)=await ReadSettledAsync(keyword,timeout.Token);
            var tiles=LiveSearchCardReader.TileBounds(xml);
            if(!stable || raw.Count==0 || tiles.Count==0 || raw.Count!=tiles.Count)
            {
                await SaveViewerDiagnosticsAsync(keyword,xml,timeout.Token);
                string reason=!stable?"GRID_NOT_STABLE":raw.Count==0?"NO_CARD":"CARD_TILE_MISMATCH";
                log($"TARGETED_FIRST_CARD_STOP: reason={reason}; cards={raw.Count}; tiles={tiles.Count}.");
                return new LiveSearchReport(cards,1,0,"TARGETED_FIRST_CARD_"+reason);
            }

            var first=raw[0] with
            {
                Note=raw[0].Note+$" | TARGETED_FIRST_CARD: tìm @{expected}; không quét/OCR xếp hạng"
            };
            cards.Add(first);
            onCard(first);
            var tile=tiles[0];
            int tapX=tile.X+tile.Width/2;
            int tapY=tile.Y+Math.Min(tile.Height-18,Math.Max(70,tile.Height/2));
            log($"TARGETED_FIRST_CARD_PICK: expected=@{expected}; slot=1/{raw.Count}; " +
                $"tap=({tapX},{tapY}); noFurtherGridScan=true.");
            await session.TapLiveCardAtAsync(tapX,tapY,timeout.Token);
            await DelayAsync(1500,timeout.Token);
            string after=await session.SourceAsync(timeout.Token);
            if(HasGrid(after))
            {
                log("TARGETED_FIRST_CARD_NOT_OPENED: sau tap vẫn ở lưới; chuyển ứng viên web kế tiếp.");
                return new LiveSearchReport(cards,1,0,"TARGETED_FIRST_CARD_NOT_OPENED");
            }

            string captureDirectory=Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AndroidMultiDeviceV1","diagnostics",session.DeviceKey,"targeted-first-card-"+
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string validation=await ValidateOpenedLiveAsync(captureDirectory,keyword,1,
                expectedHostUsername,timeout.Token);
            bool accepted=validation.Contains("LIVE_ACCEPTED",StringComparison.Ordinal);
            // A rejected candidate may only advance when Phase 3 returned to a VERIFIED grid.
            // If the UI is unknown, hand recovery to the fresh-session supervisor.
            if(!accepted && !validation.StartsWith("LIVE_REJECTED:",StringComparison.Ordinal) &&
               !validation.Contains("CAPTCHA_DISMISS_FAILED",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "CHROME_CDP_ANDROID_CHECKPOINT_UNVERIFIED: " + validation);
            log($"TARGETED_FIRST_CARD_RESULT: expected=@{expected}; result={validation}; " +
                $"next={(accepted?"KEEP_ROOM":"NEXT_WEB_CANDIDATE")}.");
            return new LiveSearchReport(cards,1,0,"TARGETED_FIRST_CARD; "+validation,
                accepted?first:null);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {
            log($"TARGETED_FIRST_CARD_TIMEOUT: expected=@{expected}; next=NEXT_WEB_CANDIDATE.");
            return new LiveSearchReport(cards,1,0,"TARGETED_FIRST_CARD_TIMEOUT");
        }
    }

    public async Task<LiveSearchReport> RunAsync(CancellationToken ct,string? onlyKeyword=null,
        string? expectedHostUsername=null)
    {
        searchNavigationBlocked=false;
        searchNavigationStopReason="";
        var errors=config.Validate();
        if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
        var keys=string.IsNullOrWhiteSpace(onlyKeyword)
            ? config.Keywords.ToList()
            : new List<string>{onlyKeyword.Trim()};
        if(string.IsNullOrWhiteSpace(onlyKeyword) && config.KeywordSelectionMode=="Random")
            for(int i=keys.Count-1;i>0;i--)
            {int j=Random.Shared.Next(i+1);(keys[i],keys[j])=(keys[j],keys[i]);}
        string expectedHost=NormalizeHost(expectedHostUsername??"");
        if(expectedHost.Length>0)
            return await RunTargetedFirstCardAsync(ct,keys[0],expectedHostUsername!);
        var all=new List<LiveSearchCard>();
        int attempted=0,scrolls=0;
        LiveSearchCard? selected=null;
        string finish="Đã kiểm tra hết từ khóa trong giới hạn cấu hình";
        var totalDeadline=DateTimeOffset.UtcNow.AddSeconds(config.TotalSearchTimeoutSeconds);
        foreach(var keyword in keys)
        {
            if(DateTimeOffset.UtcNow>=totalDeadline)
            {finish="Hết thời gian tìm kiếm toàn đợt";break;}
            ct.ThrowIfCancellationRequested();
            attempted++;
            using var keywordTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            var batch=new List<LiveSearchCard>();
            var targetObservations=new List<TargetObservation>();
            var remaining=totalDeadline-DateTimeOffset.UtcNow;
            var keywordBudgetSeconds=Math.Min(config.PerKeywordTimeoutSeconds,
                Math.Max(1,remaining.TotalSeconds));
            var keywordDeadline=DateTimeOffset.UtcNow.AddSeconds(keywordBudgetSeconds);
            keywordTimeout.CancelAfter(TimeSpan.FromSeconds(keywordBudgetSeconds));
            log($"{keyword}: giới hạn {keywordBudgetSeconds:F0}s/từ khóa; " +
                $"toàn đợt còn {remaining.TotalSeconds:F0}s; " +
                $"mục tiêu {config.CardsPerBatch} card quan sát; lưới 2x2 lấy 4+2+2.");
            try
            {
                if(!await SearchOneKeywordWithMissingTabRetriesAsync(keyword,keywordTimeout.Token))
                {
                    if(searchNavigationBlocked)
                    {
                        finish="LIVE Search: điều hướng hoặc xác nhận tab LIVE thất bại ("+
                            searchNavigationStopReason+")";
                        log($"LIVE_SEARCH_BATCH_STOP: {finish}; không lặp lại 5 Back cho từ khóa khác.");
                        return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                    }
                    continue;
                }
                // A keyword gets its own tracker so the same screen slots do
                // not imply the same room when switching Search keywords.
                var tracker=new LiveCardScrollTracker();
                var cardCache=new Dictionary<int,LiveSearchCard>();
                // Includes unresolved observations, without treating them as confirmed NEW.
                var observationCache=new Dictionary<int,LiveSearchCard>();
                // PENDING survives across page transitions. Never turn a
                // previously undecidable tile into NEW simply because its
                // ID is absent from the confirmed-card cache.
                var pendingIds=new HashSet<int>();
                // First-sighting evidence is retained when a tile is PENDING.
                // It can be reconsidered once its badge is fully visible.
                var pendingHistory=new Dictionary<int,(int firstPage,int sightings,bool enteredBottom)>();
                int emptyReads=0,stillPages=0;
                string captureDirectory=Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AndroidMultiDeviceV1","diagnostics",session.DeviceKey,"live-scroll-"+
                    DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+attempted);
                log($"{keyword}: ghi ảnh và XML cho TỪNG màn hình tại {captureDirectory} (chỉ trên PC).");
                for(int page=0;page<=config.MaxSearchScrolls;page++)
                {
                    keywordTimeout.Token.ThrowIfCancellationRequested();
                    var (xml,raw,stable)=await ReadSettledAsync(keyword,keywordTimeout.Token);
                    if(!stable && raw.Count>0)
                    {
                        finish="UI kết quả LIVE chưa ổn định — không kéo khi chưa đọc xong";
                        log($"{keyword}: {finish}.");
                        return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                    }
                    if(raw.Count==0)
                    {
                        emptyReads++;
                        log($"{keyword}: chưa thấy card sau khi chờ UI; đọc lại màn hình ({emptyReads}/2). {LiveSearchCardReader.Diagnose(xml)}");
                        if(emptyReads>=2)
                        {
                            await SaveViewerDiagnosticsAsync(keyword,xml,keywordTimeout.Token);
                            finish="Không tìm được cấu trúc card trong XML; dừng để chẩn đoán";
                            return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                        }
                        await DelayAsync(1400,keywordTimeout.Token);
                        page--; // retry the same viewport, without dragging
                        continue;
                    }
                    emptyReads=0;
                    var tiles=LiveSearchCardReader.TileBounds(xml);
                    if(tiles.Count!=raw.Count)
                    {
                        finish="Vị trí card XML không khớp số card; không kéo phỏng đoán";
                        await SaveViewerDiagnosticsAsync(keyword,xml,keywordTimeout.Token);
                        return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                    }
                    // The old coordinate-only tracker could count the SAME
                    // bottom-row cards as new when they moved up one full row.
                    // Snapshot the actual previews before creating any IDs.
                    byte[] trackingImage;
                    try { trackingImage=await session.ScreenshotPngAsync(keywordTimeout.Token); }
                    catch(OperationCanceledException){throw;}
                    catch(TimeoutException){throw;}
                    catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
                    catch(Exception ex)
                    {
                        finish="Không chụp được ảnh để chống đếm trùng card";
                        log($"{keyword}: {finish} ({ex.GetType().Name}); không kéo tiếp.");
                        return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                    }
                    var (matchedIds,moved,_)=tracker.Match(tiles,trackingImage,xml);
                    var ids=matchedIds.ToArray();
                    var fresh=new HashSet<int>(Enumerable.Range(0,ids.Length)
                        .Where(i=>!cardCache.ContainsKey(ids[i])));
                    log($"{keyword}: màn hình {page+1}, {raw.Count} khung, " +
                        $"{fresh.Count} card CHƯA ĐỐI CHIẾU, {raw.Count-fresh.Count} card đã nhận diện; " +
                        $"sau kéo chuyển động={(moved?"có":"chưa thấy")}." );
                    var cards=raw.ToArray();
                    for(int i=0;i<cards.Length;i++)
                        if(!fresh.Contains(i) && cardCache.TryGetValue(ids[i],out var cached))
                            cards[i]=cached;
                    if(config.CardsPerBatch==8 && tiles.Count==4 && cards.Length==4 && page>0)
                    {
                        // Bounded 4+2+2 sampling: the upper row is overlap.
                        // OCR only the bottom row being sampled on this swipe.
                        fresh.Remove(0);
                        fresh.Remove(1);
                        fresh.Add(2);
                        fresh.Add(3);
                    }
                    if(fresh.Count>0)
                    {
                        // Only new tiles are OCR-read. Previously seen previews
                        // can animate continuously, but re-reading them should
                        // not eat the whole keyword timeout on every 5% drag.
                        var first=await LiveVisualOcr.TryEnhanceAsync(session,xml,cards,log,
                            keywordTimeout.Token,onlyIndices:fresh,
                            diagnosticDirectory:Path.Combine(captureDirectory,$"badge-viewport-{page+1:D2}-first"));
                        if(first is not null)
                        {
                            var secondNeeded=fresh.Any(i=>first[i].Viewers.HasValue ||
                                first[i].ViewerEvidence is "OcrConflict" or "Không đọc được badge người xem");
                            if(secondNeeded)
                            {
                                await DelayAsync(1200,keywordTimeout.Token);
                                var second=await LiveVisualOcr.TryEnhanceAsync(session,xml,cards,log,
                                    keywordTimeout.Token,readTitles:false,onlyIndices:fresh,
                                    diagnosticDirectory:Path.Combine(captureDirectory,$"badge-viewport-{page+1:D2}-second"));
                                if(second is not null)
                                    first=LiveVisualOcr.CombineTwoCaptures(first,second,log);
                            }
                            cards=first.ToArray();
                        }
                    }
                    // V2.1.23: bounded 4+2+2 sample mode. On this TikTok
                    // two-column result layout, the top row after each scroll
                    // is the previous bottom row. Sampling and OCR confidence
                    // are separate: an unreadable candidate consumes a slot,
                    // but is never called a verified unique LIVE room.
                    // This mode intentionally stops after two actual swipes,
                    // even when no room passed OCR / identity verification.
                    if(config.CardsPerBatch==8 && tiles.Count==4 && cards.Length==4)
                    {
                        int firstIndex=page==0?0:2;
                        var sampledIndices=new HashSet<int>(Enumerable.Range(firstIndex,4-firstIndex));
                        var overlapIndices=new HashSet<int>(Enumerable.Range(0,firstIndex));
                        try
                        {
                            await SaveViewportSnapshotAsync(captureDirectory,page,xml,trackingImage,
                                tiles,ids,cards,sampledIndices,new HashSet<int>(),keywordTimeout.Token);
                            log($"{keyword}: lưu mẫu viewport-{page+1:D2}.png/.json/.xml " +
                                $"(SAMPLED={sampledIndices.Count}, OVERLAP={overlapIndices.Count}).");
                        }
                        catch(OperationCanceledException){throw;}
                        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                        { log($"{keyword}: lỗi lưu chẩn đoán: {ex.Message}"); }

                        foreach(int i in sampledIndices.OrderBy(i=>i))
                        {
                            var card=cards[i];
                            var sampled=card with {Note=card.Note+
                                " | SAMPLED: chưa xác minh ID phòng; Viewer chỉ tham khảo"};
                            var viewportAnchors=Enumerable.Range(0,cards.Length)
                                .Select(slot=>new Phase2ViewportAnchor(slot,cards[slot],tiles[slot]))
                                .ToArray();
                            targetObservations.Add(new TargetObservation(
                                sampled,page,i,tiles[i],trackingImage,viewportAnchors));
                            batch.Add(sampled);
                            all.Add(sampled);
                            onCard(sampled);
                            log($"{keyword}: SAMPLE {batch.Count}/8 từ màn hình {page+1}, " +
                                $"ô {i+1}/4, Viewer={(sampled.Viewers.HasValue?sampled.Viewers.Value.ToString("N0"):"?")}, " +
                                $"OCR={sampled.ViewerEvidence}; không khẳng định duy nhất.");
                        }
                        log($"{keyword}: màn hình {page+1}, lấy mẫu={sampledIndices.Count}, " +
                            $"chồng lấp={overlapIndices.Count}, tổng đã quan sát={batch.Count}/8.");
                        if(batch.Count>=8 || page>=2)
                        {
                            selected=BestInBatch(batch.Take(8).ToArray(),keyword);
                            finish=selected is null
                                ? "Hoàn tất 8 card quan sát; không có card đạt ngưỡng OCR"
                                : "Hoàn tất 8 card quan sát; có gợi ý Viewer OCR tham khảo";
                            log($"{keyword}: ĐỦ 8/8 CARD QUAN SÁT (4+2+2); " +
                                "DỪNG NGAY, không kéo để chờ 8 NEW hoặc hết thời gian.");
                            if(selected is not null)
                            {
                                // Rank ONLY the 8 already-observed cards, highest provisional
                                // Viewer first. Never re-scan or extend the sample window.
                                var ranked=targetObservations.Where(o=>WithinThreshold(o.Card))
                                    .OrderByDescending(o=>o.Card.Viewers)
                                    .ThenBy(o=>o.Page).ThenBy(o=>o.Slot).ToArray();
                                int estimatedPage=page;
                                bool accepted=false;
                                for(int index=0;index<ranked.Length;index++)
                                {
                                    keywordTimeout.Token.ThrowIfCancellationRequested();
                                    var target=ranked[index];
                                    int rank=index+1;
                                    log($"PHASE2 NEXT_CANDIDATE: rank={rank}/{ranked.Length}; " +
                                        $"ocrViewer={target.Card.Viewers:N0}; " +
                                        $"viewport={target.Page+1}; slot={target.Slot+1}.");
                                    Phase2AttemptResult outcome;
                                    try
                                    {
                                        outcome=await FindAndOpenSelectedAsync(target,captureDirectory,
                                            estimatedPage,rank,keywordTimeout.Token);
                                    }
                                    catch(OperationCanceledException){throw;}
                                    catch(TimeoutException){throw;}
                                    catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
                                    catch(Exception ex)
                                    {
                                        log($"PHASE2 ATTEMPT_ERROR: rank={rank}; " +
                                            $"{ex.GetType().Name}: {ex.Message}; không tap phỏng đoán.");
                                        finish += "; lỗi tìm lại card";
                                        break;
                                    }
                                    estimatedPage=outcome.GridPage;
                                    finish += "; " + outcome.Status;
                                    if(outcome.Status.Contains("LIVE_ACCEPTED",StringComparison.Ordinal))
                                    {
                                        selected=target.Card;
                                        accepted=true;
                                        log($"PHASE2 CANDIDATE_ACCEPTED: rank={rank}, " +
                                            $"ocrViewer={target.Card.Viewers:N0}; dừng thử các card khác.");
                                        break;
                                    }
                                    if(!outcome.GridVerified)
                                    {
                                        log($"PHASE2 NEXT_CANDIDATE_STOP: rank={rank}; " +
                                            "chưa xác nhận đã quay về lưới; không tiếp tục.");
                                        break;
                                    }
                                    if(outcome.Status.Contains("LIVE_REJECTED:",StringComparison.Ordinal))
                                        log($"PHASE2 CANDIDATE_REJECTED: rank={rank}; " +
                                            $"đã về lưới, thử ứng viên hạng {rank+1} nếu còn.");
                                    else
                                        log($"PHASE2 CANDIDATE_SKIPPED: rank={rank}; " +
                                            "không thể xác minh card, tiếp tục khi lưới còn nguyên.");
                                }
                                if(!accepted)
                                {
                                    finish += "; Không có LIVE nào được xác minh đạt điều kiện trong nhóm 8 card";
                                    log("PHASE2 ALL_CANDIDATES_EXHAUSTED: không có phòng được chấp nhận.");
                                }
                            }
                            return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                        }
                        log($"{keyword}: tiếp tục 1 lần kéo để lấy 2 card hàng dưới tiếp theo; " +
                            $"kéo={config.SearchSwipePercent}%, đợi={config.SearchAfterSwipeWaitMilliseconds}ms.");
                        await session.SwipeLiveSearchUpAsync(keywordTimeout.Token,config.SearchSwipePercent);
                        scrolls++;
                        await DelayAsync(config.SearchAfterSwipeWaitMilliseconds,keywordTimeout.Token);
                        continue;
                    }
                    // Unrecognized layout: preserve conservative identity matching;
                    // never assume 4+2+2 if the viewport is not a 2x2 grid.
                    // Reconcile ALL unfamiliar cards, including those carried
                    // over from earlier PENDING screens. Missing Viewer is not
                    // a reason to reject the whole batch: it only prevents
                    // that particular card from being ranked by audience.
                    var pendingThisPage=new HashSet<int>();
                    if(page>0 && fresh.Count>0)
                    {
                        var aliases=tracker.ReconcilePreviousViewport(tiles,ids,cards,
                            observationCache,fresh,out var unresolved,out var provenNew);
                        foreach(var pair in aliases)
                        {
                            ids[pair.Key]=pair.Value;
                            fresh.Remove(pair.Key);
                            if(pendingIds.Contains(pair.Value))
                            {
                                pendingThisPage.Add(pair.Key);
                                observationCache[pair.Value]=cards[pair.Key];
                                log($"{keyword}: PENDING ID {pair.Value} được khớp lại; tiếp tục theo dõi, chưa cộng vào nhóm.");
                                continue;
                            }
                            log($"{keyword}: SEEN card {pair.Key+1}/{cards.Length} " +
                                $"= ID {pair.Value}; không đếm lại.");
                        }
                        foreach(var i in fresh.ToArray())
                        {
                            int id=ids[i];
                            bool alreadyPending=pendingIds.Contains(id);
                            bool independentNew=provenNew.Contains(i) && !unresolved.Contains(i);
                            bool frontBadgeVisible=cards[i].ViewerEvidence is
                                ("Verified" or "OcrRepeatedCandidate" or "OcrCandidate" or
                                 "IconLikeIgnored" or "BadgeCandidate");
                            // If a card first entered from the bottom and was
                            // followed in another viewport under the same ID,
                            // a now-readable badge can resolve its identity.
                            // Never promote solely because the audience count
                            // matches (or differs from) another card.
                            if(alreadyPending && pendingHistory.TryGetValue(id,out var state))
                            {
                                // Viewer is not an identity: unrelated LIVE
                                // rooms may show the same audience count.
                                // Only a sufficiently specific title+creator
                                // pair can veto promotion as an old room.
                                bool hasNamedRoom=cards[i].Creator!="Chưa đọc được chủ phòng" &&
                                    cards[i].Title!="Chưa đọc được tiêu đề";
                                bool sameNamedRoom=hasNamedRoom && cardCache.Values.Any(c=>
                                    c.Title==cards[i].Title && c.Creator==cards[i].Creator);
                                if(state.enteredBottom && state.sightings>=1 &&
                                   frontBadgeVisible &&
                                   tracker.MeasuredShiftPixels>=25 && !sameNamedRoom)
                                {
                                    independentNew=true;
                                    log($"{keyword}: PENDING → NEW card {i+1}/{cards.Length}, " +
                                        "đã theo dõi qua nhiều ảnh và badge đã rõ.");
                                }
                            }
                            // A distinctive full room title AND creator can establish a new
                            // identity even when a long swipe leaves no overlapping old tile.
                            // Require both fields and uniqueness across confirmed AND pending rooms;
                            // viewer counts and screenshot motion alone never establish identity.
                            bool named= !string.IsNullOrWhiteSpace(cards[i].Title) &&
                                !string.IsNullOrWhiteSpace(cards[i].Creator) &&
                                cards[i].Title!="Chưa đọc được tiêu đề" &&
                                cards[i].Creator!="Chưa đọc được chủ phòng";
                            bool duplicatesNamed=named &&
                                cardCache.Values.Concat(observationCache.Values).Any(old=>
                                    old.Title==cards[i].Title && old.Creator==cards[i].Creator);
                            bool otherTileNamed=named && Enumerable.Range(0,cards.Length).Any(j=>
                                j!=i && cards[j].Title==cards[i].Title &&
                                cards[j].Creator==cards[i].Creator);
                            if(!independentNew && named && !duplicatesNamed && !otherTileNamed &&
                                moved && ids[i]!=0 && page>0)
                            {
                                independentNew=true;
                                log($"{keyword}: NEW card {i+1}/{cards.Length}: " +
                                    "tiêu đề + chủ phòng khác toàn bộ card đã lưu (không dùng Viewer làm ID).");
                            }
                            if(!independentNew)
                            {
                                fresh.Remove(i);
                                pendingIds.Add(id);
                                observationCache[id]=cards[i];
                                pendingThisPage.Add(i);
                                int visibleBottom=tiles.Max(t=>t.Bottom);
                                bool bottom=tiles[i].Y>=visibleBottom-tiles[i].Height-26;
                                if(pendingHistory.TryGetValue(id,out var old))
                                    pendingHistory[id]=(old.firstPage,old.sightings+1,old.enteredBottom);
                                else pendingHistory[id]=(page,1,bottom);
                                log($"{keyword}: PENDING card {i+1}/{cards.Length} " +
                                    "(chưa phân biệt chắc chắn cũ/mới); giữ để đối chiếu lần kế, " +
                                    "KHÔNG cộng vào nhóm.");
                            }
                        }
                    }
                    foreach(int i in fresh)
                    {
                        pendingIds.Remove(ids[i]);
                        pendingHistory.Remove(ids[i]);
                    }
                    // A reliably exposed new card can be counted even if its
                    // audience icon or OCR fails: Viewer is a separate filter.
                    // The card remains VIEWER_UNKNOWN rather than aborting the
                    // keyword or poisoning the rest of the group.
                    if(page>0)
                    {
                        if(fresh.Count>0)stillPages=0;
                        else stillPages++; // SEEN/PENDING, no proven new content
                    }
                    int seenCount=Enumerable.Range(0,ids.Length)
                        .Count(i=>!fresh.Contains(i) && !pendingThisPage.Contains(i));
                    log($"{keyword}: thời gian còn: từ khóa={Math.Max(0,(keywordDeadline-DateTimeOffset.UtcNow).TotalSeconds):F0}s; " +
                        $"toàn đợt={Math.Max(0,(totalDeadline-DateTimeOffset.UtcNow).TotalSeconds):F0}s; " +
                        $"độ dịch đo được={tracker.MeasuredShiftPixels}px; " +
                        $"longSwipeCandidates={tracker.LongSwipeCandidates}, longSwipeAccepted={tracker.LongSwipeAccepted} " +
                        "(0px nghĩa là chưa đo được, không đồng nghĩa chưa kéo).");
                    log($"{keyword}: màn hình {page+1}: NEW={fresh.Count}, " +
                        $"SEEN={seenCount}, PENDING={pendingThisPage.Count}; " +
                        $"nhóm duy nhất={batch.Count}/{config.CardsPerBatch}.");
                    if(stillPages==4 || (stillPages>4 && stillPages%4==0))
                    {
                        log($"{keyword}: {stillPages} lượt chưa xác minh được card NEW " +
                            $"(PENDING={pendingThisPage.Count}). Tiếp tục trong giới hạn " +
                            $"{config.MaxSearchScrolls} lượt kéo/thời gian; KHÔNG đếm PENDING thành NEW.");
                    }
                    try
                    {
                        await SaveViewportSnapshotAsync(captureDirectory,page,xml,trackingImage,
                            tiles,ids,cards,fresh,pendingThisPage,keywordTimeout.Token);
                        log($"{keyword}: đã lưu ảnh trước/sau cuộn gián tiếp qua viewport-{page+1:D2}.png " +
                            "và bản đồ card cùng tên .json/.xml.");
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                    {
                        log($"{keyword}: KHÔNG LƯU ĐƯỢC ảnh chẩn đoán: {ex.Message}");
                    }
                    if(page==0 && cards.All(c=>c.Viewers is null))
                    {
                        log($"{keyword}: toàn bộ Viewer chưa đọc được; " +
                            "ghi chẩn đoán rồi tiếp tục tìm trong giới hạn, " +
                            "không loại toàn bộ nhóm chỉ vì lỗi OCR.");
                        await SaveViewerDiagnosticsAsync(keyword,xml,keywordTimeout.Token);
                    }
                    for(int i=0;i<cards.Length;i++)
                    {
                        keywordTimeout.Token.ThrowIfCancellationRequested();
                        if(!fresh.Contains(i))continue;
                        var card=cards[i];
                        cardCache[ids[i]]=card;
                        observationCache[ids[i]]=card;
                        var added=card with { Note=card.Note +
                            (WithinThreshold(card)
                              ? " | Đạt ngưỡng OCR (tham khảo, chưa xác minh độc lập)"
                              : " | Chưa đạt/chưa xác định ngưỡng Viewer") };
                        cardCache[ids[i]]=added;
                        batch.Add(added);
                        all.Add(added);
                        onCard(added);
                        log($"{keyword}: NEW nhóm #{batch.Count} (track ID {ids[i]}, " +
                            $"ô {i+1}/{cards.Length}), " +
                            $"Viewer={(added.Viewers.HasValue?added.Viewers.Value.ToString("N0"):"?")}, " +
                            $"bằng chứng={added.ViewerEvidence}." );
                        // Hard completion boundary: once 8 unique cards are
                        // accepted, stop regardless of remaining time or swipes.
                        if(batch.Count>=config.CardsPerBatch)
                        {
                            selected=BestInBatch(batch.Take(config.CardsPerBatch).ToArray(),keyword);
                            finish=selected is null
                                ? "Đã đọc đủ card duy nhất; không có phòng đạt ngưỡng Viewer"
                                : "Đã đọc đủ card duy nhất; đề xuất phòng có Viewer OCR cao nhất";
                            log($"{keyword}: ĐỦ {config.CardsPerBatch}/{config.CardsPerBatch}; " +
                                "DỪNG NGAY — không kéo tiếp dù còn thời gian.");
                            return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                        }
                        await DelayAsync(config.SearchPerCardWaitMilliseconds,keywordTimeout.Token);
                    }
                    log($"{keyword}: nhóm đang có {batch.Count}/{config.CardsPerBatch} card mới đã đọc.");
                    if(batch.Count>=config.CardsPerBatch)
                    {
                        // Evaluate the first complete group in read order.
                        var group=batch.Take(config.CardsPerBatch).ToArray();
                        selected=BestInBatch(group,keyword);
                        if(selected is not null)
                        {
                            finish="Đã đề xuất LIVE có Viewer OCR cao nhất trong nhóm, chưa mở phòng";
                            return new LiveSearchReport(all,attempted,scrolls,finish,selected);
                        }
                        batch.RemoveRange(0,config.CardsPerBatch);
                    }
                    if(page==config.MaxSearchScrolls)break;
                    log($"{keyword}: đã đọc xong màn hình; kéo CÓ KIỂM SOÁT {config.SearchSwipePercent}% " +
                        $"(~{(int)Math.Round(Math.Max(300,tiles.Max(t=>t.Bottom)+41)*config.SearchSwipePercent/100.0)}px trên màn này) đúng 1 lần, " +
                        $"chờ {config.SearchAfterSwipeWaitMilliseconds}ms rồi kiểm tra từng card MỚI.");
                    await session.SwipeLiveSearchUpAsync(keywordTimeout.Token,config.SearchSwipePercent);
                    scrolls++;
                    await DelayAsync(config.SearchAfterSwipeWaitMilliseconds,keywordTimeout.Token);
                }
                if(batch.Count>0)
                {
                    // Evaluate even the final incomplete batch before switching
                    // keyword, e.g. 6 visible cards under a 65-second timeout.
                    var finalCandidate=BestInBatch(batch,keyword);
                    if(finalCandidate is not null)
                    {
                        selected=finalCandidate;
                        finish="Gợi ý từ nhóm cuối chưa đủ số card; Viewer vẫn là OCR tham khảo";
                        break;
                    }
                }
                log($"{keyword}: chưa có ứng viên đạt ngưỡng trong phạm vi kéo; " +
                    "chuyển từ khóa nếu còn thời gian.");
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested)
            {
                string expired=DateTimeOffset.UtcNow>=totalDeadline ? "toàn đợt" : "từ khóa";
                log($"Từ khóa {keyword}: hết giới hạn {expired}; " +
                    $"đã đọc {batch.Count} card trong nhóm đang dở.");
                if(batch.Count>0)
                {
                    var tentative=BestInBatch(batch,keyword);
                    if(tentative is not null)
                    {
                        selected=tentative;
                        finish="Gợi ý OCR từ nhóm chưa đủ card khi hết thời gian — chưa mở LIVE";
                        break;
                    }
                }
            }
            catch(TimeoutException ex)
            {
                log($"Từ khóa {keyword}: lỗi Appium ({ex.Message}); chuyển lên Global Session Recovery.");
                throw;
            }
        }
        return new LiveSearchReport(all,attempted,scrolls,finish,selected);
    }

}
