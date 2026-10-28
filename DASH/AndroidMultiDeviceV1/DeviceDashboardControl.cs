using System.Drawing;
using System.Windows.Forms;
using AndroidMultiDeviceV1.Core;

namespace AndroidMultiDeviceV1;

/// <summary>
/// V2 is a passive UI over the existing DeviceRecords and worker callbacks.
/// It never creates sessions, changes assignments, or guesses a CloudPhone mapping.
/// </summary>
internal sealed class DeviceDashboardControl : UserControl
{
    readonly Settings _settings;
    readonly Func<DeviceRecord,int> _accountCount;
    readonly Action<DeviceRecord> _start;
    readonly Action<DeviceRecord> _stop;
    readonly Action<DeviceRecord> _startLive;
    readonly Func<IReadOnlyList<DeviceRecord>,Task> _startBatch;
    readonly Action<DeviceRecord> _openAccounts;
    readonly Action<DeviceRecord> _openLogs;
    readonly Action<DeviceRecord> _editLifecycle;
    readonly Action<DeviceRecord> _editEndpoint;
    readonly Func<Task> _refreshAdb;
    readonly Action<string> _log;

    readonly DataGridView _grid = new();
    readonly TextBox _search = new();
    readonly ComboBox _filter = new();
    readonly Label _counts = new();
    readonly Label _detailTitle = new();
    readonly RichTextBox _identity = new();
    readonly RichTextBox _runtime = new();
    readonly System.Windows.Forms.Timer _clockRefresh = new() { Interval = 1000 };
    readonly SplitContainer _split = new();
    readonly ContextMenuStrip _columnsMenu = new();
    readonly HashSet<Guid> _checkedIds = new(); // intentionally empty on every launch
    bool _updating;
    bool _splitInitialized;

    static readonly Color Navy = Color.FromArgb(28,48,73);
    static readonly Color Muted = Color.FromArgb(83,101,123);
    static readonly Color Light = Color.FromArgb(245,248,252);

    public DeviceDashboardControl(Settings settings, Func<DeviceRecord,int> accountCount,
        Action<DeviceRecord> start, Action<DeviceRecord> stop, Action<DeviceRecord> startLive,
        Action<DeviceRecord> openAccounts, Action<DeviceRecord> openLogs,
        Action<DeviceRecord> editLifecycle,Action<DeviceRecord> editEndpoint,
        Func<IReadOnlyList<DeviceRecord>,Task> startBatch,
        Func<Task> refreshAdb, Action<string> log)
    {
        _settings=settings; _accountCount=accountCount; _start=start; _stop=stop;
        _startLive=startLive; _openAccounts=openAccounts; _openLogs=openLogs;
        _editLifecycle=editLifecycle;_editEndpoint=editEndpoint;
        _startBatch=startBatch; _refreshAdb=refreshAdb; _log=log;
        Dock=DockStyle.Fill; BackColor=Color.White;
        Build(); RefreshData();
        _clockRefresh.Tick+=(_,_)=> { if(Visible && !IsDisposed)UpdateDetails(); };
        _clockRefresh.Start();
        Disposed+=(_,_)=>_clockRefresh.Dispose();
    }

    static Button MakeButton(string label, int width, EventHandler handler)
    {
        var b=new Button {Text=label,Width=width,Height=34,AutoSize=false,
            Margin=new Padding(3,4,3,2),FlatStyle=FlatStyle.Flat,
            BackColor=Color.White,ForeColor=Navy,Font=new Font("Segoe UI",9.3f)};
        b.FlatAppearance.BorderColor=Color.FromArgb(200,213,228);
        b.Click+=handler;return b;
    }

    void Build()
    {
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,
            Margin=Padding.Empty,Padding=new Padding(10,8,10,9),BackColor=Color.White};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(root);

        var banner=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        banner.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        banner.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        banner.Controls.Add(new Label {Text="QUẢN LÝ THIẾT BỊ   ·   Dashboard V2",
            Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",14,FontStyle.Bold),
            ForeColor=Navy,TextAlign=ContentAlignment.MiddleLeft},0,0);
        _counts.Dock=DockStyle.Fill;_counts.ForeColor=Muted;
        _counts.Font=new Font("Segoe UI",10);_counts.TextAlign=ContentAlignment.MiddleLeft;
        banner.Controls.Add(_counts,0,1);root.Controls.Add(banner,0,0);

        var toolbar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,
            BackColor=Color.White,Padding=Padding.Empty,Margin=Padding.Empty};
        _search.Width=265;_search.Font=new Font("Segoe UI",10);
        _search.PlaceholderText="Tìm máy, alias, CloudPhone, ADB, TikTok...";
        _search.Margin=new Padding(3,6,5,3);
        _search.TextChanged+=(_,_)=>RefreshData();toolbar.Controls.Add(_search);
        _filter.DropDownStyle=ComboBoxStyle.DropDownList;_filter.Width=143;
        _filter.Font=new Font("Segoe UI",9.5f);_filter.Margin=new Padding(3,5,4,3);
        _filter.Items.AddRange(new object[]{"Tất cả", "ADB Online", "ADB Offline", "Đang chạy",
            "Chờ / Idle", "Có lỗi", "Sắp hết hạn", "Chưa liên kết"});
        _filter.SelectedIndex=0;_filter.SelectedIndexChanged+=(_,_)=>RefreshData();
        toolbar.Controls.Add(_filter);
        toolbar.Controls.Add(MakeButton("Sửa thông tin",113,(_,_)=>EditSelected()));
        toolbar.Controls.Add(MakeButton("Sửa Remote ADB",125,(_,_)=>RunSelected(_editEndpoint)));
        toolbar.Controls.Add(MakeButton("Vòng đời TK",110,(_,_)=>RunSelected(_editLifecycle)));
        var columnsButton=MakeButton("Cột hiển thị",110,(_,_)=>{});
        columnsButton.Click+=(_,_)=>_columnsMenu.Show(columnsButton,new Point(0,columnsButton.Height));
        toolbar.Controls.Add(columnsButton);
        toolbar.Controls.Add(MakeButton("Tick trang",93,(_,_)=>SetVisibleChecked(true)));
        toolbar.Controls.Add(MakeButton("Bỏ tick",86,(_,_)=>SetVisibleChecked(false)));
        toolbar.Controls.Add(MakeButton("Start đã tick",106,async(_,_)=>await BatchStartAsync()));
        toolbar.Controls.Add(MakeButton("Stop đã tick",106,(_,_)=>BatchStop()));
        toolbar.Controls.Add(MakeButton("Refresh ADB",110,async(_,_)=>
        {
            try {await _refreshAdb();RefreshData();}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Refresh ADB",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }));
        root.Controls.Add(toolbar,0,1);

        _split.Dock=DockStyle.Fill;_split.Orientation=Orientation.Horizontal;
        _split.SplitterWidth=6;_split.BackColor=Color.FromArgb(216,227,240);
        _split.SizeChanged+=(_,_)=>InitializeSplitter();
        _split.SplitterMoved+=(_,_)=>
        {
            if(!_splitInitialized || _updating || _split.Height<370)return;
            if(_settings.DeviceDashboardSplitterDistance==_split.SplitterDistance)return;
            _settings.DeviceDashboardSplitterDistance=_split.SplitterDistance;
            SaveUiPreferences();
        };
        root.Controls.Add(_split,0,2);

        ConfigureGrid();
        _split.Panel1.Controls.Add(_grid);

        var detailRoot=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,
            Margin=Padding.Empty,Padding=new Padding(10,7,10,7),BackColor=Light};
        detailRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        detailRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        detailRoot.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        detailRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        _detailTitle.Font=new Font("Segoe UI Semibold",12,FontStyle.Bold);
        _detailTitle.ForeColor=Navy;_detailTitle.Dock=DockStyle.Fill;
        _detailTitle.TextAlign=ContentAlignment.MiddleLeft;
        detailRoot.Controls.Add(_detailTitle,0,0);

        var details=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,
            Margin=Padding.Empty,Padding=Padding.Empty};
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        details.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        details.Controls.Add(MakeDetailCard("THÔNG TIN THIẾT BỊ",_identity),0,0);
        details.Controls.Add(MakeDetailCard("TRẠNG THÁI VÀ NHẬT KÝ GẦN NHẤT",_runtime),1,0);
        detailRoot.Controls.Add(details,0,1);

        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,
            Margin=Padding.Empty,Padding=new Padding(0,3,0,0)};
        actions.Controls.Add(MakeButton("Start",88,(_,_)=>RunSelected(_start)));
        actions.Controls.Add(MakeButton("Stop",88,(_,_)=>RunSelected(_stop)));
        actions.Controls.Add(MakeButton("Start LIVE",102,(_,_)=>RunSelected(_startLive)));
        actions.Controls.Add(MakeButton("Kho tài khoản",120,(_,_)=>RunSelected(_openAccounts)));
        actions.Controls.Add(MakeButton("Vòng đời TK",115,(_,_)=>RunSelected(_editLifecycle)));
        actions.Controls.Add(MakeButton("Nhật ký máy",115,(_,_)=>RunSelected(_openLogs)));
        actions.Controls.Add(MakeButton("Sửa thông tin",118,(_,_)=>EditSelected()));
        actions.Controls.Add(MakeButton("Sửa Remote ADB",123,(_,_)=>RunSelected(_editEndpoint)));
        detailRoot.Controls.Add(actions,0,2);
        _split.Panel2.Controls.Add(detailRoot);
        BuildColumnsMenu();
    }

    static Control MakeDetailCard(string heading,RichTextBox area)
    {
        var card=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,
            Margin=new Padding(0,0,8,2),Padding=new Padding(12,8,12,7),BackColor=Color.White};
        card.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
        card.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        card.Controls.Add(new Label {Text=heading,Dock=DockStyle.Fill,
            Font=new Font("Segoe UI Semibold",9,FontStyle.Bold),ForeColor=Muted},0,0);
        area.Dock=DockStyle.Fill;area.ReadOnly=true;area.TabStop=false;
        area.BorderStyle=BorderStyle.None;area.BackColor=Color.White;
        area.ForeColor=Navy;area.Font=new Font("Segoe UI",10);
        area.ScrollBars=RichTextBoxScrollBars.Vertical;
        area.DetectUrls=false;area.WordWrap=true;
        card.Controls.Add(area,0,1);
        return card;
    }

    void InitializeSplitter()
    {
        if(_splitInitialized || _split.Height<370)return;
        _split.Panel1MinSize=145;_split.Panel2MinSize=170;
        int minTop=_split.Panel1MinSize;
        int maxTop=_split.Height-_split.Panel2MinSize-_split.SplitterWidth;
        if(maxTop<=minTop)return;
        int target=_settings.DeviceDashboardSplitterDistance>0
            ?_settings.DeviceDashboardSplitterDistance
            :(int)Math.Round(_split.Height*0.54);
        _updating=true;
        try {_split.SplitterDistance=Math.Clamp(target,minTop,maxTop);_splitInitialized=true;}
        finally {_updating=false;}
    }

    void ConfigureGrid()
    {
        _grid.Dock=DockStyle.Fill;_grid.Margin=Padding.Empty;
        _grid.Font=new Font("Segoe UI",10);
        _grid.BackgroundColor=Color.White;_grid.BorderStyle=BorderStyle.FixedSingle;
        _grid.GridColor=Color.FromArgb(231,237,244);
        _grid.RowHeadersVisible=false;_grid.AllowUserToAddRows=false;
        _grid.AllowUserToDeleteRows=false;_grid.AllowUserToResizeRows=false;
        _grid.AllowUserToResizeColumns=true;_grid.AutoGenerateColumns=false;
        _grid.MultiSelect=false;_grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        _grid.ColumnHeadersHeight=41;_grid.RowTemplate.Height=36;
        _grid.EnableHeadersVisualStyles=false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(233,241,251);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor=Navy;
        _grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI Semibold",10,FontStyle.Bold);
        _grid.DefaultCellStyle.ForeColor=Navy;
        _grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(218,234,253);
        _grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(13,46,87);
        _grid.DefaultCellStyle.Padding=new Padding(5,0,5,0);
        _grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(249,251,254);
        _grid.ReadOnly=false; // Only the checkbox column can be edited; text columns remain ReadOnly.
        AddColumn(new DataGridViewCheckBoxColumn {Name="Checked",HeaderText="✓",Width=42,
            TrueValue=true,FalseValue=false,ThreeState=false});
        AddText("Name","Tên máy",104);AddText("Alias","Tên dễ nhớ",136);
        AddText("Cloud","Phiên CloudPhone",184);AddText("Serial","Remote ADB",207);
        AddText("ADB","ADB",80);AddText("Worker","Worker",101);
        AddText("Phase","Công đoạn",148);AddText("Account","Tài khoản",190);
        AddText("Lease","Hạn thuê",128);AddText("Error","Lỗi gần nhất",230);
        AddText("Model","Model",112);AddText("Port","Port",68);
        AddText("LastSeen","ADB thấy lúc",132);
        _grid.Columns["Checked"]!.ReadOnly=false;
        ApplyLayoutPreferences();
        _grid.ColumnWidthChanged+=(_,_)=>{ /* Save once after mouse release, never during drag. */ };
        _grid.MouseUp+=(_,_)=>SaveColumnLayoutIfChanged();
        _grid.CurrentCellDirtyStateChanged+=(_,_)=>
        {
            if(!_updating && _grid.IsCurrentCellDirty && _grid.CurrentCell?.OwningColumn.Name=="Checked")
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged+=(_,e)=>
        {
            if(_updating || e.RowIndex<0 || e.ColumnIndex<0 || _grid.Columns[e.ColumnIndex].Name!="Checked")return;
            if(_grid.Rows[e.RowIndex].Tag is not DeviceRecord device)return;
            if(_grid.Rows[e.RowIndex].Cells["Checked"].Value is true)_checkedIds.Add(device.DeviceId);
            else _checkedIds.Remove(device.DeviceId);
            UpdateCounts();
        };
        _grid.SelectionChanged+=(_,_)=>{if(!_updating)UpdateDetails();};
        _grid.CellDoubleClick+=(_,e)=>
        {if(e.RowIndex>=0 && e.ColumnIndex>=0 && _grid.Columns[e.ColumnIndex].Name!="Checked")EditSelected();};
        _grid.ShowCellToolTips=true;
    }

    void AddText(string id,string title,int width)=>AddColumn(new DataGridViewTextBoxColumn
    {Name=id,HeaderText=title,Width=width,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
    void AddColumn(DataGridViewColumn column)=>_grid.Columns.Add(column);

    void ApplyLayoutPreferences()
    {
        var widths=_settings.DeviceDashboardColumnWidths ??=new();
        var visible=_settings.DeviceDashboardColumnVisibility ??=new();
        foreach(DataGridViewColumn c in _grid.Columns)
        {
            if(widths.TryGetValue(c.Name,out int w) && w>=40 && w<=700)
                c.Width=w;
            if(c.Name is "Checked" or "Name")continue;
            bool defaultVisible=c.Name is not ("Model" or "Port" or "LastSeen");
            c.Visible=visible.TryGetValue(c.Name,out bool show)?show:defaultVisible;
        }
    }

    void BuildColumnsMenu()
    {
        _columnsMenu.Items.Clear();
        foreach(DataGridViewColumn column in _grid.Columns)
        {
            if(column.Name is "Checked" or "Name")continue;
            var c=column;
            var item=new ToolStripMenuItem(c.HeaderText) {Checked=c.Visible,CheckOnClick=true};
            item.CheckedChanged+=(_,_)=>
            {
                c.Visible=item.Checked;
                (_settings.DeviceDashboardColumnVisibility ??=new())[c.Name]=item.Checked;
                SaveUiPreferences();
            };
            _columnsMenu.Items.Add(item);
        }
        _columnsMenu.Items.Add(new ToolStripSeparator());
        _columnsMenu.Items.Add("Đặt lại cột mặc định",null,(_,_)=>ResetColumns());
    }

    void ResetColumns()
    {
        _settings.DeviceDashboardColumnWidths=new();
        _settings.DeviceDashboardColumnVisibility=new();
        _updating=true;
        try
        {
            foreach(DataGridViewColumn c in _grid.Columns)
            {
                c.Visible=true;
                c.Width=c.Name switch
                {
                    "Checked"=>42,"Name"=>104,"Alias"=>136,"Cloud"=>184,"Serial"=>207,
                    "ADB"=>80,"Worker"=>101,"Phase"=>148,"Account"=>190,"Lease"=>128,
                    "Error"=>230,"Model"=>112,"Port"=>68,"LastSeen"=>132,_=>110
                };
                if(c.Name is "Model" or "Port" or "LastSeen")c.Visible=false;
            }
        }
        finally {_updating=false;}
        BuildColumnsMenu();SaveUiPreferences();
    }

    void SaveColumnLayoutIfChanged()
    {
        if(_updating || !IsHandleCreated)return;
        var widths=_settings.DeviceDashboardColumnWidths ??=new();
        bool changed=false;
        foreach(DataGridViewColumn c in _grid.Columns)
        {
            if(!widths.TryGetValue(c.Name,out int prev) || prev!=c.Width)
            {widths[c.Name]=c.Width;changed=true;}
        }
        if(changed)SaveUiPreferences();
    }

    void SaveUiPreferences()
    {
        try {Storage.SaveSettings(_settings);}
        catch(Exception ex){_log($"DEVICE_DASHBOARD_LAYOUT_SAVE_FAILED: {ex.GetType().Name}: {ex.Message}");}
    }

    public void FocusGrid()=>_grid.Focus();
    DeviceRecord? Selected()=>_grid.CurrentRow?.Tag as DeviceRecord;

    static string LeaseText(DeviceRecord d)
    {
        if(d.LeaseExpiresAt is null)return "Chưa nhập";
        var delta=d.LeaseExpiresAt.Value-DateTimeOffset.Now;
        if(delta<=TimeSpan.Zero)return "ĐÃ HẾT HẠN";
        if(delta.TotalDays>=1)return $"{(int)delta.TotalDays} ngày {delta.Hours} giờ";
        return $"{Math.Max(0,(int)delta.TotalHours)} giờ {delta.Minutes} phút";
    }

    static bool IsRunning(DeviceRecord d)=>d.WorkerState.Equals("Running",StringComparison.OrdinalIgnoreCase)
        || d.WorkerState.Equals("WaitingForSlot",StringComparison.OrdinalIgnoreCase);

    static string StageLabel(DeviceRecord d)
    {
        string s=d.Stage?.Trim()??"";
        string upper=s.ToUpperInvariant();
        if(upper.Contains("ERROR") || upper.Contains("FAILED"))return "Lỗi";
        if(upper.Contains("PREFLIGHT"))return "Preflight";
        if(upper.Contains("LOGIN"))return "Google Login";
        if(upper.Contains("SEARCH"))return "Tìm LIVE";
        if(upper.Contains("RECOVER"))return "Phục hồi";
        if(upper.Contains("PROFILE"))return "Profile";
        if(upper.Contains("LIVE"))return "LIVE running";
        return string.IsNullOrWhiteSpace(s)?"Chờ":s.Replace('_',' ');
    }

    bool VisibleByFilter(DeviceRecord d)
    {
        string q=_search.Text.Trim();
        if(q.Length>0 && !new[]{d.Name,d.DisplayAlias,d.CloudInstanceName,d.Serial,d.Model,d.Connection,
            d.WorkerState,d.Stage,d.Detail,d.AccountEmail,d.LastError,d.OperatorNotes}.Any(s=>
                (s??"").Contains(q,StringComparison.OrdinalIgnoreCase)))return false;
        return _filter.SelectedIndex switch
        {
            1=>d.Connection.Equals("ONLINE",StringComparison.OrdinalIgnoreCase),
            2=>d.Connection.Equals("OFFLINE",StringComparison.OrdinalIgnoreCase),
            3=>IsRunning(d),4=>!IsRunning(d),
            5=>!string.IsNullOrWhiteSpace(d.LastError) || d.Stage is "ERROR" or "RESTRICTED" or
                "ACCOUNT_CHECKPOINT" or "WAIT_REVIEW" or "PROFILE_REVIEW",
            6=>d.LeaseExpiresAt is not null && d.LeaseExpiresAt<=DateTimeOffset.Now.AddHours(24),
            7=>string.IsNullOrWhiteSpace(d.CloudInstanceName),
            _=>true
        };
    }

    public void RefreshData()
    {
        if(IsDisposed || _grid.IsCurrentCellInEditMode)return;
        var active=Selected()?.DeviceId;
        int scroll=-1;
        try {scroll=_grid.FirstDisplayedScrollingRowIndex;}catch(InvalidOperationException){}
        var availableIds=_settings.Devices.Select(d=>d.DeviceId).ToHashSet();
        _checkedIds.IntersectWith(availableIds);
        _updating=true;
        try
        {
            _grid.Rows.Clear();
            foreach(var d in _settings.Devices.Where(VisibleByFilter))
            {
                int rowIndex=_grid.Rows.Add(_checkedIds.Contains(d.DeviceId),d.Name,
                    string.IsNullOrWhiteSpace(d.DisplayAlias)?"—":d.DisplayAlias,
                    string.IsNullOrWhiteSpace(d.CloudInstanceName)?"Chưa liên kết":d.CloudInstanceName,
                    d.Serial,d.Connection,d.WorkerState,StageLabel(d),
                    string.IsNullOrWhiteSpace(d.AccountEmail)?"Chưa xác định":d.AccountEmail,
                    LeaseText(d),string.IsNullOrWhiteSpace(d.LastError)?"—":d.LastError,
                    string.IsNullOrWhiteSpace(d.Model)?"—":d.Model,d.SystemPort,
                    d.LastSeenAt?.LocalDateTime.ToString("dd/MM HH:mm")??"Chưa kiểm tra");
                var row=_grid.Rows[rowIndex];row.Tag=d;
                string fullPhase=string.IsNullOrWhiteSpace(d.Detail)?d.Stage:d.Stage+" · "+d.Detail;
                row.Cells["Phase"].ToolTipText=fullPhase;
                row.Cells["Account"].ToolTipText=d.AccountEmail;
                row.Cells["Cloud"].ToolTipText=d.CloudInstanceName;
                row.Cells["Serial"].ToolTipText=d.Serial;
                row.Cells["Error"].ToolTipText=d.LastError;
                if(d.Connection.Equals("OFFLINE",StringComparison.OrdinalIgnoreCase))
                    row.DefaultCellStyle.ForeColor=Color.FromArgb(124,132,144);
                else if(d.Connection.Equals("ONLINE",StringComparison.OrdinalIgnoreCase))
                    row.Cells["ADB"].Style.ForeColor=Color.FromArgb(21,128,61);
                if(IsRunning(d))row.Cells["Worker"].Style.ForeColor=Color.FromArgb(27,95,182);
                if(d.LeaseExpiresAt is not null && d.LeaseExpiresAt<=DateTimeOffset.Now.AddHours(24))
                    row.Cells["Lease"].Style.BackColor=Color.FromArgb(255,244,219);
                if(!string.IsNullOrWhiteSpace(d.LastError))
                    row.Cells["Error"].Style.BackColor=Color.FromArgb(255,237,237);
                if(active==d.DeviceId)row.Selected=true;
            }
            if(active is not null)
            {
                foreach(DataGridViewRow row in _grid.Rows)
                    if(row.Tag is DeviceRecord d && d.DeviceId==active)
                    {_grid.CurrentCell=row.Cells["Name"];break;}
            }
            if(scroll>=0 && scroll<_grid.Rows.Count)
            {
                try {_grid.FirstDisplayedScrollingRowIndex=scroll;}
                catch(InvalidOperationException){}
            }
        }
        finally {_updating=false;}
        UpdateCounts();UpdateDetails();
    }

    void UpdateCounts()
    {
        int online=_settings.Devices.Count(d=>d.Connection.Equals("ONLINE",StringComparison.OrdinalIgnoreCase));
        int running=_settings.Devices.Count(IsRunning);
        int due=_settings.Devices.Count(d=>d.LeaseExpiresAt is not null &&
            d.LeaseExpiresAt<=DateTimeOffset.Now.AddHours(24));
        int unmapped=_settings.Devices.Count(d=>string.IsNullOrWhiteSpace(d.CloudInstanceName));
        _counts.Text=$"Máy: {_settings.Devices.Count}   |   ADB Online: {online}   |   Worker chạy/chờ: {running}   |   " +
            $"Hết hạn / <24h: {due}   |   Chưa liên kết: {unmapped}   |   Đã tick: {_checkedIds.Count}";
    }

    void UpdateDetails()
    {
        var d=Selected();
        if(d is null)
        {
            _detailTitle.Text="Chọn một dòng để xem toàn bộ thông tin";
            _identity.Text="Tên CloudPhone và thời hạn thuê được nhập thủ công.\nKhông ánh xạ máy theo thứ tự trong danh sách.";
            _runtime.Text="Không có thiết bị được chọn.\nCác máy không được tick mặc định; chỉ những máy được tick mới chạy lệnh hàng loạt.";
            return;
        }
        _detailTitle.Text=d.Name+(string.IsNullOrWhiteSpace(d.DisplayAlias)?"":$"  ·  {d.DisplayAlias}");
        string identityText=$"Tên máy: {d.Name}\n"+
            $"Tên dễ nhớ: {(string.IsNullOrWhiteSpace(d.DisplayAlias)?"Chưa nhập":d.DisplayAlias)}\n"+
            $"CloudPhone: {(string.IsNullOrWhiteSpace(d.CloudInstanceName)?"Chưa liên kết":d.CloudInstanceName)}\n"+
            $"Remote ADB: {d.Serial}\n"+
            $"Model: {d.Model}\n"+
            $"Appium systemPort: {d.SystemPort}\n"+
            $"Hạn thuê: {LeaseText(d)}"+
            (d.LeaseExpiresAt.HasValue?$" ({d.LeaseExpiresAt.Value.LocalDateTime:dd/MM/yyyy HH:mm})":"")+"\n"+
            $"ADB thấy lần cuối: {d.LastSeenAt?.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss")??"Chưa kiểm tra"}\n"+
            $"Ghi chú: {(string.IsNullOrWhiteSpace(d.OperatorNotes)?"—":d.OperatorNotes)}";
        var lifecycle=_settings.AccountPolicyFor(d);
        string lifecycleRunning=d.LifecycleSessionStartedUtc.HasValue && d.LifecycleDeadlineUtc.HasValue
            ? $"Đang quản lý: {d.LifecycleRuntimeMode} | @{d.LifecycleAccountHandle}\n"+
              $"Phiên bắt đầu: {d.LifecycleSessionStartedUtc.Value.LocalDateTime:dd/MM HH:mm:ss} | " +
              $"Hạn kết thúc: {d.LifecycleDeadlineUtc.Value.LocalDateTime:dd/MM HH:mm:ss}\n"+
              $"Thời gian còn: {Math.Max(0,(int)Math.Ceiling((d.LifecycleDeadlineUtc.Value-DateTimeOffset.UtcNow).TotalSeconds))} giây " +
              $"| Giới hạn đang áp dụng: {AccountDurationDisplay.FormatHours(d.LifecycleActiveLimitMinutes)}\n"
            : "Bộ đếm vòng đời: CHƯA KÍCH HOẠT (chưa xác minh tài khoản đang chạy)\n";
        string runtimeText=$"ADB: {d.Connection}     |     Worker: {d.WorkerState}\n"+
            $"Vòng đời TK: {AccountDurationDisplay.FormatHours(lifecycle.SessionLimitMinutes)} | Tự chuyển: {(lifecycle.AutoRotateAccounts?"Bật":"Tắt")}\n"+
            lifecycleRunning+
            $"AUTO: chỉ TK chưa dùng | MANUAL: TK cũ còn giờ | Xóa khỏi kho: {(lifecycle.AutoPurgeUsedAccounts?$"Sau {lifecycle.PurgeUsedAfterHours} giờ":"Tắt")}\n"+
            $"Appium: {d.AppiumStatus}\n"+
            $"Công đoạn: {d.Stage}\n"+
            $"Chi tiết: {(string.IsNullOrWhiteSpace(d.Detail)?"—":d.Detail)}\n"+
            $"Tài khoản runtime: {(string.IsNullOrWhiteSpace(d.AccountEmail)?"Chưa xác định":d.AccountEmail)}\n"+
            $"Tài khoản trong kho: {_accountCount(d)}\n"+
            $"Worker bắt đầu: {d.WorkerStartedAt?.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss")??"—"}\n"+
            $"Lỗi gần nhất: {(string.IsNullOrWhiteSpace(d.LastError)?"Không có":d.LastError)}";
        if(_identity.Text!=identityText)_identity.Text=identityText;
        if(_runtime.Text!=runtimeText)_runtime.Text=runtimeText;
    }

    void SetVisibleChecked(bool checkedValue)
    {
        _updating=true;
        try
        {
            foreach(DataGridViewRow row in _grid.Rows)
            {
                if(row.Tag is not DeviceRecord d)continue;
                if(checkedValue)_checkedIds.Add(d.DeviceId);else _checkedIds.Remove(d.DeviceId);
                row.Cells["Checked"].Value=checkedValue;
            }
        }
        finally {_updating=false;}
        UpdateCounts();
    }

    List<DeviceRecord> CheckedDevices()=>_settings.Devices
        .Where(d=>_checkedIds.Contains(d.DeviceId)).ToList();

    async Task BatchStartAsync()
    {
        var selected=CheckedDevices();
        if(selected.Count==0){MessageBox.Show(this,"Hãy tick máy cần Start trong cột đầu tiên.");return;}
        if(MessageBox.Show(this,$"Start {selected.Count} thiết bị đã tick?",
            "Xác nhận Start",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        try {await _startBatch(selected);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Start thiết bị",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        RefreshData();
    }
    void BatchStop()
    {
        var selected=CheckedDevices();
        if(selected.Count==0){MessageBox.Show(this,"Hãy tick máy cần Stop trong cột đầu tiên.");return;}
        if(MessageBox.Show(this,$"Gửi lệnh Stop cho {selected.Count} thiết bị đã tick?",
            "Xác nhận Stop",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        foreach(var d in selected)_stop(d);
        RefreshData();
    }
    void RunSelected(Action<DeviceRecord> action)
    {
        var d=Selected();if(d is null){MessageBox.Show(this,"Hãy chọn một thiết bị.");return;}
        action(d);RefreshData();
    }

    void EditSelected()
    {
        var d=Selected();if(d is null){MessageBox.Show(this,"Hãy chọn một thiết bị.");return;}
        using var dialog=new DeviceMetadataDialog(d,_settings.Devices);
        if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;
        d.DisplayAlias=dialog.Alias;d.CloudInstanceName=dialog.CloudName;
        d.LeaseExpiresAt=dialog.LeaseExpiresAt;d.OperatorNotes=dialog.Notes;
        Storage.SaveSettings(_settings);
        _log($"DEVICE_METADATA_UPDATED: {d.Name} [{d.Serial}]; CloudPhone mapping edited manually; worker unchanged.");
        RefreshData();
    }
}

/// <summary>Manually entered rental metadata; no invented CloudPhone API integration.</summary>
internal sealed class DeviceMetadataDialog : Form
{
    readonly TextBox _alias=new();
    readonly TextBox _cloud=new();
    readonly DateTimePicker _expiry=new();
    readonly TextBox _notes=new();
    readonly DeviceRecord _target;
    readonly IEnumerable<DeviceRecord> _others;
    public string Alias=>_alias.Text.Trim();
    public string CloudName=>_cloud.Text.Trim();
    public DateTimeOffset? LeaseExpiresAt=>_expiry.Checked?new DateTimeOffset(_expiry.Value):null;
    public string Notes=>_notes.Text.Trim();

    public DeviceMetadataDialog(DeviceRecord target,IEnumerable<DeviceRecord> devices)
    {
        _target=target;_others=devices;
        Text=$"Thông tin thuê / Alias — {target.Name}";
        StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(540,395);
        Size=new Size(585,425);Font=new Font("Segoe UI",9);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=6,
            Padding=new Padding(18,15,18,12)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,148));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(var height in new[]{40,40,45,70,49,49})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        Controls.Add(root);
        void Put(int row,string label,Control control)
        {
            root.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);
            control.Dock=DockStyle.Fill;control.Margin=new Padding(0,4,2,4);root.Controls.Add(control,1,row);
        }
        _alias.Text=target.DisplayAlias;_alias.MaxLength=64;
        _cloud.Text=target.CloudInstanceName;_cloud.MaxLength=140;
        _expiry.Format=DateTimePickerFormat.Custom;_expiry.CustomFormat="dd/MM/yyyy HH:mm";
        _expiry.ShowCheckBox=true;
        _expiry.Value=(target.LeaseExpiresAt?.LocalDateTime??DateTime.Now.AddDays(1));
        _expiry.Checked=target.LeaseExpiresAt.HasValue;
        _notes.Text=target.OperatorNotes;_notes.MaxLength=280;_notes.Multiline=true;
        Put(0,"Tên dễ nhớ (alias)",_alias);
        Put(1,"Tên phiên CloudPhone",_cloud);
        Put(2,"Hết hạn thuê (tùy chọn)",_expiry);
        Put(3,"Ghi chú",_notes);
        var note=new Label {Dock=DockStyle.Fill,AutoSize=false,ForeColor=Color.FromArgb(82,98,118),
            Text="Chỉ lưu thông tin quản lý. Không thay ADB, DeviceId, systemPort hay kho tài khoản."};
        root.Controls.Add(note,0,4);root.SetColumnSpan(note,2);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Lưu",Width=96,Height=31,DialogResult=DialogResult.None};
        save.Click+=(_,_)=>
        {
            if(CloudName.Length>0 && _others.Any(d=>d.DeviceId!=_target.DeviceId &&
                d.CloudInstanceName.Equals(CloudName,StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this,"Phiên CloudPhone này đã được gắn với một máy khác. Hãy kiểm tra ánh xạ.",
                    "Trùng tên phiên",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;
            }
            DialogResult=DialogResult.OK;Close();
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(new Button {Text="Hủy",Width=96,Height=31,DialogResult=DialogResult.Cancel});
        root.Controls.Add(buttons,0,5);root.SetColumnSpan(buttons,2);
        AcceptButton=save;CancelButton=(Button)buttons.Controls[1];
    }
}
