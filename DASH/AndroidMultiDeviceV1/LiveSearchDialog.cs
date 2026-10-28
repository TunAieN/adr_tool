using System.Drawing;
using System.Windows.Forms;
using AndroidMultiDeviceV1.Core;

namespace AndroidMultiDeviceV1;

/// <summary>
/// LIVE search window. Phases 1-3 search and verify a room; an accepted room
/// immediately enters the bounded Phase 5 auto-comment loop.
/// ShowDialog prevents competing device actions from the main window.
/// </summary>
public sealed class LiveSearchDialog : Form
{
    readonly Settings _system;
    readonly DeviceRecord _device;
    readonly LiveManagerSettings _config;
    readonly Action<string> _mainLog;
    readonly DataGridView _cards=new() {
        Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
        SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
        MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,RowTemplate=new DataGridViewRow {Height=29}
    };
    readonly TextBox _log=new() {Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,
        ScrollBars=ScrollBars.Vertical,Font=new Font("Consolas",9),BackColor=Color.FromArgb(247,249,252)};
    readonly Label _status=new() {AutoSize=true,Text="Chưa tìm kiếm.",ForeColor=Color.FromArgb(58,72,94)};
    readonly Button _start=new() {Text="Tìm LIVE",Width=114,Height=32};
    readonly Button _stop=new() {Text="Dừng",Width=83,Height=32,Enabled=false};
    readonly Button _browse=new() {Text="Theo dõi / Duyệt LIVE",Width=168,Height=32};
    readonly Button _benchmark=new() {Text="Đo Appium 20×2",Width=144,Height=27};
    readonly CheckBox _safeIdle=new() {Text="Dùng idle 1s xuyên suốt tìm LIVE và Phase 5",
        AutoSize=true,Checked=false,Margin=new Padding(16,5,0,0)};
    readonly TextBox _commentLibrary=new() {Width=180,Height=28,ReadOnly=true,PlaceholderText="Thư viện bình luận"};
    CancellationTokenSource? _cts;
    bool _working;
    bool _closeAfterStop;

    public LiveSearchDialog(Settings settings,DeviceRecord device,LiveManagerSettings config,Action<string> mainLog)
    {
        _system=settings;_device=device;_config=config;_mainLog=mainLog;
        _safeIdle.Checked=_system.ForceLiveIdleOneSecond;
        Text="Tìm LIVE — tìm lại và mở card đã đối chiếu";
        StartPosition=FormStartPosition.CenterParent;Size=new Size(1000,660);MinimumSize=new Size(820,480);
        Font=new Font("Segoe UI",9);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12),BackColor=Color.White};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,28));
        Controls.Add(root);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,FlowDirection=FlowDirection.LeftToRight};
        actions.Controls.Add(new Label{Text=$"{_device.Name}  ·  {_device.Serial}",Width=270,Height=35,TextAlign=ContentAlignment.MiddleLeft,
            Font=new Font("Segoe UI Semibold",10,FontStyle.Bold)});
        actions.Controls.Add(_start);actions.Controls.Add(_stop);actions.Controls.Add(_browse);actions.Controls.Add(_commentLibrary);
        actions.Controls.Add(new Label{Text=$"Từ khóa: {_config.Keywords.Count}  |  Card/lượt: {_config.CardsPerBatch}  |  Phase 5: tự động gửi bình luận",
            AutoSize=true,Margin=new Padding(15,10,0,0)});
        root.Controls.Add(actions,0,0);
        root.Controls.Add(_status,0,1);
        _cards.Columns.Add(new DataGridViewTextBoxColumn{Name="Keyword",HeaderText="Từ khóa",FillWeight=84});
        _cards.Columns.Add(new DataGridViewTextBoxColumn{Name="Title",HeaderText="Chủ đề LIVE",FillWeight=130});
        _cards.Columns.Add(new DataGridViewTextBoxColumn{Name="Creator",HeaderText="Chủ phòng (nếu đọc được)",FillWeight=130});
        _cards.Columns.Add(new DataGridViewTextBoxColumn{Name="Viewers",HeaderText="Viewer",FillWeight=62});
        _cards.Columns.Add(new DataGridViewTextBoxColumn{Name="Evidence",HeaderText="Xác minh Viewer",FillWeight=120});
        _cards.Columns.Add(new DataGridViewTextBoxColumn{Name="Note",HeaderText="Ghi chú",FillWeight=200});
        root.Controls.Add(_cards,0,2);
        var lower=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0,10,0,0)};
        lower.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        lower.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var logTitle=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,
            FlowDirection=FlowDirection.LeftToRight,Margin=Padding.Empty};
        logTitle.Controls.Add(new Label{Text="Nhật ký tìm LIVE",AutoSize=true,
            Margin=new Padding(0,5,16,0),Font=new Font("Segoe UI Semibold",9,FontStyle.Bold)});
        logTitle.Controls.Add(_benchmark);
        logTitle.Controls.Add(_safeIdle);
        lower.Controls.Add(logTitle,0,0);
        lower.Controls.Add(_log,0,1);root.Controls.Add(lower,0,3);
        _commentLibrary.Text=$"{_config.CommentDrafts.Count} câu trong thư viện";
        _start.Click+=async(_,_)=>await StartSearchAsync();
        _browse.Click+=async(_,_)=>await StartRoomMonitorAsync();
        _benchmark.Click+=async(_,_)=>await StartIdleBenchmarkAsync();
        _stop.Click+=(_,_)=>StopSearch();
        FormClosing+=OnClosing;
    }
    void Progress(string message)
    {
        if(IsDisposed)return;
        if(InvokeRequired){try{BeginInvoke(new Action(()=>Progress(message)));}catch(InvalidOperationException){}return;}
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        if(_log.TextLength>80000)_log.Text=_log.Text[^50000..];
        _mainLog($"{_device.Name} LIVE Search: {message}");
    }
    void AddCard(LiveSearchCard card)
    {
        if(IsDisposed)return;
        if(InvokeRequired){try{BeginInvoke(new Action(()=>AddCard(card)));}catch(InvalidOperationException){}return;}
        string viewer=card.Viewers.HasValue?card.Viewers.Value.ToString("N0"):"?";
        string evidence=card.ViewerEvidence switch {
            "Verified"=>"Đã xác minh badge",
            "BadgeCandidate"=>"Cần xác minh icon người",
            "OcrConfirmed"=>"OCR cũ: cần xác minh lại",
            "OcrConflict"=>"OCR mâu thuẫn",
            "OcrCandidate"=>"OCR tạm tính",
            "OcrRepeatedCandidate"=>"OCR trùng 2 ảnh (tạm)",
            "IconLikeIgnored"=>"Like — bỏ qua",
            "OcrFrameConflict"=>"OCR 2 ảnh mâu thuẫn",
            "BadgeNotFound"=>"Không thấy badge",
            "ChromeWebVerified"=>"CDP + SVG người — chờ máy xác minh",
            _=>"Chưa xác minh"
        };
        int idx=_cards.Rows.Add(card.Keyword,card.Title,card.Creator,viewer,evidence,card.Note);
        if(card.ViewerEvidence!="Verified" && card.ViewerEvidence!="OcrConfirmed")_cards.Rows[idx].DefaultCellStyle.BackColor=Color.FromArgb(255,250,233);
        _status.Text=$"Đã đọc {_cards.Rows.Count} card (chưa tự mở phòng LIVE).";
    }
    /// <summary>
    /// Isolated UiAutomator2 benchmark using a fresh diagnostic session.
    /// This path never opens a LIVE, sends text, performs a gesture or calls
    /// the comment module. Regular app operations retain their idle setting.
    /// </summary>
    async Task StartIdleBenchmarkAsync()
    {
        if(_working)return;
        _working=true;
        _start.Enabled=false;_browse.Enabled=false;_benchmark.Enabled=false;_stop.Enabled=true;
        _cts=new CancellationTokenSource();
        var ct=_cts.Token;
        try
        {
            var adb=new AdbClient(()=>_system.AdbPath) {PerformanceLog=Progress};
            var online=await adb.DevicesAsync(ct);
            if(!online.Contains(_device.Serial,StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Thiết bị chưa Online trong adb devices -l.");
            Progress("APP_IDLE_DIAG_ONLY: phép thử ABBA 20 mẫu mỗi cấu hình, warmup riêng; " +
                "không chạm/vuốt/nhập chữ/gửi bình luận.");
            await using var session=new AppiumSession(_system.AppiumUrl);
            session.Performance=new AppiumPerformanceMonitor(Progress,Storage.Folder,_device.Name);
            await session.CreateAsync(_device,ct);
            await AppiumIdleBenchmark.RunAsync(session,Progress,ct);
            _status.Text="Đã đo ABBA 20×2, đã khôi phục setting; xem APP_IDLE_TEST_COMPARE.";
            Progress("APP_IDLE_DIAG_COMPLETE: đã đóng phép thử, luồng bình thường không thay đổi.");
        }
        catch(OperationCanceledException)
        {
            _status.Text="Đã dừng đo tốc độ Appium.";
            Progress("APP_IDLE_DIAG_STOPPED: người dùng nhấn Dừng.");
        }
        catch(Exception ex)
        {
            _status.Text="Không hoàn tất phép đo Appium; xem log.";
            Progress("APP_IDLE_DIAG_ERROR: "+ex.Message);
        }
        finally
        {
            _cts?.Dispose();_cts=null;_working=false;
            _start.Enabled=true;_browse.Enabled=true;_benchmark.Enabled=true;_stop.Enabled=false;
            if(_closeAfterStop && !IsDisposed)BeginInvoke(new Action(Close));
        }
    }

    // Separate, opt-in room navigation/observation. The existing search and
    // Phase 5 auto-comment paths remain byte-for-byte unchanged.
    async Task StartRoomMonitorAsync()
    {
        if(_working)return;
        _working=true;
        _start.Enabled=false;
        _browse.Enabled=false;
        _benchmark.Enabled=false;
        _stop.Enabled=true;
        _cts=new CancellationTokenSource();
        var ct=_cts.Token;
        try
        {
            var adb=new AdbClient(()=>_system.AdbPath) {PerformanceLog=Progress};
            var online=await adb.DevicesAsync(ct);
            if(!online.Contains(_device.Serial,StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Thiết bị chưa Online trong adb devices -l.");
            Progress("ROOM_MONITOR_UI_START: chế độ duyệt LIVE độc lập, không gọi module bình luận. " +
                "Hãy mở sẵn một phòng LIVE trên điện thoại.");
            await using var session=new AppiumSession(_system.AppiumUrl);
            session.Performance=new AppiumPerformanceMonitor(Progress,Storage.Folder,_device.Name);
            await session.CreateAsync(_device,ct);
            var lifecycle=new LiveRoomLifecycleMonitor(session,_config,Progress,adb,_device.Serial);
            string result=await lifecycle.RunAsync(ct);
            _status.Text="Theo dõi LIVE: "+result;
            Progress("ROOM_MONITOR_UI_FINISH: "+result);
        }
        catch(OperationCanceledException)
        {
            _status.Text="Đã dừng theo yêu cầu.";
            Progress("ROOM_MONITOR_UI_STOPPED: người dùng nhấn Dừng.");
        }
        catch(Exception ex)
        {
            _status.Text="Lỗi theo dõi LIVE: xem nhật ký.";
            Progress("ROOM_MONITOR_UI_ERROR: "+ex.Message);
        }
        finally
        {
            _cts?.Dispose();_cts=null;_working=false;
            _start.Enabled=true;_browse.Enabled=true;_benchmark.Enabled=true;_stop.Enabled=false;
            if(_closeAfterStop && !IsDisposed)BeginInvoke(new Action(Close));
        }
    }

    async Task StartSearchAsync()
    {
        if(_working)return;
        if(!_config.KeywordSearchEnabled){MessageBox.Show(this,"Bật Tìm LIVE bằng từ khóa trong Cấu hình LIVE trước.");return;}
        bool safeIdleRequested=_safeIdle.Checked;
        _working=true;_start.Enabled=false;_browse.Enabled=false;_benchmark.Enabled=false;_safeIdle.Enabled=false;
        _stop.Enabled=true;_cards.Rows.Clear();_log.Clear();
        _cts=new CancellationTokenSource();
        var ct=_cts.Token;
        try
        {
            var adb=new AdbClient(()=>_system.AdbPath) {PerformanceLog=Progress};
            _status.Text="Đang chạy workflow tự động 24/7; nhấn Dừng để kết thúc.";
            Progress($"GLOBAL_WORKFLOW_START: source={_config.LiveSourceMode}; keywordQueue=true; gridRecovery=true; " +
                "sessionRecovery=true; stopCondition=USER_CANCELLATION_ONLY.");
            var workflow=new LiveAutonomousWorkflow(
                _system.AppiumUrl,_device,_config,safeIdleRequested,adb,Progress,AddCard,
                maxDevicesPerRoom:_system.MaxDevicesPerLiveRoom,
                roomDeviceLimitProvider:()=>_system.MaxDevicesPerLiveRoom,
                sessionRecoveryLimitProvider:()=>_system.MaxSessionRecoveryAttempts);
            await workflow.RunAsync(ct);
        }
        catch(OperationCanceledException){_status.Text="Đã dừng theo yêu cầu.";Progress("Người dùng yêu cầu Stop.");}
        catch(Exception ex){_status.Text="Lỗi kỹ thuật: xem nhật ký.";Progress("Lỗi: "+ex.Message);}
        finally
        {
            _cts?.Dispose();_cts=null;_working=false;_start.Enabled=true;_browse.Enabled=true;_benchmark.Enabled=true;_safeIdle.Enabled=true;_stop.Enabled=false;
            if(_closeAfterStop && !IsDisposed)BeginInvoke(new Action(Close));
        }
    }
    void StopSearch(){if(!_working)return;_status.Text="Đang hủy thao tác, đóng Appium session...";_stop.Enabled=false;_cts?.Cancel();}
    void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(!_working)return;
        e.Cancel=true;_closeAfterStop=true;StopSearch();
    }
}
