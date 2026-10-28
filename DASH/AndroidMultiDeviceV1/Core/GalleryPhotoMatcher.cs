using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Fail-closed visual identification of a photo inside TikTok's own gallery.
/// Resource-id l37 identifies every thumbnail equally; index [1]/[2] never
/// proves a relationship to a filename uploaded through ADB.
/// Screenshots are kept in memory and are not written to logs.
/// </summary>
public static class GalleryPhotoMatcher
{
    public sealed record Candidate(string ElementId,double Score,int X,int Y,int Width,int Height);
    public sealed record MatchResult(Candidate? Match,double Best,double RunnerUp,int VisibleCount,int ComparedCount,string Reason);

    const int SampleWidth=24;

    static float[] Features(Bitmap image,Rectangle rect)
    {
        // Ignore edges/ticks around gallery thumbnails. Source is already a
        // square 720px crop; TikTok generally center-crops its thumbnails.
        int inset=(int)(Math.Min(rect.Width,rect.Height)*0.16);
        var roi=Rectangle.Inflate(rect,-inset,-inset);
        if(roi.Width<16 || roi.Height<16)
            return Array.Empty<float>();
        using var normalized=new Bitmap(SampleWidth,SampleWidth);
        using(var graphics=Graphics.FromImage(normalized))
        {
            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            graphics.DrawImage(image,new Rectangle(0,0,SampleWidth,SampleWidth),roi,GraphicsUnit.Pixel);
        }
        var values=new float[SampleWidth*SampleWidth*3];
        for(int y=0;y<SampleWidth;y++)for(int x=0;x<SampleWidth;x++)
        {
            var pixel=normalized.GetPixel(x,y);
            int idx=3*(y*SampleWidth+x);
            values[idx]=pixel.R/255f;values[idx+1]=pixel.G/255f;values[idx+2]=pixel.B/255f;
        }
        return values;
    }

    static double Pearson(ReadOnlySpan<float> a,ReadOnlySpan<float> b)
    {
        double aa=0,bb=0;
        for(int i=0;i<a.Length;i++){aa+=a[i];bb+=b[i];}
        aa/=a.Length;bb/=b.Length;
        double num=0,sa=0,sb=0;
        for(int i=0;i<a.Length;i++)
        {
            double x=a[i]-aa,y=b[i]-bb;
            num+=x*y;sa+=x*x;sb+=y*y;
        }
        return sa*sb<1e-10?0:Math.Clamp(num/Math.Sqrt(sa*sb),-1,1);
    }

    public static double Similarity(Bitmap source,Bitmap screenshot,Rectangle region)
    {
        var bounds=new Rectangle(0,0,screenshot.Width,screenshot.Height);
        region=Rectangle.Intersect(region,bounds);
        if(region.Width<40 || region.Height<40)return 0;
        // Candidate button may include a selection badge/outer border; center
        // region reduces that influence. Portraits with similar solid backgrounds
        // may be ambiguous and must be rejected by the runner-up margin.
        var a=Features(source,new Rectangle(0,0,source.Width,source.Height));
        var b=Features(screenshot,region);
        if(a.Length==0 || b.Length!=a.Length)return 0;
        double diff=0;
        for(int i=0;i<a.Length;i++)diff+=Math.Abs(a[i]-b[i]);
        double color=1-Math.Clamp(diff/a.Length,0,1);
        double corr=(Pearson(a,b)+1)/2;
        // A match must agree in both chromatic detail and spatial structure.
        return Math.Clamp(0.64*corr+0.36*color,0,1);
    }

    public static async Task<double> VerifyAvatarAsync(AppiumSession session,string sourceJpeg,
        IReadOnlyList<UiLocator> avatarNode,CancellationToken ct)
    {
        using var source=new Bitmap(sourceJpeg);
        foreach(var locator in avatarNode)
        {
            var id=await session.FindAsync(locator,ct);
            if(id is null)continue;
            try
            {
                var r=await session.ElementRectAsync(id,ct);
                var window=await session.WindowRectAsync(ct);
                var screenshotBytes=await session.ScreenshotPngAsync(ct);
                using var memory=new MemoryStream(screenshotBytes);
                using var screenshot=new Bitmap(memory);
                if(r.Width<35 || r.Height<35 || window.Width<=0 || window.Height<=0)continue;
                double factorX=(double)screenshot.Width/window.Width;
                double factorY=(double)screenshot.Height/window.Height;
                if(Math.Abs(factorX-factorY)>0.08)continue;
                var region=new Rectangle((int)Math.Round((r.X-window.X)*factorX),
                    (int)Math.Round((r.Y-window.Y)*factorY),
                    (int)Math.Round(r.Width*factorX),(int)Math.Round(r.Height*factorY));
                return Similarity(source,screenshot,region);
            }
            catch(InvalidOperationException) { /* UI changed while capturing */ }
        }
        return 0;
    }

    /// <summary>
    /// V2.1.41: support both selection-circle thumbnails (legacy l37)
    /// and the new l3z photo buttons. Some newly provisioned cloud phones show
    /// only ONE image in Recents; there is then no same-row spacing to infer.
    /// The uploaded reference image MUST visually match; index 1 alone NEVER
    /// authorizes an avatar change.
    /// </summary>
    public static async Task<MatchResult> FindAsync(AppiumSession session,string sourceJpeg,
        IReadOnlyList<UiLocator> galleryButtons,CancellationToken ct)
    {
        // Prefer the first selector which actually exists (l3z then l37),
        // rather than treating two locators for one tile as two distinct photos.
        IReadOnlyList<string> elements=Array.Empty<string>();
        string locatorName="unknown";
        foreach(var locator in galleryButtons)
        {
            elements=await session.FindAllAnyAsync(new[]{locator},ct);
            if(elements.Count==0)continue;
            locatorName=locator.Value.Contains("l3z",StringComparison.Ordinal)?"l3z":
                locator.Value.Contains("l37",StringComparison.Ordinal)?"l37":"other";
            break;
        }
        if(elements.Count==0)
            return new(null,0,0,0,0,"Không có nút chọn ảnh l3z/l37 trong Gallery.");

        var screenshotBytes=await session.ScreenshotPngAsync(ct);
        using var stream=new MemoryStream(screenshotBytes);
        using var screenshot=new Bitmap(stream);
        using var source=new Bitmap(sourceJpeg);
        var window=await session.WindowRectAsync(ct);
        if(window.Width<=0 || window.Height<=0)
            throw new InvalidOperationException("Appium không trả kích thước màn hình.");
        double fx=(double)screenshot.Width/window.Width;
        double fy=(double)screenshot.Height/window.Height;
        if(Math.Abs(fx-fy)/Math.Max(fx,fy)>0.32)
            return new(null,0,0,elements.Count,0,
                $"Kích thước Appium và screenshot lệch bất thường: {window.Width}x{window.Height}, " +
                $"{screenshot.Width}x{screenshot.Height}.");

        var badges=new List<(string Id,Rectangle Pixel,int X,int Y,int Width,int Height)>();
        int rectFailures=0;
        foreach(var id in elements)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r=await session.ElementRectAsync(id,ct);
                var p=new Rectangle((int)Math.Round((r.X-window.X)*fx),
                    (int)Math.Round((r.Y-window.Y)*fy),
                    (int)Math.Round(r.Width*fx),(int)Math.Round(r.Height*fy));
                if(p.Width<=0 || p.Height<=0){rectFailures++;continue;}
                badges.Add((id,p,r.X,r.Y,r.Width,r.Height));
            }
            catch(InvalidOperationException){rectFailures++;}
        }
        if(badges.Count==0)
            return new(null,0,0,elements.Count,0,$"Không có tọa độ ảnh hợp lệ; lỗi={rectFailures}.");

        var screenBounds=new Rectangle(0,0,screenshot.Width,screenshot.Height);
        var candidates=new List<Candidate>();
        var examples=new List<string>();
        int rejected=0;

        // l3z on newer TikTok variants may cover the photo tile rather than
        // its old tiny selection circle. Treat a square-sized control as a
        // direct visual crop, but never accept a full-screen overlay.
        var circles=new List<(string Id,Rectangle Pixel,int X,int Y,int Width,int Height)>();
        foreach(var b in badges)
        {
            int side=Math.Min(b.Pixel.Width,b.Pixel.Height);
            if(side<48){circles.Add(b);continue;}
            if(side>screenshot.Width*0.62 ||
                Math.Max(b.Pixel.Width,b.Pixel.Height)>side*1.70)
            {rejected++;continue;}
            var tile=new Rectangle(b.Pixel.Left+(b.Pixel.Width-side)/2,
                b.Pixel.Top+(b.Pixel.Height-side)/2,side,side);
            var visible=Rectangle.Intersect(tile,screenBounds);
            if(visible.Width<side*0.9 || visible.Height<side*0.9)
            {rejected++;continue;}
            double score=Similarity(source,screenshot,visible);
            candidates.Add(new Candidate(b.Id,score,b.X,b.Y,b.Width,b.Height));
            if(examples.Count<4)examples.Add($"button-tile=({tile.X},{tile.Y},{side}) score={score:F3}");
        }

        // Legacy l37 is a small selection circle at the top-right of each
        // thumbnail. Infer the cell side from circles in the SAME row.
        var gaps=new List<int>();
        foreach(var b in circles)
        {
            var n=circles.Where(x=>x.Id!=b.Id && x.Pixel.X>b.Pixel.X &&
                    Math.Abs(x.Pixel.Y-b.Pixel.Y)<=Math.Max(12,b.Pixel.Height))
                .OrderBy(x=>x.Pixel.X).FirstOrDefault();
            if(n.Id is null)continue;
            int gap=n.Pixel.X-b.Pixel.X;
            if(gap>=48 && gap<=Math.Min(220,screenshot.Width*0.5))gaps.Add(gap);
        }
        gaps.Sort();
        int inferredSide=gaps.Count>0?gaps[gaps.Count/2]:0;
        bool singlePhotoCircle=circles.Count==1 && badges.Count==1;
        if(inferredSide==0 && singlePhotoCircle)
        {
            // On the supplied screenshot Recents contains a single image tile,
            // about a quarter of the viewport width. This is a *hypothesis*
            // validated by similarity with the uploaded photo, not proof from
            // gallery ordering. Never use it for multiple ambiguous circles.
            inferredSide=(int)Math.Round(screenshot.Width/4.0);
        }
        if(inferredSide>0)
        {
            foreach(var b in circles)
            {
                int margin=Math.Clamp((int)Math.Round(inferredSide*0.08),3,12);
                int topInset=Math.Clamp((int)Math.Round(inferredSide*0.02),0,4);
                var tile=new Rectangle(b.Pixel.Right+margin-inferredSide,
                    b.Pixel.Top-topInset,inferredSide,inferredSide);
                var visible=Rectangle.Intersect(tile,screenBounds);
                if(inferredSide<48 || visible.Width<inferredSide*0.90 ||
                    visible.Height<inferredSide*0.90)
                {rejected++;continue;}
                double score=Similarity(source,screenshot,visible);
                candidates.Add(new Candidate(b.Id,score,b.X,b.Y,b.Width,b.Height));
                if(examples.Count<4)examples.Add($"circle-tile=({tile.X},{tile.Y},{inferredSide}) score={score:F3}");
            }
        }

        string diag=$"locator={locatorName}; buttons={elements.Count}; compared={candidates.Count}; " +
            $"circles={circles.Count}; inferredSide={inferredSide}; " +
            $"singleCircle={singlePhotoCircle}; screenshot={screenshot.Width}x{screenshot.Height}; " +
            $"rectFailures={rectFailures}; rejected={rejected}; " +string.Join("; ",examples);
        if(candidates.Count==0)
            return new(null,0,0,elements.Count,0,
                "Không xác định được vùng thumbnail phù hợp. "+diag);

        var ordered=candidates.OrderByDescending(c=>c.Score).ToList();
        double best=ordered[0].Score;
        double second=ordered.Count>1?ordered[1].Score:0;
        // Very high similarity is required even for a single visible photo;
        // a lone candidate is not automatically correct.
        bool unique=best>=0.88 && (ordered.Count==1 || best-second>=0.08);
        return new(unique?ordered[0]:null,best,second,elements.Count,candidates.Count,
            (unique?"Ảnh đối chiếu trực quan đạt ngưỡng; ":
                "Ảnh chưa đạt tương đồng/độ phân biệt; ")+diag);
    }

}
