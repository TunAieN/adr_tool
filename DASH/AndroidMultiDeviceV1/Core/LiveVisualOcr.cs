using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// OCR fallback ONLY when TikTok exposes image-only LIVE result tiles.
/// Requires the operator to install Tesseract OCR for Windows (eng language).
/// Read-only: never clicks a card, joins a room, or posts a message.
/// </summary>
internal static class LiveVisualOcr
{
    static string? FindExecutable()
    {
        var custom=Environment.GetEnvironmentVariable("TESSERACT_EXE");
        if(!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))return custom;
        foreach(var candidate in new [] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Tesseract-OCR","tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Tesseract-OCR","tesseract.exe")
        }) if(File.Exists(candidate)) return candidate;
        var path=Environment.GetEnvironmentVariable("PATH")??"";
        foreach(var folder in path.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            try { var file=Path.Combine(folder.Trim().Trim('"'),"tesseract.exe");
                if(File.Exists(file))return file; }
            catch(ArgumentException){}
        }
        return null;
    }

    static async Task<string> OcrAsync(string exe,Bitmap source,Rectangle crop,
        string tempDirectory,int pageMode,CancellationToken ct,bool highContrast=false)
    {
        crop=Rectangle.Intersect(crop,new Rectangle(0,0,source.Width,source.Height));
        if(crop.Width<8 || crop.Height<6)return "";
        string file=Path.Combine(tempDirectory,$"roi-{Guid.NewGuid():N}.png");
        try
        {
            // Scale from the actual ROI dimensions. Tiny cloud-phone badges need
            // more enlargement than larger renders, while capping the result
            // avoids wasting memory on high-resolution devices.
            int resizeFactor=Math.Clamp((int)Math.Ceiling(90d/crop.Height),4,7);
            using(var resized=new Bitmap(crop.Width*resizeFactor,crop.Height*resizeFactor))
            {
                using(var g=Graphics.FromImage(resized))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source,new Rectangle(0,0,resized.Width,resized.Height),crop,GraphicsUnit.Pixel);
                }
                if(highContrast)
                {
                    for(int y=0;y<resized.Height;y++)for(int x=0;x<resized.Width;x++)
                    {
                        var c=resized.GetPixel(x,y);
                        bool bright=c.R>=165 && c.G>=165 && c.B>=165 &&
                            Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<=75;
                        resized.SetPixel(x,y,bright?Color.Black:Color.White);
                    }
                }
                resized.Save(file,System.Drawing.Imaging.ImageFormat.Png);
            }
            using var p=new Process();
            p.StartInfo.FileName=exe;
            p.StartInfo.UseShellExecute=false;
            p.StartInfo.CreateNoWindow=true;
            p.StartInfo.RedirectStandardOutput=true;
            p.StartInfo.RedirectStandardError=true;
            p.StartInfo.ArgumentList.Add(file);
            p.StartInfo.ArgumentList.Add("stdout");
            p.StartInfo.ArgumentList.Add("--psm");
            p.StartInfo.ArgumentList.Add(pageMode.ToString(CultureInfo.InvariantCulture));
            p.StartInfo.ArgumentList.Add("-l");
            p.StartInfo.ArgumentList.Add("eng");
            if(pageMode==7 || pageMode==8)
            {
                p.StartInfo.ArgumentList.Add("-c");
                p.StartInfo.ArgumentList.Add("tessedit_char_whitelist=0123456789KkMm+.,");
            }
            if(!p.Start())return "";
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            try
            {
                var output=await p.StandardOutput.ReadToEndAsync(timeout.Token);
                await p.WaitForExitAsync(timeout.Token);
                return p.ExitCode==0?output.Trim():"";
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested)
            {
                try {if(!p.HasExited)p.Kill(entireProcessTree:true);}catch{}
                return "";
            }
            finally {try{if(!p.HasExited)p.Kill(entireProcessTree:true);}catch{}}
        }
        finally {try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    static int? Count(string value)
    {
        // Restrict OCR to a WHOLE chip-shaped number. Never parse a numeric
        // prefix of a longer string (for example 2.9K+95), which previously
        // could be treated as 2,900 despite contamination from the video.
        var compact=Regex.Replace(value,@"\s+","").Trim().TrimStart('.');
        // A single digit followed by 'm' in a tiny badge is easily an OCR
        // hallucination (captured 7m while the image showed person icon + 74).
        // Do not rank such an unsupported million-scale reading as a LIVE.
        if(Regex.IsMatch(compact,@"^\d[mM]\+?$",RegexOptions.CultureInvariant))return null;
        var match=Regex.Match(compact,@"^(?<n>\d{1,5}|\d{1,3}[.,]\d{1,2}[kKmM]|\d{1,5}[kKmM])(?<plus>\+)?$",
            RegexOptions.CultureInvariant);
        if(!match.Success)return null;
        string raw=match.Groups["n"].Value;
        int mult=1;
        if(raw.EndsWith("K",StringComparison.OrdinalIgnoreCase)){mult=1000;raw=raw[..^1];}
        else if(raw.EndsWith("M",StringComparison.OrdinalIgnoreCase)){mult=1000000;raw=raw[..^1];}
        if(!double.TryParse(raw.Replace(',','.'),NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,out var parsed))return null;
        var n=parsed*mult;
        return n>=0 && n<=100000000?(int)Math.Round(n):null;
    }

    // The SAME glyph can OCR inconsistently depending on the preprocessing:
    // 1.4K+ -> 14K+ (decimal point lost), 171 -> 71 (leading thin 1 lost).
    // Reconciliation below is deliberately *provisional*: never upgrade a
    // result from a single screenshot to Verified/OcrConfirmed.
    static (int? Value,string Rule) ReconcileViewerOcr(
        string tightRaw,string wideRaw,string contrastRaw)
    {
        var raws=new[]{tightRaw,wideRaw,contrastRaw};
        var valid=raws.Select(raw=>(Raw:Regex.Replace(raw,@"\s+","").Trim().TrimStart('.'),
            Value:Count(raw))).Where(x=>x.Value.HasValue).ToArray();
        if(valid.Length==0)return (null,"NoReading");
        var distinct=valid.Select(x=>x.Value!.Value).Distinct().ToArray();
        if(distinct.Length==1)return (distinct[0],"SameImageAgreement");

        // Only reconcile these when ALL parseable readings have the same
        // base digits and the same K/M suffix, differing just by a dot.
        // Example: '14K+' + '1.4K+' -> candidate 1,400 (NOT 14,000).
        // The loss of a decimal point is common at 352x624 resolution.
        static string CanonicalDigits(string raw) =>
            Regex.Replace(raw.ToUpperInvariant().TrimEnd('+'),@"[.,]","");
        var suffixes=valid.Select(x=>Regex.Match(x.Raw,@"[kKmM]\+?$").Value.ToUpperInvariant())
            .Distinct().ToArray();
        if(suffixes.Length==1 && suffixes[0].Length>0 &&
            valid.Select(x=>CanonicalDigits(x.Raw)).Distinct().Count()==1)
        {
            var decimalCandidates=valid.Where(x=>
                Regex.IsMatch(x.Raw,@"^\d{1,3}[.,]\d{1,2}[kKmM]\+?$"))
                .Select(x=>x.Value!.Value).Distinct().ToArray();
            if(decimalCandidates.Length==1)
                return (decimalCandidates[0],"DecimalDotRecoveredCandidate");
        }

        // Integer-only LIVE badges (99, 533, 55, 319) must NOT pass through
        // the K/M decimal-point recovery. Tesseract's PSM 8 sometimes drops
        // a digit or invents a trailing zero. Two agreeing preprocessings of
        // ONE image are only a provisional candidate; a second screenshot is
        // still required before the UI may show OcrRepeatedCandidate.
        // Examples: 99 / 9 / 99 -> 99; 533 / 5330 / 533 -> 533.
        if(valid.Length>=3 && valid.All(x=>Regex.IsMatch(x.Raw,@"^\d{1,5}$")))
        {
            var groups=valid.GroupBy(x=>x.Value!.Value)
                .OrderByDescending(g=>g.Count()).ToArray();
            if(groups[0].Count()>=2 &&
                (groups.Length==1 || groups[0].Count()>groups[1].Count()))
                return (groups[0].Key,"IntegerMajorityCandidate");
        }

        // A leading thin '1' is sometimes dropped entirely. Return the
        // longer OCR reading *only* when a shorter candidate is its exact
        // suffix. This is NOT proof, so it remains a candidate for review.
        // Example: 71 versus 171 => 171 (candidate only).
        if(valid.Length<=3 && suffixes.Length==1 && suffixes[0].Length==0 &&
            valid.All(x=>Regex.IsMatch(x.Raw,@"^\d{1,5}$")))
        {
            var longest=valid.OrderByDescending(x=>x.Raw.Length).First();
            if(valid.Where(x=>x.Value!=longest.Value)
                .All(x=>longest.Raw.Length==x.Raw.Length+1 &&
                    longest.Raw.StartsWith('1') &&
                    longest.Raw.EndsWith(x.Raw,StringComparison.Ordinal)))
                return (longest.Value!.Value,"LeadingOneRecoveredCandidate");
        }
        // One preprocessing may return only a shortened integer: 55 / "" / 5.
        // In that case keep the longer reading for *manual review*, not as a
        // trusted viewer count. Do not prefer 5330 over 533 just because the
        // longer reading happens to end with an extra zero.
        if(valid.Length==2 && valid.All(x=>Regex.IsMatch(x.Raw,@"^\d{1,5}$")))
        {
            var longer=valid.OrderByDescending(x=>x.Raw.Length).First();
            var shorter=valid.OrderBy(x=>x.Raw.Length).First();
            if(longer.Raw.Length==shorter.Raw.Length+1 &&
                (longer.Raw.StartsWith(shorter.Raw,StringComparison.Ordinal) &&
                    !longer.Raw.EndsWith('0') ||
                 longer.Raw.StartsWith('1') &&
                    longer.Raw.EndsWith(shorter.Raw,StringComparison.Ordinal)))
                return (longer.Value!.Value,"PartialIntegerRecoveredCandidate");
        }
        return (null,"OcrConflict");
    }

    // Reject OCR output that appends two or more digits to an otherwise
    // plausible integer observed in the SAME badge (e.g. 227 => 22770).
    // OCR variants based on one image are correlated, so agreement by
    // PSM 8 and PSM 13 must not override this contradiction.
    static bool HasSuspiciousIntegerExtension(params string[] raws)
    {
        var integers=raws.Select(raw=>Regex.Replace(raw,@"\s+","").Trim())
            .Where(raw=>Regex.IsMatch(raw,@"^\d{2,5}$"))
            .Distinct(StringComparer.Ordinal).ToArray();
        return integers.Any(shorter=>integers.Any(longer=>
            longer.Length>=shorter.Length+2 &&
            longer.StartsWith(shorter,StringComparison.Ordinal)));
    }

    // Pixel-level decimal evidence for tiny 352px LIVE chips. Tesseract often
    // reads 3.4K+ as 34K+. We inspect the narrow gap after the FIRST digit,
    // not the OCR string, and never blindly divide a true 34K+ by ten.
    // A tiny isolated light blob at the baseline of the numeric glyphs is
    // evidence of a decimal POINT. A negative result is inconclusive.
    static bool HasDecimalDotPixels(Bitmap image,Rectangle roi,double sx,double sy)
    {
        if(roi.IsEmpty)return false;
        int stepX=Math.Max(1,(int)Math.Round(sx));
        int stepY=Math.Max(1,(int)Math.Round(sy));
        for(int logicalX=8;logicalX<=10;logicalX++)
        {
            int x=roi.Left+(int)Math.Round(logicalX*sx);
            int y10=roi.Top+(int)Math.Round(10*sy);
            int y11=roi.Top+(int)Math.Round(11*sy);
            int y12=roi.Top+(int)Math.Round(12*sy);
            int y13=roi.Top+(int)Math.Round(13*sy);
            if(x-stepX<0 || x+stepX>=image.Width || y13>=image.Height)continue;
            static int Light(Color c)=>Math.Min(c.R,Math.Min(c.G,c.B));
            int before=Math.Max(Light(image.GetPixel(x,y10)),Light(image.GetPixel(x,y11)));
            int rise=Light(image.GetPixel(x,y12));
            int peak=Light(image.GetPixel(x,y13));
            int left=Light(image.GetPixel(x-stepX,y13));
            int right=Light(image.GetPixel(x+stepX,y13));
            // Require baseline-only brightening plus a narrow, isolated dot.
            // More than one bright neighbor suggests this is a digit stroke.
            if(peak-before>=52 && rise-before>=17 &&
                peak-Math.Min(left,right)>=40 &&
                peak>=Math.Max(105,before+52))return true;
        }
        return false;
    }

    static (int? Value,string Rule) ReconcileWithDecimalPixels(
        string tightRaw,string wideRaw,string contrastRaw,bool hasDot)
    {
        var (value,rule)=ReconcileViewerOcr(tightRaw,wideRaw,contrastRaw);
        if(!value.HasValue || rule!="SameImageAgreement")return (value,rule);
        // Only two digits + K/M are ambiguous at very low resolution.
        // Examples: 34K may really be 34K or 3.4K; 7.7K read as 77K.
        // OCR may return just "K+" in a weak preprocessing variant.
        // Ignore unparsable variants rather than treating them as proof of no dot.
        var observed=new[]{tightRaw,wideRaw,contrastRaw}
            .Select(v=>v.Trim().ToUpperInvariant())
            .Where(v=>Count(v).HasValue).ToArray();
        bool ambiguous=observed.Length>=1 && observed.All(v=>
            Regex.IsMatch(v,@"^\d{2}[KM]\+?$"));
        if(!ambiguous)return (value,rule);
        if(!hasDot)return (null,"TwoDigitScaleAmbiguous");
        var first=Regex.Match(observed[0],@"^(\d)(\d)([KM])\+?$",RegexOptions.CultureInvariant);
        if(!first.Success)return (null,"TwoDigitScaleAmbiguous");
        string restored=first.Groups[1].Value+"."+first.Groups[2].Value+
            first.Groups[3].Value;
        return (Count(restored),"DotPixelsRecoveredCandidate");
    }

    static string ShortOcr(string s)
    {
        s=Regex.Replace(s,@"\s+"," ").Trim();
        return s.Length<=24?s:s[..24];
    }

    // Extract UI screen size from Page Source; screenshot pixels might be scaled.
    static (double x,double y) Scale(string xml,Bitmap screenshot)
    {
        try
        {
            var hierarchy=XDocument.Parse(xml).Root;
            int w=(int?)hierarchy?.Attribute("width")??0;
            int h=(int?)hierarchy?.Attribute("height")??0;
            if(w>0 && h>0)return ((double)screenshot.Width/w,(double)screenshot.Height/h);
        }
        catch(System.Xml.XmlException){}
        return (1,1);
    }

    static Rectangle ScaledRect(double x,double y,double width,double height,double sx,double sy) =>
        new((int)Math.Round(x*sx),(int)Math.Round(y*sy),
            Math.Max(1,(int)Math.Round(width*sx)),Math.Max(1,(int)Math.Round(height*sy)));

    // All OCR regions inside a card are expressed as normalized fractions.
    // The XML tile remains the source of truth; screenshot scale is applied only
    // after the relative rectangle has been calculated.
    static Rectangle NormalizedTileRect(LiveSearchTile tile,double left,double top,
        double width,double height,double sx,double sy)
    {
        left=Math.Clamp(left,0,1);top=Math.Clamp(top,0,1);
        width=Math.Clamp(width,0,1-left);height=Math.Clamp(height,0,1-top);
        return ScaledRect(tile.X+tile.Width*left,tile.Y+tile.Height*top,
            tile.Width*width,tile.Height*height,sx,sy);
    }

    // Image-only LIVE cards may show a white PERSON silhouette (viewers) or
    // a white THUMBS-UP (likes). Both contain white pixels; simply testing
    // "iconPixels=True" confuses Like counts with audience size.
    //
    // These normalized binary silhouettes were sampled from actual 352x624
    // cloud-phone badges; classify conservatively. Unrecognized rendering,
    // scale, or icon style MUST be Unknown, never presumed Person.
    enum BadgeIcon { Unknown, Person, Like }

    static readonly string[][] PersonMasks =
    {
        new[] {
            "...............", ".......##......", "......####.....",
            "......####.....", "........#......", "...............",
            "......####.....", ".....######....", ".....######....",
            "..............."
        },
        new[] {
            "...............", "......###......", "......####.....",
            "......####.....", ".......##......", "...............",
            "......####.....", ".....######....", ".....######....",
            "..............."
        }
    };
    static readonly string[][] LikeMasks =
    {
        new[] {
            "...............", "........##.....", ".......###.....",
            "......####.....", "....#.######...", "....#.######...",
            "....#.######...", "....#.######...", ".......####....",
            "..............."
        },
        new[] {
            ".........#.....", "........##.....", ".......###.....",
            "......######...", "....#.######...", "....#.######...",
            "....#.######...", "....#.######...", "......#####....",
            "..............."
        }
    };

    static double SilhouetteMatch(bool[,] observed,string[] sample)
    {
        int intersection=0, observedCount=0, sampleCount=0;
        for(int y=0;y<10;y++)for(int x=0;x<15;x++)
        {
            bool actual=observed[y,x], expected=sample[y][x]=='#';
            if(actual)observedCount++;
            if(expected)sampleCount++;
            if(actual && expected)intersection++;
        }
        return observedCount+sampleCount==0?0:
            2.0*intersection/(observedCount+sampleCount);
    }

    // A person has a separated head and shoulders at this canonical scale.
    // This independent geometry test prevents an alignment search from turning
    // a solid thumbs-up or arbitrary white text into an audience icon.
    static bool HasSeparatedHeadAndShoulders(bool[,] mask)
    {
        int head=0,body=0,gap=0;
        for(int x=3;x<=12;x++)
        {
            if(mask[2,x] || mask[3,x])head++;
            if(mask[6,x] || mask[7,x])body++;
            if(x>=4 && x<=11 && mask[5,x])gap++;
        }
        return head>=3 && body>=5 && gap<=1;
    }

    // The raw badge is typically only 14-16 pixels wide at 352px viewport.
    // Moving the sample by ONE physical pixel can lower the old Person score
    // from 0.74 to 0.70 (Phone 03/04) without any icon or DPI change.
    // Sample a bounded set of crop alignments derived from the detected icon
    // size, not from a device ID. Compare Person/Like on EACH SAME alignment
    // and require a separated head-body structure for Person. Do not simply
    // take the largest Person score across every crop and discard Like evidence.
    static (BadgeIcon Kind,double PersonScore,double LikeScore,
        Rectangle Region,string Alignment) DetectBadgeIcon(
        Bitmap image,LiveSearchTile tile,int redRight,int redTop,double sx,double sy)
    {
        var baseline=BadgeIconRegion(image,tile,redRight,redTop,sx,sy);
        if(baseline.Width<8 || baseline.Height<8)
            return (BadgeIcon.Unknown,0,0,baseline,"no-region");

        // Small, bounded adjustments are proportional to the *screenshot*
        // icon size. No per-phone configuration, no unsafe confidence decrease.
        int trimW=Math.Max(1,(int)Math.Round(baseline.Width*0.125));
        int trimH=Math.Max(1,(int)Math.Round(baseline.Height*0.14));
        int shiftX=Math.Max(1,(int)Math.Round(baseline.Width*0.125));
        int shiftY=Math.Max(1,(int)Math.Round(baseline.Height*0.07));
        var trials=new (int Dx,int Dy,int Dw,int Dh)[] {
            (0,0,0,0),
            (0,shiftY,0,-trimH),
            (shiftX/2,shiftY,0,-trimH),
            (shiftX,shiftY,-trimW,-trimH),
            (0,shiftY,-trimW,-trimH),
            (0,0,-trimW,0),
            (shiftX/2,0,-trimW,0),
            (shiftX,0,-trimW,0),
            (0,0,0,-trimH),
            (shiftX/2,0,0,-trimH)
        };
        double bestPerson=0,bestLike=0;
        double personRival=0,likeRival=0;
        Rectangle personRegion=baseline,likeRegion=baseline;
        int bestPersonTrial=-1,bestLikeTrial=-1;
        for(int t=0;t<trials.Length;t++)
        {
            var v=trials[t];
            var crop=new Rectangle(baseline.Left+v.Dx,baseline.Top+v.Dy,
                baseline.Width+v.Dw,baseline.Height+v.Dh);
            if(crop.Width<8 || crop.Height<7 || crop.Left<0 || crop.Top<0 ||
                crop.Right>image.Width || crop.Bottom>image.Height)continue;
            var mask=new bool[10,15];
            for(int y=0;y<10;y++)for(int x=0;x<15;x++)
            {
                int px=crop.Left+Math.Min(crop.Width-1,
                    (int)Math.Floor((x+0.5)*crop.Width/15d));
                int py=crop.Top+Math.Min(crop.Height-1,
                    (int)Math.Floor((y+0.5)*crop.Height/10d));
                var c=image.GetPixel(px,py);
                mask[y,x]=Math.Min(c.R,Math.Min(c.G,c.B))>=185;
            }
            double person=PersonMasks.Max(p=>SilhouetteMatch(mask,p));
            double like=LikeMasks.Max(p=>SilhouetteMatch(mask,p));
            // The old 0.72/0.12 thresholds are intentionally retained.
            if(person>=0.72 && person-like>=0.12 &&
                HasSeparatedHeadAndShoulders(mask) && person>bestPerson)
            {
                bestPerson=person;
                personRival=like;
                bestPersonTrial=t;
                personRegion=crop;
            }
            if(like>=0.72 && like-person>=0.12 && like>bestLike)
            {
                bestLike=like;
                likeRival=person;
                bestLikeTrial=t;
                likeRegion=crop;
            }
            // Do not force an Unknown to be a Person: downstream OCR is only
            // allowed when the icon has structural and template evidence.
        }
        if(bestPersonTrial>=0 && bestLikeTrial<0)
            return (BadgeIcon.Person,bestPerson,personRival,personRegion,
                $"adaptive-{bestPersonTrial}");
        if(bestLikeTrial>=0 && bestPersonTrial<0)
            return (BadgeIcon.Like,likeRival,bestLike,likeRegion,
                $"adaptive-{bestLikeTrial}");
        // Ambiguous across crop alignments -> abstain instead of misreading
        // TikTok Like counts as LIVE viewers.
        return (BadgeIcon.Unknown,bestPerson,bestLike,baseline,
            bestPersonTrial>=0?"ambiguous-person-like":"no-valid-silhouette");
    }

    // Use the measured red LIVE label Y coordinate, NOT the visible tile height.
    // The tile bottom can be clipped by the screen and change from 321px to
    // 153px even though the LIVE badge always renders at the same pixel height.
    static Rectangle BadgeIconRegion(Bitmap image,LiveSearchTile tile,
        int redRight,int redTop,double sx,double sy)
    {
        if(redRight<0 || redTop<0)return Rectangle.Empty;
        int iconLeft=redRight+Math.Max(1,(int)Math.Round(tile.Width*0.006*sx));
        int iconTop=redTop;
        int iconWidth=Math.Max(8,(int)Math.Round(tile.Width*0.09*sx));
        int iconHeight=Math.Max(10,(int)Math.Round(tile.Width*0.081*sy));
        return Rectangle.Intersect(new Rectangle(iconLeft,iconTop,iconWidth,iconHeight),
            new Rectangle(0,0,image.Width,image.Height));
    }

    // TikTok renders LIVE search labels and audience icons as pixels. The
    // original single scan row (tile.Y + tile.Height*0.032) missed visible
    // lower-row badges because their tiles were clipped by the viewport.
    // Scan the first 24px of the card and accept only a long red label whose
    // left edge is close to the card edge, then anchor the OCR to that label.
    static Rectangle ViewerNumberRoi(Bitmap screenshot,LiveSearchTile tile,double sx,double sy,
        out int redRight,out int redTop)
    {
        redRight=-1;
        redTop=-1;
        int scanStart=Math.Max(0,(int)Math.Round((tile.X+tile.Width*0.02)*sx));
        int scanEnd=Math.Min(screenshot.Width-1,
            (int)Math.Round((tile.X+tile.Width*0.49)*sx));
        int top=Math.Max(0,(int)Math.Round((tile.Y+2)*sy));
        int bottom=Math.Min(screenshot.Height-1,
            (int)Math.Round((tile.Y+Math.Min(24,tile.Height-1))*sy));
        int required=Math.Max(7,(int)Math.Round(tile.Width*0.085*sx));
        int allowedStart=(int)Math.Round((tile.X+tile.Width*0.105)*sx);
        // Rank first by red-run length, then by earliest Y. Real LIVE labels
        // have a solid horizontal top edge; a later red video patch must not
        // shift our icon/number crop downward.
        int bestLength=0;
        var redCandidates=new List<(int Row,int Right,int Length)>();
        for(int row=top;row<=bottom;row++)
        {
            int runStart=-1;
            for(int x=scanStart;x<=scanEnd+1;x++)
            {
                bool red=false;
                if(x<=scanEnd)
                {
                    var c=screenshot.GetPixel(x,row);
                    red=c.R>160 && c.G<160 && c.R>c.G*1.6 && c.R>c.B*1.1;
                }
                if(red && runStart<0)runStart=x;
                if(!red && runStart>=0)
                {
                    int len=x-runStart;
                    if(runStart<=allowedStart && len>=required)
                    {
                        redCandidates.Add((row,x-1,len));
                        bestLength=Math.Max(bestLength,len);
                    }
                    runStart=-1;
                }
            }
        }
        // Anti-aliased label corners are often 1-2px shorter than the full
        // rectangle. Prefer the TOP row within two pixels of the longest run,
        // otherwise the icon crop shifts down and a Person becomes Unknown.
        var badge=redCandidates.Where(c=>c.Length>=bestLength-2)
            .OrderBy(c=>c.Row).ThenByDescending(c=>c.Length).FirstOrDefault();
        if(badge.Length==0)return Rectangle.Empty;
        redTop=badge.Row;
        redRight=badge.Right;
        // Use tile WIDTH for height and offsets: the visual badge does not
        // shrink when a card is only partly visible. Start after Person/Like.
        int left=redRight+(int)Math.Round(tile.Width*0.09*sx);
        int roiTop=redTop-Math.Max(2,(int)Math.Round(tile.Width*0.017*sy));
        var roi=new Rectangle(left,roiTop,
            Math.Max(25,(int)Math.Round(tile.Width*0.38*sx)),
            Math.Max(14,(int)Math.Round(tile.Width*0.12*sy)));
        roi=Rectangle.Intersect(roi,new Rectangle(0,0,screenshot.Width,screenshot.Height));
        return roi.Width>=20 && roi.Height>=12?roi:Rectangle.Empty;
    }

    // Store precisely what OCR saw, not just the full viewport screenshot.
    // Diagnostics are local-only; a separate folder is created for each search.
    static void SaveBadgeCrop(Bitmap screen,Rectangle region,string folder,
        int cardIndex,string suffix)
    {
        if(region.IsEmpty)return;
        try
        {
            Directory.CreateDirectory(folder);
            region=Rectangle.Intersect(region,new Rectangle(0,0,screen.Width,screen.Height));
            if(region.Width<6 || region.Height<6)return;
            using var cut=screen.Clone(region,screen.PixelFormat);
            cut.Save(Path.Combine(folder,$"badge-{cardIndex+1:D2}-{suffix}.png"),
                System.Drawing.Imaging.ImageFormat.Png);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException)
        { /* Diagnostics must never abort the search. */ }
    }

    // Re-read from an independently acquired screenshot of the SAME viewport.
    // Two captures agreeing do NOT prove the LIVE audience count is exact
    // (TikTok abbreviates numbers, and both OCR shots may repeat an error).
    // Preserve provisional evidence, never mark OcrConfirmed/Verified.
    public static IReadOnlyList<LiveSearchCard> CombineTwoCaptures(
        IReadOnlyList<LiveSearchCard> first,IReadOnlyList<LiveSearchCard> second,
        Action<string> log)
    {
        if(first.Count!=second.Count)return first;
        var merged=new List<LiveSearchCard>(first.Count);
        for(int i=0;i<first.Count;i++)
        {
            var a=first[i];var b=second[i];
            bool personA=a.Note.Contains("icon=Person",StringComparison.Ordinal);
            bool personB=b.Note.Contains("icon=Person",StringComparison.Ordinal);
            if(personA && personB && a.Viewers.HasValue &&
                a.Viewers==b.Viewers && b.ViewerEvidence=="OcrCandidate")
            {
                merged.Add(a with {
                    ViewerEvidence="OcrRepeatedCandidate",
                    Note="Hai ảnh độc lập cùng ước lượng "+a.Viewers.Value.ToString("N0")+
                        " Viewer; vẫn chưa xác minh để tự động chọn phòng. "+a.Note
                });
                log($"OCR card {i+1}/{first.Count}: hai ảnh cùng đọc " +
                    $"{a.Viewers.Value:N0} Viewer (CHƯA XÁC MINH). ");
            }
            else
            {
                // When frames disagree, keep the visible candidates in the
                // note but don't silently choose the higher or lower count.
                if(personA && personB && a.Viewers.HasValue && b.Viewers.HasValue &&
                    a.Viewers!=b.Viewers)
                {
                    merged.Add(a with {Viewers=null,ViewerEvidence="OcrFrameConflict",
                        Note=$"Hai ảnh OCR mâu thuẫn: {a.Viewers:N0} / {b.Viewers:N0}; không chọn số."});
                    log($"OCR card {i+1}/{first.Count}: hai ảnh cho Viewer KHÁC NHAU; cần xem ảnh.");
                }
                else merged.Add(a);
            }
        }
        return merged;
    }

    public static async Task<IReadOnlyList<LiveSearchCard>?> TryEnhanceAsync(
        AppiumSession session,string xml,IReadOnlyList<LiveSearchCard> cards,
        Action<string> log,CancellationToken ct,bool readTitles=true,
        IReadOnlySet<int>? onlyIndices=null,string? diagnosticDirectory=null)
    {
        var exe=FindExecutable();
        if(exe is null)
        {
            log("Bộ đọc ảnh LIVE: XML không có tiêu đề/Viewer; máy chưa tìm thấy Tesseract OCR. " +
                "Cài Tesseract (eng) hoặc đặt TESSERACT_EXE rồi mở lại tool; không tự suy diễn số Viewer.");
            return null;
        }
        var tiles=LiveSearchCardReader.TileBounds(xml);
        if(tiles.Count!=cards.Count || tiles.Count==0)
        {
            log("Bộ đọc ảnh LIVE: số vùng card và XML không khớp; bỏ qua OCR để tránh ghép nhầm.");
            return null;
        }
        byte[] png;
        try {png=await session.ScreenshotPngAsync(ct);}
        catch(OperationCanceledException){throw;}
        catch(Exception ex){log($"Không chụp được ảnh LIVE cho OCR ({ex.GetType().Name}).");return null;}
        using var ms=new MemoryStream(png);
        using var screenshot=new Bitmap(ms);
        var (sx,sy)=Scale(xml,screenshot);
        var dir=Path.Combine(Path.GetTempPath(),"AndroidMultiDevice-LiveOcr");
        Directory.CreateDirectory(dir);
        string? automaticFailureDirectory=null;
        string FailureDirectory()
        {
            if(diagnosticDirectory is not null)return diagnosticDirectory;
            return automaticFailureDirectory??=Path.Combine(Storage.Folder,"diagnostics",
                session.DeviceKey,"live-ocr-failures",
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture));
        }
        var result=new List<LiveSearchCard>();
        try
        {
        for(int i=0;i<cards.Count;i++)
        {
            ct.ThrowIfCancellationRequested();
            var card=cards[i];var tile=tiles[i];
            if(onlyIndices is not null && !onlyIndices.Contains(i))
            {
                result.Add(card);
                continue;
            }
            // Read only the numeric portion of the dark LIVE badge.
            // The person-icon is excluded by locating the variable-width red
            // label first. A single screenshot remains only a candidate.
            var tight=ViewerNumberRoi(screenshot,tile,sx,sy,out int redRight,out int redTop);
            if(diagnosticDirectory is not null)
            {
                // Capture 90px across tile header even if the badge locator fails.
                SaveBadgeCrop(screenshot,ScaledRect(tile.X,tile.Y,
                    Math.Min(tile.Width,110),Math.Min(tile.Height,28),sx,sy),
                    diagnosticDirectory,i,"header");
                SaveBadgeCrop(screenshot,tight,diagnosticDirectory,i,"roi");
            }
            if(tight==Rectangle.Empty)
            {
                string failureDir=FailureDirectory();
                SaveBadgeCrop(screenshot,NormalizedTileRect(tile,0,0,0.65,0.14,sx,sy),
                    failureDir,i,"FAILED-badge-not-found");
                // A bare tile rectangle does not prove the LIVE viewer badge
                // is visible. No hard-coded fallback that might read Like data.
                result.Add(card with {
                    Viewers=null,ViewerEvidence="BadgeNotFound",
                    Note="Không xác định được nhãn đỏ LIVE để cắt Viewer; giữ nguyên dữ liệu XML."
                });
                log($"OCR card {i+1}/{cards.Count}: không định vị được badge LIVE; không đoán số.");
                continue;
            }
            // A numeric OCR result is never a viewer count without confirming
            // the PERSON icon. Like icons and unknown glyphs are excluded before
            // running any OCR; a white-pixel test is not sufficient.
            var icon=DetectBadgeIcon(screenshot,tile,redRight,redTop,sx,sy);
            if(icon.Kind!=BadgeIcon.Person)
            {
                string excluded=icon.Kind==BadgeIcon.Like?"IconLikeIgnored":"IconUnknown";
                if(icon.Kind==BadgeIcon.Unknown)
                {
                    string failureDir=FailureDirectory();
                    SaveBadgeCrop(screenshot,tight,failureDir,i,"FAILED-viewer-roi");
                    SaveBadgeCrop(screenshot,icon.Region,
                        failureDir,i,"FAILED-icon-unknown");
                    SaveBadgeCrop(screenshot,NormalizedTileRect(tile,0,0,0.65,0.14,sx,sy),
                        failureDir,i,"FAILED-header-context");
                    log($"OCR_DIAGNOSTIC_SAVED: card={i+1}; reason=IconUnknown; folder={failureDir}.");
                }
                result.Add(card with {
                    Viewers=null,ViewerEvidence=excluded,
                    Note=icon.Kind==BadgeIcon.Like
                        ?"Badge dùng biểu tượng Like, không phải Viewer; bỏ qua số."
                        :"Không xác minh được biểu tượng người; không sử dụng số badge."
                });
                log($"OCR card {i+1}/{cards.Count}: " +
                    (icon.Kind==BadgeIcon.Like?"icon Like — BỎ QUA số Like":
                        "icon không rõ — không tính Viewer") +
                    $"; personScore={icon.PersonScore:F2}, likeScore={icon.LikeScore:F2}; " +
                    $"iconAlign={icon.Alignment}; iconRect={icon.Region}.");
                continue;
            }
            // Variant OCR preprocessing of ONE tightly cropped number region;
            // these are not independent observations and remain OcrCandidate.
            var tightRaw=await OcrAsync(exe,screenshot,tight,dir,7,ct);
            var wideRaw=await OcrAsync(exe,screenshot,tight,dir,8,ct);
            var contrastRaw=await OcrAsync(exe,screenshot,tight,dir,7,ct,true);
            // The dot detector works only for abbreviated counts like 3.4K+.
            // On plain integers, it can see a false dot from anti-aliasing
            // (e.g. 533 or 55) and must not influence the interpretation.
            bool abbreviatedChip=new[]{tightRaw,wideRaw,contrastRaw}.Any(raw=>
                Regex.IsMatch(raw.Trim(),@"[kKmM]\+?$"));
            bool decimalPixel=abbreviatedChip &&
                HasDecimalDotPixels(screenshot,tight,sx,sy);
            var (viewer,reconciliation)=ReconcileWithDecimalPixels(
                tightRaw,wideRaw,contrastRaw,decimalPixel);
            string verificationRaw="";
            if(!viewer.HasValue || reconciliation=="OcrConflict")
            {
                // Independent segmentation mode: try to recover a number,
                // but do not accept a lone weak guess as a verified count.
                verificationRaw=await OcrAsync(exe,screenshot,tight,dir,13,ct);
                int? verify=Count(verificationRaw);
                var candidates=new[]{Count(tightRaw),Count(wideRaw),Count(contrastRaw)};
                if(verify.HasValue && candidates.Count(v=>v==verify)>=1)
                {
                    viewer=verify;
                    reconciliation="AlternateModeAgreesCandidate";
                }
            }
            // CRITICAL: On a 352px screenshot '227' became '22770' in
            // wide+alternate OCR, producing a false highest-viewer winner.
            // Repeated screenshots with the same preprocessing do not fix it.
            // Flag a conflict instead of selecting the artificially long value.
            if(HasSuspiciousIntegerExtension(tightRaw,wideRaw,contrastRaw,verificationRaw))
            {
                viewer=null;
                reconciliation="SuspiciousIntegerExtension";
            }
            string state=(reconciliation=="OcrConflict" ||
                reconciliation=="SuspiciousIntegerExtension")?"OcrConflict":
                viewer.HasValue?"OcrCandidate":"Không đọc được badge người xem";
            // Never call two preprocessings of the same image OcrConfirmed.
            // Evidence for ranking must come from an independent source or
            // explicit operator confirmation of the viewer badge.
            string rawNote=$"OCR thô tight='{ShortOcr(tightRaw)}', wide='{ShortOcr(wideRaw)}', " +
                $"contrast='{ShortOcr(contrastRaw)}'; icon=Person, " +
                $"personScore={icon.PersonScore:F2}, likeScore={icon.LikeScore:F2}; " +
                $"iconAlign={icon.Alignment}; " +
                $"dotPixels={(abbreviatedChip?decimalPixel.ToString():"n/a")}; " +
                $"alternate='{ShortOcr(verificationRaw)}'; quy tắc={reconciliation}";
            string title=card.Title;
            string creator=card.Creator;
            if(readTitles && (title=="Chưa đọc được tiêu đề" ||
                creator=="Chưa đọc được chủ phòng") && tile.Height>230)
            {
                // Title/host metadata occupies the lower part of each card.
                // Crop it proportionally so density, navigation-bar height and
                // small TikTok layout changes do not shift the text out of ROI.
                var titleRegion=NormalizedTileRect(tile,0.02,0.69,0.96,0.29,sx,sy);
                var normalRaw=await OcrAsync(exe,screenshot,titleRegion,dir,6,ct);
                var contrastMetadataRaw=await OcrAsync(exe,screenshot,titleRegion,dir,6,ct,true);
                static int MetadataScore(string value)=>value.Count(c=>char.IsLetterOrDigit(c));
                string raw=MetadataScore(contrastMetadataRaw)>MetadataScore(normalRaw)
                    ?contrastMetadataRaw:normalRaw;
                var lines=raw.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
                    .Select(t=>Regex.Replace(t.Trim(),@"\s+"," "))
                    .Where(t=>t.Length>=5 && t.Any(char.IsLetter)).ToList();
                if(title=="Chưa đọc được tiêu đề" && lines.Count>0)
                    title="OCR (ước lượng): "+lines[0][..Math.Min(90,lines[0].Length)];
                if(creator=="Chưa đọc được chủ phòng" && lines.Count>1)
                    creator=lines[1][..Math.Min(60,lines[1].Length)];
                if(lines.Count==0)
                {
                    string failureDir=FailureDirectory();
                    SaveBadgeCrop(screenshot,titleRegion,failureDir,i,"FAILED-title-host-roi");
                    log($"OCR_DIAGNOSTIC_SAVED: card={i+1}; reason=TitleHostUnreadable; folder={failureDir}.");
                }
            }
            // Never turn a title OCR error into a stable creator/room ID.
            var fingerprint=card.Fingerprint;
            if(title.StartsWith("OCR (ước lượng):",StringComparison.Ordinal))
                fingerprint=title.ToLowerInvariant()+"|"+(viewer?.ToString()??"?");
            result.Add(card with {
                Title=title,Creator=creator,Viewers=viewer,ViewerEvidence=state,Fingerprint=fingerprint,
                Note=state=="OcrConflict"?"Các vùng OCR cho số khác nhau; "+rawNote:
                     state=="OcrCandidate"?"Chỉ là ứng viên từ một ảnh, chưa xác minh độc lập; "+rawNote:
                     "Không đọc được badge người xem; "+rawNote
            });
            log($"OCR card {i+1}/{cards.Count}: " +
                (viewer.HasValue?$"{viewer:N0} Viewer (CHƯA XÁC MINH)":
                    (reconciliation=="OcrConflict" || reconciliation=="SuspiciousIntegerExtension")?
                    "OCR xung đột, không chọn số":"không đọc được Viewer") +
                $"; {rawNote}.");
            if(!viewer.HasValue)
            {
                string failureDir=FailureDirectory();
                SaveBadgeCrop(screenshot,tight,failureDir,i,"FAILED-viewer-unreadable");
                SaveBadgeCrop(screenshot,NormalizedTileRect(tile,0,0,0.65,0.14,sx,sy),
                    failureDir,i,"FAILED-viewer-context");
                log($"OCR_DIAGNOSTIC_SAVED: card={i+1}; reason={reconciliation}; folder={failureDir}.");
            }
        }
        return result;
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException)
        {
            log($"Không thể chạy bộ OCR ảnh LIVE ({ex.GetType().Name}); giữ dữ liệu XML, không đoán Viewer.");
            return null;
        }
    }
}
