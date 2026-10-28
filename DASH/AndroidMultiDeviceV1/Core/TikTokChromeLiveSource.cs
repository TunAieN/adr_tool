using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;

namespace AndroidMultiDeviceV1.Core;

public sealed record BrowserLiveCandidate(string Keyword,string Username,string Title,
    int Viewers,string LiveUrl,string ViewerEvidence="PersonSvgAdjacent");

/// <summary>
/// Discovers LIVE candidates through a dedicated Chrome profile and localhost
/// CDP. It never attaches to, focuses, or changes the user's normal Chrome.
/// </summary>
public sealed class TikTokChromeLiveSource
{
    static readonly SemaphoreSlim BrowserGate=new(1,1);
    static readonly object CacheGate=new();
    static readonly Dictionary<string,(DateTimeOffset At,IReadOnlyList<BrowserLiveCandidate> Cards)> Cache=
        new(StringComparer.OrdinalIgnoreCase);

    readonly LiveManagerSettings _config;
    readonly Action<string> _log;

    public TikTokChromeLiveSource(LiveManagerSettings config,Action<string> log)
    {
        _config=config;
        _log=log;
    }

    public async Task<BrowserLiveCandidate?> FindBestAsync(string keyword,CancellationToken ct)
    {
        var candidates=await FindCandidatesAsync(keyword,ct);
        var selected=candidates.FirstOrDefault();
        if(selected is not null)
            _log($"CHROME_CDP_SELECTED: keyword=“{keyword.Trim()}”; host=@{selected.Username}; " +
                $"webViewer={selected.Viewers:N0}; evidence={selected.ViewerEvidence}; " +
                "likeCountAccepted=false; next=ANDROID_SEARCH_EXACT_USERNAME.");
        return selected;
    }

    public async Task<IReadOnlyList<BrowserLiveCandidate>> FindCandidatesAsync(
        string keyword,CancellationToken ct)
    {
        keyword=keyword.Trim();
        await BrowserGate.WaitAsync(ct);
        try
        {
            IReadOnlyList<BrowserLiveCandidate> cards;
            lock(CacheGate)
            {
                cards=Cache.TryGetValue(keyword,out var cached) &&
                    DateTimeOffset.UtcNow-cached.At<TimeSpan.FromSeconds(15)
                    ? cached.Cards : [];
            }
            if(cards.Count>0)
                _log($"CHROME_CDP_CACHE_HIT: keyword=“{keyword}”; cards={cards.Count}.");
            else
            {
                cards=await ScrapeAsync(keyword,ct);
                lock(CacheGate)Cache[keyword]=(DateTimeOffset.UtcNow,cards);
            }

            var eligible=cards.Where(c=>c.Viewers>=_config.MinimumViewers &&
                    (_config.MaximumViewers==0 || c.Viewers<=_config.MaximumViewers))
                .OrderByDescending(c=>c.Viewers).ThenBy(c=>c.Username,StringComparer.OrdinalIgnoreCase)
                .ToList();
            _log($"CHROME_CDP_FILTER: keyword=“{keyword}”; cards={cards.Count}; " +
                $"eligible={eligible.Count}; range=[{_config.MinimumViewers},"+
                $"{(_config.MaximumViewers==0?"INF":_config.MaximumViewers.ToString(CultureInfo.InvariantCulture))}].");
            _log($"CHROME_CDP_QUEUE_SAVED: keyword=“{keyword}”; items={eligible.Count}; " +
                "order=VIEWER_DESC; likeCountAccepted=false.");
            return eligible;
        }
        finally{BrowserGate.Release();}
    }

    async Task<IReadOnlyList<BrowserLiveCandidate>> ScrapeAsync(string keyword,CancellationToken ct)
    {
        string url="https://www.tiktok.com/search/live?q="+Uri.EscapeDataString(keyword)+
            "&t="+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        await using var page=await TikTokChromeCdpBrowser.OpenPageAsync(_log,ct);
        await page.NavigateAsync(url,ct);
        _log($"CHROME_CDP_NAVIGATE: keyword=“{keyword}”; profile=DEDICATED; endpoint=LOCALHOST.");

        var deadline=DateTimeOffset.UtcNow.AddSeconds(_config.ChromePageLoadTimeoutSeconds);
        CdpLiveScan scan=new();
        int stableReads=0,previousCount=-1;
        while(DateTimeOffset.UtcNow<deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500,ct);
            scan=await page.ReadLiveCardsAsync(ct);
            if(scan.Cards.Count>0 && scan.Cards.Count==previousCount)stableReads++;
            else stableReads=0;
            previousCount=scan.Cards.Count;
            if(scan.Cards.Count>0 && stableReads>=2)break;
        }
        _log($"CHROME_CDP_READ: keyword=“{keyword}”; liveLinks={scan.Stats.LiveLinks}; " +
            $"cards={scan.Cards.Count}; viewerSvgAccepted={scan.Stats.ViewerSvgAccepted}; " +
            $"likeSvgRejected={scan.Stats.LikeSvgRejected}; ambiguousRejected={scan.Stats.AmbiguousRejected}; " +
            $"stable={stableReads>=2}.");
        if(scan.Challenge && scan.Cards.Count==0)
            throw new InvalidOperationException(
                "Profile Chrome CDP đang gặp màn hình đăng nhập/xác minh/CAPTCHA. Hãy xử lý thủ công trong cửa sổ profile riêng rồi chạy lại.");
        return scan.Cards.Select(card=>new BrowserLiveCandidate(keyword,card.Username,
            card.Title,card.Viewers,card.LiveUrl,card.Evidence)).ToList();
    }
}

public static class TikTokChromeCdpBrowser
{
    static readonly SemaphoreSlim StartGate=new(1,1);
    static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(3)};

    public static string ProfileDirectory => Path.Combine(Storage.Folder,"chrome-live-cdp-profile");

    public static async Task<CdpPage> OpenPageAsync(Action<string> log,CancellationToken ct)
    {
        var endpoint=await EnsureEndpointAsync(log,ct);
        using var list=await GetJsonAsync(endpoint.Port,"/json/list",ct);
        JsonElement? target=null;
        foreach(var item in list.RootElement.EnumerateArray())
        {
            if((item.TryGetProperty("type",out var type)?type.GetString():"")!="page")continue;
            string url=item.TryGetProperty("url",out var value)?value.GetString()??"":"";
            if(url.Contains("tiktok.com/search/live",StringComparison.OrdinalIgnoreCase))
            {target=item.Clone();break;}
            if(target is null && (url=="about:blank" || url.StartsWith("chrome://newtab",StringComparison.OrdinalIgnoreCase)))
                target=item.Clone();
        }
        if(target is null)
        {
            using var created=await SendJsonAsync(endpoint.Port,HttpMethod.Put,
                "/json/new?about%3Ablank",ct);
            target=created.RootElement.Clone();
        }
        string? webSocket=target.Value.TryGetProperty("webSocketDebuggerUrl",out var ws)
            ?ws.GetString():null;
        if(string.IsNullOrWhiteSpace(webSocket))
            throw new InvalidOperationException("CDP không trả về WebSocket cho tab Chrome riêng.");
        var page=new CdpPage(new Uri(webSocket));
        await page.ConnectAsync(ct);
        await page.CommandAsync("Page.enable",null,ct);
        await page.CommandAsync("Runtime.enable",null,ct);
        await page.CommandAsync("Page.bringToFront",null,ct);
        return page;
    }

    static async Task<CdpEndpoint> EnsureEndpointAsync(Action<string> log,CancellationToken ct)
    {
        await StartGate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(ProfileDirectory);
            var existing=await TryReadEndpointAsync(ct);
            if(existing is not null)
            {
                log($"CHROME_CDP_ATTACH: profile={ProfileDirectory}; port={existing.Value.Port}; reused=true.");
                return existing.Value;
            }

            string activePort=Path.Combine(ProfileDirectory,"DevToolsActivePort");
            try{if(File.Exists(activePort))File.Delete(activePort);}catch(IOException){}
            string chrome=ResolveChromeExecutable();
            var start=new ProcessStartInfo(chrome)
            {
                UseShellExecute=false,
                CreateNoWindow=false,
                WorkingDirectory=Path.GetDirectoryName(chrome)??Environment.CurrentDirectory
            };
            start.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            start.ArgumentList.Add("--remote-debugging-port=0");
            start.ArgumentList.Add("--user-data-dir="+ProfileDirectory);
            start.ArgumentList.Add("--profile-directory=Default");
            start.ArgumentList.Add("--no-first-run");
            start.ArgumentList.Add("--no-default-browser-check");
            start.ArgumentList.Add("--disable-background-mode");
            start.ArgumentList.Add("--new-window");
            start.ArgumentList.Add("about:blank");
            var process=Process.Start(start)??throw new InvalidOperationException("Không khởi động được Google Chrome.");
            log($"CHROME_CDP_PROFILE_CREATED: profile={ProfileDirectory}; pid={process.Id}; " +
                "remoteAddress=127.0.0.1; requestedPort=AUTO.");

            var deadline=DateTimeOffset.UtcNow.AddSeconds(20);
            while(DateTimeOffset.UtcNow<deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(250,ct);
                var ready=await TryReadEndpointAsync(ct);
                if(ready is not null)
                {
                    log($"CHROME_CDP_READY: port={ready.Value.Port}; profile=DEDICATED; normalChromeUntouched=true.");
                    return ready.Value;
                }
            }
            throw new TimeoutException("Chrome profile riêng chưa mở cổng CDP trong 20 giây.");
        }
        finally{StartGate.Release();}
    }

    static async Task<CdpEndpoint?> TryReadEndpointAsync(CancellationToken ct)
    {
        string path=Path.Combine(ProfileDirectory,"DevToolsActivePort");
        if(!File.Exists(path))return null;
        string[] lines;
        try{lines=await File.ReadAllLinesAsync(path,ct);}catch(IOException){return null;}
        if(lines.Length<1 || !int.TryParse(lines[0],NumberStyles.None,CultureInfo.InvariantCulture,out int port) ||
           port is <1 or >65535)return null;
        try
        {
            using var version=await GetJsonAsync(port,"/json/version",ct);
            if(!version.RootElement.TryGetProperty("Browser",out var browser) ||
               !(browser.GetString()??"").Contains("Chrome",StringComparison.OrdinalIgnoreCase))return null;
            return new CdpEndpoint(port);
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException)
        {return null;}
    }

    static string ResolveChromeExecutable()
    {
        var paths=new[]{
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Google","Chrome","Application","chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google","Chrome","Application","chrome.exe")
        };
        return paths.FirstOrDefault(File.Exists)??throw new FileNotFoundException(
            "Không tìm thấy Google Chrome. Hãy cài Chrome trước khi dùng nguồn TikTok Web CDP.");
    }

    static Task<JsonDocument> GetJsonAsync(int port,string path,CancellationToken ct) =>
        SendJsonAsync(port,HttpMethod.Get,path,ct);

    static async Task<JsonDocument> SendJsonAsync(int port,HttpMethod method,string path,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,$"http://127.0.0.1:{port}{path}");
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream,cancellationToken:ct);
    }

    readonly record struct CdpEndpoint(int Port);
}

public sealed class CdpPage : IAsyncDisposable
{
    readonly ClientWebSocket _socket=new();
    readonly Uri _uri;
    int _nextId;

    internal CdpPage(Uri uri){_uri=uri;}
    internal Task ConnectAsync(CancellationToken ct)=>_socket.ConnectAsync(_uri,ct);
    internal Task NavigateAsync(string url,CancellationToken ct)=>CommandAsync("Page.navigate",new{url},ct);

    internal async Task<CdpLiveScan> ReadLiveCardsAsync(CancellationToken ct)
    {
        var response=await CommandAsync("Runtime.evaluate",new
        {
            expression=LiveCardScript,
            returnByValue=true,
            awaitPromise=false
        },ct);
        if(!response.TryGetProperty("result",out var outer) ||
           !outer.TryGetProperty("result",out var result) ||
           !result.TryGetProperty("value",out var value) || value.ValueKind!=JsonValueKind.String)
            return new CdpLiveScan();
        return JsonSerializer.Deserialize<CdpLiveScan>(value.GetString()??"{}",
            new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new CdpLiveScan();
    }

    internal async Task<JsonElement> CommandAsync(string method,object? parameters,CancellationToken ct)
    {
        int id=Interlocked.Increment(ref _nextId);
        byte[] payload=JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string,object?>
        {
            ["id"]=id,["method"]=method,["params"]=parameters??new{}
        });
        await _socket.SendAsync(payload,WebSocketMessageType.Text,true,ct);
        while(true)
        {
            using var data=new MemoryStream();
            var buffer=new byte[16384];
            WebSocketReceiveResult receive;
            do
            {
                receive=await _socket.ReceiveAsync(buffer,ct);
                if(receive.MessageType==WebSocketMessageType.Close)
                    throw new InvalidOperationException("Chrome đã đóng kết nối CDP.");
                data.Write(buffer,0,receive.Count);
            }while(!receive.EndOfMessage);
            data.Position=0;
            using var document=await JsonDocument.ParseAsync(data,cancellationToken:ct);
            var root=document.RootElement;
            if(!root.TryGetProperty("id",out var responseId) || responseId.GetInt32()!=id)continue;
            if(root.TryGetProperty("error",out var error))
                throw new InvalidOperationException("CDP "+method+" lỗi: "+error.GetRawText());
            return root.Clone();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if(_socket.State==WebSocketState.Open)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try{await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure,"done",timeout.Token);}catch{}
        }
        _socket.Dispose();
    }

    // A number is accepted only when it is inside the exact /@username/live
    // anchor and its adjacent SVG is either semantically a viewer/person icon
    // or has TikTok's observed 48x48 person-path fingerprint. Generic SVGs and
    // like/heart/favorite/digg contexts are rejected before numeric parsing.
    static readonly string LiveCardScript="""
    (() => {
      const result = {
        url: location.href,
        ready: document.readyState,
        challenge: false,
        cards: [],
        stats: { liveLinks: 0, viewerSvgAccepted: 0, likeSvgRejected: 0, ambiguousRejected: 0 }
      };
      const body = (document.body?.innerText || '').slice(0, 7000).toLowerCase();
      result.challenge = /captcha|verify to continue|xác minh|đăng nhập để tiếp tục|log in|sign in/.test(body) ||
        /\/login\/?$/.test(location.pathname) || /log in|sign in/i.test(document.title);
      const countPattern = /^(?:\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*[kKmM]?\+?$/;
      const positive = /(viewer|viewers|audience|watching|online|person|people|người xem|đang xem|live[-_ ]?view)/i;
      const negative = /(like|likes|heart|favorite|favourite|digg|tim[-_ ]?(?:count|number)?|lượt thích)/i;
      const personPath = 'm24 3a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 23c13.06 26 7 33.92 7 40.44 7 44 9 44 16 44h16c7 0 9 0 9-3.56c41 33.92 34.94 26 24 26z';
      // TikTok's current thumbs-up/like SVG. It is deliberately matched by a
      // stable path prefix because this icon is often aria-hidden and carries
      // no semantic class or label in Search LIVE cards.
      const likePathPrefix = 'm26.56 2.78a4.37 4.37 0 0 1 7.22 4.52l-3.17 9.16';
      const attrs = ['aria-label','data-e2e','data-testid','data-icon','class','id','title'];
      function normalizedPath(svg) {
        return [...svg.querySelectorAll('path')]
          .map(path => (path.getAttribute('d') || '').toLowerCase().replace(/\s+/g,' ').trim())
          .join('|');
      }
      function blob(element) {
        const values = [];
        for (let node = element, depth = 0; node && depth < 4; node = node.parentElement, depth++) {
          for (const attr of attrs) values.push(node.getAttribute?.(attr) || '');
          values.push(node.querySelector?.(':scope > title')?.textContent || '');
        }
        return values.join(' ').toLowerCase();
      }
      function parseCount(raw) {
        let text = raw.trim().replace(/[+＋]$/,'').trim();
        const match = text.match(/^(\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*([kKmM])?$/);
        if (!match) return null;
        let number = match[1];
        if (!match[2] && /^\d{1,3}(?:[.,]\d{3})+$/.test(number)) number = number.replace(/[.,]/g,'');
        else number = number.replace(',','.');
        let value = Number(number);
        if (!Number.isFinite(value)) return null;
        if (match[2]) value *= match[2].toLowerCase() === 'm' ? 1000000 : 1000;
        value = Math.round(value);
        return value >= 0 && value <= 100000000 ? value : null;
      }
      function adjacentSvg(textElement, liveAnchor) {
        const textRect = textElement.getBoundingClientRect();
        let best = null;
        for (let holder = textElement.parentElement, depth = 0;
             holder && depth < 4 && liveAnchor.contains(holder);
             holder = holder.parentElement, depth++) {
          for (const svg of holder.querySelectorAll('svg')) {
            if (svg.closest('a[href*="/live"]') !== liveAnchor) continue;
            const rect = svg.getBoundingClientRect();
            if (rect.width < 5 || rect.height < 5 || rect.width > 48 || rect.height > 48) continue;
            const centerDelta = Math.abs((rect.top + rect.height/2) - (textRect.top + textRect.height/2));
            const gap = textRect.left - rect.right;
            if (centerDelta > Math.max(9, textRect.height * 0.65) || gap < -7 || gap > 24) continue;
            const score = centerDelta + Math.abs(gap);
            if (!best || score < best.score) best = { svg, holder, score };
          }
        }
        return best;
      }
      const seen = new Map();
      const links = [...document.querySelectorAll('a[href*="/live"]')]
        .filter(link => /^\/@[^/]+\/live\/?$/.test(new URL(link.href, location.href).pathname));
      result.stats.liveLinks = links.length;
      for (const link of links) {
        const parsedUrl = new URL(link.href, location.href);
        const usernameMatch = parsedUrl.pathname.match(/^\/@([^/]+)\/live\/?$/);
        if (!usernameMatch) continue;
        const username = decodeURIComponent(usernameMatch[1]).replace(/^@/,'').trim();
        if (!username || username.length > 80) continue;
        const readings = [];
        const walker = document.createTreeWalker(link, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
          const raw = (walker.currentNode.nodeValue || '').trim();
          if (!countPattern.test(raw)) continue;
          const element = walker.currentNode.parentElement;
          if (!element || element.closest('a[href*="/live"]') !== link) continue;
          const adjacent = adjacentSvg(element, link);
          if (!adjacent) { result.stats.ambiguousRejected++; continue; }
          const context = blob(element) + ' ' + blob(adjacent.svg) + ' ' + blob(adjacent.holder);
          const iconPath = normalizedPath(adjacent.svg);
          const fingerprintLike = adjacent.svg.getAttribute('viewBox') === '0 0 48 48' &&
            iconPath.startsWith(likePathPrefix);
          if (negative.test(context) || fingerprintLike) { result.stats.likeSvgRejected++; continue; }
          const semanticPerson = positive.test(context);
          const fingerprintPerson = adjacent.svg.getAttribute('viewBox') === '0 0 48 48' &&
            iconPath === personPath;
          if (!semanticPerson && !fingerprintPerson) {
            result.stats.ambiguousRejected++;
            continue;
          }
          const viewers = parseCount(raw);
          if (viewers === null) { result.stats.ambiguousRejected++; continue; }
          const evidence = semanticPerson ? 'PersonSvgSemantic' : 'PersonSvgPathVerified';
          readings.push({ viewers, evidence, score: adjacent.score });
        }
        if (!readings.length) continue;
        readings.sort((a,b) => {
          const semantic = Number(b.evidence === 'PersonSvgSemantic') - Number(a.evidence === 'PersonSvgSemantic');
          return semantic || a.score - b.score;
        });
        const reading = readings[0];
        const image = link.querySelector('img[alt]');
        let title = (image?.getAttribute('alt') || link.getAttribute('aria-label') || 'TikTok LIVE').trim();
        title = title.replace(/\s+LIVE\s+(?:\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*[kKmM]?\+?\s*$/i,'').trim() || 'TikTok LIVE';
        const card = { username, title, viewers: reading.viewers, liveUrl: link.href, evidence: reading.evidence };
        const old = seen.get(username.toLowerCase());
        if (!old || card.viewers > old.viewers) seen.set(username.toLowerCase(), card);
      }
      result.cards = [...seen.values()].sort((a,b) => b.viewers - a.viewers);
      result.stats.viewerSvgAccepted = result.cards.length;
      return JSON.stringify(result);
    })()
    """;
}

public sealed class CdpLiveScan
{
    public string Url { get; set; }="";
    public string Ready { get; set; }="";
    public bool Challenge { get; set; }
    public List<CdpLiveCard> Cards { get; set; }=[];
    public CdpLiveStats Stats { get; set; }=new();
}

public sealed class CdpLiveCard
{
    public string Username { get; set; }="";
    public string Title { get; set; }="";
    public int Viewers { get; set; }
    public string LiveUrl { get; set; }="";
    public string Evidence { get; set; }="";
}

public sealed class CdpLiveStats
{
    public int LiveLinks { get; set; }
    public int ViewerSvgAccepted { get; set; }
    public int LikeSvgRejected { get; set; }
    public int AmbiguousRejected { get; set; }
}
