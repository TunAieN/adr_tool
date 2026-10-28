using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using AndroidMultiDeviceV1.Core;

namespace AndroidMultiDeviceV1;

public sealed class MainForm : Form
{
    readonly Settings _settings=Storage.LoadSettings();
    AccountVault? _vault;
    DeviceOrchestrator? _orchestrator;
    readonly DataGridView _deviceGrid=CreateGrid();
    readonly DataGridView _accountGrid=CreateGrid();
    // A checkbox is an explicit batch selection; row highlighting is only for inspection.
    readonly HashSet<Guid> _checkedAccountIds=new();
    readonly HashSet<string> _checkedDeviceSerials=new(StringComparer.OrdinalIgnoreCase);
    readonly CheckBox _checkAllDevices=new(){Text="Tất cả",AutoCheck=false,ThreeState=true,
        AutoSize=false,TextAlign=ContentAlignment.MiddleLeft,CheckAlign=ContentAlignment.MiddleLeft,
        BackColor=Color.FromArgb(241,245,249),Font=new Font("Segoe UI",8.5f)};
    readonly CheckBox _checkAllAccounts=new(){Text="Tất cả",AutoCheck=false,ThreeState=true,
        AutoSize=false,TextAlign=ContentAlignment.MiddleLeft,CheckAlign=ContentAlignment.MiddleLeft,
        BackColor=Color.FromArgb(241,245,249),Font=new Font("Segoe UI",8.5f)};
    bool _refreshingAccountGrid;
    bool _refreshingDeviceGrid;
    readonly TextBox _log=new(){Multiline=true,ScrollBars=ScrollBars.Vertical,ReadOnly=true,Dock=DockStyle.Fill,
                                  BackColor=Color.FromArgb(20,28,43),ForeColor=Color.Gainsboro,Font=new Font("Consolas",9)};
    readonly ComboBox _logFilter=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=210};
    readonly object _logGate=new();
    readonly List<string> _logLines=[];
    string? _detailLogSerial;
    readonly TextBox _adbPath=new(){Width=330};
    readonly TextBox _appium=new(){Width=170};
    readonly TextBox _remoteEndpoint=new(){Width=240,Text="adb.xcloudphone.net:24187"};
    readonly NumericUpDown _parallel=new(){Minimum=1,Maximum=100,Width=65};
    readonly NumericUpDown _minutes=new(){Minimum=1,Maximum=1440,Value=30,Width=80};
    readonly Label _summary=new(){AutoSize=true,ForeColor=Color.FromArgb(71,85,105)};
    readonly Label _deviceStatus=new(){Text="Chưa quét điện thoại",AutoSize=true,Margin=new Padding(12,11,5,0),ForeColor=Color.FromArgb(30,64,175),Font=new Font("Segoe UI Semibold",9,FontStyle.Bold)};
    readonly Button _unlock=new(){Text="Mở kho",Width=96,Height=34};
    readonly Label _selectedPhoneTitle=new(){Text="Chưa chọn thiết bị",Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",11,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
    readonly Label _selectedPhoneDetail=new(){Text="Chọn một dòng ở danh sách thiết bị.",Dock=DockStyle.Fill,Font=new Font("Segoe UI",9),ForeColor=Color.FromArgb(71,85,105)};
    Button? _removeDevicesButton;
    DeviceDashboardControl? _dashboard;

    readonly System.Windows.Forms.Timer _timer=new(){Interval=5000};
    bool _closing;
    DateTimeOffset _lastAccountPurgePoll=DateTimeOffset.MinValue;

    static DataGridView CreateGrid()
    {
        return new DataGridView {
            Dock=DockStyle.Fill,AutoGenerateColumns=false,ReadOnly=true,MultiSelect=true,
            SelectionMode=DataGridViewSelectionMode.FullRowSelect,AllowUserToAddRows=false,
            AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,
            BorderStyle=BorderStyle.FixedSingle,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToResizeRows=false,ColumnHeadersHeight=34,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles=false,GridColor=Color.FromArgb(225,231,238),
            ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {BackColor=Color.FromArgb(246,248,251),ForeColor=Color.FromArgb(31,41,55),Font=new Font("Segoe UI Semibold",9,FontStyle.Bold)},
            DefaultCellStyle=new DataGridViewCellStyle {SelectionBackColor=Color.FromArgb(220,236,252),SelectionForeColor=Color.FromArgb(15,23,42)},
            RowTemplate=new DataGridViewRow {Height=32}
        };
    }
    static Button Btn(string label,int width,EventHandler onClick)
    {
        var b=new Button {Text=label,Width=width,Height=32,Margin=new Padding(3),FlatStyle=FlatStyle.Flat,
            BackColor=Color.White,ForeColor=Color.FromArgb(38,50,66)};
        b.FlatAppearance.BorderColor=Color.FromArgb(204,214,226);
        b.Click+=onClick;return b;
    }
    static Label Lab(string value)=>new(){Text=value,AutoSize=true,Margin=new Padding(10,10,5,0)};
    static FlowLayoutPanel Toolbar()=>new(){Dock=DockStyle.Top,Height=41,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,AutoScroll=true,BackColor=Color.FromArgb(241,245,249)};

    public MainForm()
    {
        Text="ANDROID MULTI-DEVICE MANAGER — V1.6 (ADB / Appium / Profile)";
        MinimumSize=new Size(1050,670);Size=new Size(1360,800);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9);
        BuildLayout();
        _adbPath.Text=_settings.AdbPath;
        _appium.Text=_settings.AppiumUrl;
        _parallel.Value=Math.Clamp(_settings.MaxParallel,1,100);
        RefreshViews();
        _timer.Tick+=(_,_)=>
        {
            if(_closing)return;
            CleanupUsedAccountsIfDue();
            RefreshViews();
        };
        _timer.Start();
        FormClosing+=OnClosing;
        Log("Chưa có phiên đăng nhập. Mở kho tài khoản, sau đó Refresh ADB để kiểm tra thiết bị.");
    }
    // Master-detail workspace. Device selection is the scope for controls,
    // logs and the local encrypted account pool shown on the right.
    void BuildLayout()
    {
        SuspendLayout();
        BackColor=Color.FromArgb(247,249,252);
        Text="ANDROID MULTI-DEVICE MANAGER — Navigation + Master / Detail";
        MinimumSize=new Size(1120,720);Size=new Size(1450,860);

        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,59));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,29));
        Controls.Add(root);
        root.Controls.Add(new Label {Text="ANDROID MULTI-DEVICE MANAGER",Dock=DockStyle.Fill,
            BackColor=Color.White,Font=new Font("Segoe UI Semibold",14,FontStyle.Bold),
            ForeColor=Color.FromArgb(28,39,57),TextAlign=ContentAlignment.MiddleLeft,
            Padding=new Padding(20,0,0,0)},0,0);

        var connectionBar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,
            Padding=new Padding(10,9,4,3),BackColor=Color.FromArgb(240,244,249)};
        _adbPath.Width=255;_appium.Width=160;_remoteEndpoint.Width=185;
        connectionBar.Controls.Add(Lab("ADB:"));connectionBar.Controls.Add(_adbPath);
        connectionBar.Controls.Add(Lab("Appium:"));connectionBar.Controls.Add(_appium);
        connectionBar.Controls.Add(Lab("Remote ADB:"));connectionBar.Controls.Add(_remoteEndpoint);
        var connect=Btn("Kết nối",88,async(_,_)=>await SafeAsync(ConnectRemoteFromTopAsync));MakePrimary(connect);
        connectionBar.Controls.Add(connect);
        connectionBar.Controls.Add(Btn("Refresh",82,async(_,_)=>await SafeAsync(RefreshAsync)));
        connectionBar.Controls.Add(Btn("Cấu hình",86,(_,_)=>ConfigureSystem()));
        connectionBar.Controls.Add(Btn("LIVE",68,(_,_)=>ConfigureLive()));
        connectionBar.Controls.Add(_unlock);
        _unlock.FlatStyle=FlatStyle.Flat;_unlock.Click+=(_,_)=>UnlockVault();
        root.Controls.Add(connectionBar,0,1);

        // Navigation shell: the master-detail page is nested in the workspace,
        // never attached directly over the whole form.
        var work=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,
            Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.FromArgb(247,249,252)};
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,172));
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        work.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.Controls.Add(work,0,2);

        TabControl? detailTabs=null;
        SplitContainer? deviceDetailSplit=null;
        var navButtons=new List<Button>();
        var sidebar=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,
            Margin=Padding.Empty,Padding=new Padding(9,12,7,10),BackColor=Color.FromArgb(246,248,251)};
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        for(int i=1;i<=6;i++)sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        sidebar.Controls.Add(new Label {Text="ĐIỀU HƯỚNG",Dock=DockStyle.Fill,
            TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(10,0,0,0),
            ForeColor=Color.FromArgb(100,116,139),Font=new Font("Segoe UI Semibold",8,FontStyle.Bold)},0,0);
        void AddNavigation(string text,int row,Action action,bool active=false)
        {
            var button=new Button {Text=text,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,5),
                FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(12,0,0,0),
                BackColor=active?Color.FromArgb(221,235,252):Color.FromArgb(246,248,251),
                ForeColor=active?Color.FromArgb(25,91,181):Color.FromArgb(51,65,85),
                Font=new Font("Segoe UI",9.3f,active?FontStyle.Bold:FontStyle.Regular)};
            button.FlatAppearance.BorderSize=0;
            button.Click+=(_,_)=>{
                foreach(var nav in navButtons)
                {nav.BackColor=Color.FromArgb(246,248,251);nav.ForeColor=Color.FromArgb(51,65,85);nav.Font=new Font("Segoe UI",9.3f);}
                button.BackColor=Color.FromArgb(221,235,252);button.ForeColor=Color.FromArgb(25,91,181);
                button.Font=new Font("Segoe UI",9.3f,FontStyle.Bold);action();};
            navButtons.Add(button);sidebar.Controls.Add(button,0,row);
        }
        void ShowDashboard()
        {
            if(deviceDetailSplit is not null)deviceDetailSplit.Visible=false;
            if(_dashboard is not null)
            { _dashboard.Visible=true;_dashboard.BringToFront();_dashboard.RefreshData();_dashboard.FocusGrid(); }
        }
        void ShowLegacyDetails(int tabIndex)
        {
            if(_dashboard is not null)_dashboard.Visible=false;
            if(deviceDetailSplit is not null)
            { deviceDetailSplit.Visible=true;deviceDetailSplit.BringToFront(); }
            if(detailTabs is not null)detailTabs.SelectedIndex=tabIndex;
        }
        AddNavigation("THIẾT BỊ",1,ShowDashboard,true);
        AddNavigation("TÀI KHOẢN",2,()=>{ShowLegacyDetails(1);_accountGrid.Focus();});
        AddNavigation("CẤU HÌNH",3,ConfigureSystem);
        AddNavigation("NHẬT KÝ",4,()=>{ShowLegacyDetails(0);_log.Focus();});
        AddNavigation("CẤU HÌNH LIVE",5,ConfigureLive);
        AddNavigation("TÌM LIVE",6,OpenLiveSearch);
        work.Controls.Add(sidebar,0,0);

        var workspace=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(7,8,10,9),
            BackColor=Color.FromArgb(247,249,252)};
        work.Controls.Add(workspace,1,0);

        var split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Vertical,
            FixedPanel=FixedPanel.Panel1,BackColor=Color.FromArgb(226,232,240),Padding=new Padding(8)};
        bool splitSized=false;
        split.SizeChanged+=(_,_)=>{
            if(splitSized || split.Width<900)return;
            splitSized=true;
            split.SplitterDistance=Math.Min(390,Math.Max(300,split.Width-650));
            split.Panel1MinSize=300;split.Panel2MinSize=500;
        };
        workspace.Controls.Add(split);
        deviceDetailSplit=split;

        var master=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,
            BackColor=Color.White,Margin=Padding.Empty};
        master.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        master.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        master.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        master.Controls.Add(SectionTitle("Thiết bị"),0,0);
        var masterActions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,Padding=new Padding(5,2,5,3)};
        masterActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));
        masterActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33));
        masterActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33));
        var startSelected=Btn("Start Selected",120,async(_,_)=>await SafeAsync(StartSelectedDevicesAsync));MakePrimary(startSelected);startSelected.Dock=DockStyle.Fill;
        var stopSelected=Btn("Stop Selected",120,(_,_)=>StopSelectedDevices());stopSelected.Dock=DockStyle.Fill;
        _removeDevicesButton=Btn("Gỡ thiết bị",120,async(_,_)=>await SafeAsync(RemoveCheckedDevicesAsync));
        _removeDevicesButton.Dock=DockStyle.Fill;_removeDevicesButton.Enabled=false;
        _removeDevicesButton.BackColor=Color.FromArgb(254,242,242);
        _removeDevicesButton.ForeColor=Color.FromArgb(185,28,28);
        masterActions.Controls.Add(startSelected,0,0);masterActions.Controls.Add(stopSelected,1,0);
        masterActions.Controls.Add(_removeDevicesButton,2,0);
        master.Controls.Add(masterActions,0,1);

        _deviceGrid.ReadOnly=false;_deviceGrid.MultiSelect=false;_deviceGrid.Margin=new Padding(6);
        _deviceGrid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Checked",HeaderText="",Width=66,MinimumWidth=66,
            AutoSizeMode=DataGridViewAutoSizeColumnMode.None,ThreeState=false,TrueValue=true,FalseValue=false});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="Device Name",FillWeight=105,ReadOnly=true});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Stage",HeaderText="Status",FillWeight=80,ReadOnly=true});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Account",HeaderText="Current Account",FillWeight=125,ReadOnly=true});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Serial",HeaderText="Serial",Visible=false,ReadOnly=true});
        _deviceGrid.CurrentCellDirtyStateChanged+=(_,_)=>{
            if(!_refreshingDeviceGrid && _deviceGrid.IsCurrentCellDirty && _deviceGrid.CurrentCell?.OwningColumn.Name=="Checked")
                _deviceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        _deviceGrid.CellValueChanged+=(_,e)=>{
            if(_refreshingDeviceGrid||e.RowIndex<0||_deviceGrid.Columns[e.ColumnIndex].Name!="Checked")return;
            string serial=_deviceGrid.Rows[e.RowIndex].Cells["Serial"].Value?.ToString()??"";
            if(_deviceGrid.Rows[e.RowIndex].Cells["Checked"].Value is true)_checkedDeviceSerials.Add(serial);
            else _checkedDeviceSerials.Remove(serial);UpdateDeviceSelectionHeader();};
        _deviceGrid.SelectionChanged+=(_,_)=>{
            if(_refreshingDeviceGrid)return;
            UpdateSelectedDeviceInfo();RefreshViews();};
        var deviceContextMenu=new ContextMenuStrip();
        var editAdbMenuItem=deviceContextMenu.Items.Add("Sửa Remote ADB (giữ kho tài khoản)");
        editAdbMenuItem.Click+=async(_,_)=>await SafeAsync(()=>ChangeDeviceEndpointAsync());
        deviceContextMenu.Items.Add(new ToolStripSeparator());
        var removeDeviceMenuItem=deviceContextMenu.Items.Add("Gỡ thiết bị này");
        removeDeviceMenuItem.Click+=async(_,_)=>await SafeAsync(RemoveSelectedDeviceAsync);
        deviceContextMenu.Opening+=(_,e)=>e.Cancel=SelectedDevice() is null;
        _deviceGrid.ContextMenuStrip=deviceContextMenu;
        _deviceGrid.CellMouseDown+=(_,e)=>{
            if(e.Button!=MouseButtons.Right || e.RowIndex<0)return;
            _deviceGrid.ClearSelection();
            _deviceGrid.Rows[e.RowIndex].Selected=true;
            _deviceGrid.CurrentCell=_deviceGrid.Rows[e.RowIndex].Cells["Name"];
        };
        _checkAllDevices.Click+=(_,_)=>SetCheckedForVisibleDevices(_checkAllDevices.CheckState!=CheckState.Checked);
        _deviceGrid.Controls.Add(_checkAllDevices);
        _deviceGrid.Resize+=(_,_)=>PositionDeviceSelectionHeader();
        _deviceGrid.Scroll+=(_,_)=>PositionDeviceSelectionHeader();
        master.Controls.Add(_deviceGrid,0,2);split.Panel1.Controls.Add(master);

        var tabs=new TabControl {Dock=DockStyle.Fill,Font=new Font("Segoe UI",9)};
        detailTabs=tabs;
        var controlTab=new TabPage("Control & Log") {BackColor=Color.White};
        var controlLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};
        controlLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));controlLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        controlLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));controlLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        controlLayout.Controls.Add(_selectedPhoneTitle,0,0);controlLayout.Controls.Add(_selectedPhoneDetail,0,1);
        var controls=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
        var startOne=Btn("Start",90,async(_,_)=>await SafeAsync(StartSelectedAsync));MakePrimary(startOne);
        controls.Controls.Add(startOne);controls.Controls.Add(Btn("Stop",90,(_,_)=>StopSelected()));
        controls.Controls.Add(Btn("Start LIVE",100,(_,_)=>StartLiveForCurrentDevice()));
        controls.Controls.Add(Btn("Kiểm tra UI",100,async(_,_)=>await SafeAsync(CheckTikTokAsync)));
        controls.Controls.Add(Btn("Tìm LIVE",90,(_,_)=>OpenLiveSearch()));
        controlLayout.Controls.Add(controls,0,2);
        _log.BackColor=Color.FromArgb(248,250,253);_log.ForeColor=Color.FromArgb(47,63,84);
        _log.BorderStyle=BorderStyle.FixedSingle;controlLayout.Controls.Add(_log,0,3);
        controlTab.Controls.Add(controlLayout);

        var poolTab=new TabPage("Kho Tài Khoản Cục Bộ") {BackColor=Color.White};
        var poolLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(8)};
        poolLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));poolLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var poolActions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
        poolActions.Controls.Add(Btn("Nhập TXT / Excel",125,(_,_)=>ImportAccounts()));
        poolActions.Controls.Add(Btn("Xóa",65,(_,_)=>DeleteAccountsFromVault()));
        poolActions.Controls.Add(Btn("Chọn tất cả",95,(_,_)=>SetCheckedForVisibleAccounts(true)));
        poolActions.Controls.Add(Btn("Bỏ chọn",78,(_,_)=>SetCheckedForVisibleAccounts(false)));
        poolActions.Controls.Add(Btn("Đặt lại",78,(_,_)=>ResetAccounts()));
        poolActions.Controls.Add(Btn("TikTok handle",105,(_,_)=>SetHandle()));
        poolActions.Controls.Add(Btn("Vòng đời TK",110,(_,_)=>ConfigureAccountLifecycle()));
        poolLayout.Controls.Add(poolActions,0,0);
        _accountGrid.ReadOnly=false;_accountGrid.Margin=new Padding(0);
        _accountGrid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Checked",DataPropertyName=nameof(ManagedAccount.IsSelectedForRun),HeaderText="",Width=72,MinimumWidth=72,
            AutoSizeMode=DataGridViewAutoSizeColumnMode.None,ThreeState=false,TrueValue=true,FalseValue=false});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Id",DataPropertyName=nameof(ManagedAccount.Id),Visible=false,ReadOnly=true});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Handle",DataPropertyName=nameof(ManagedAccount.ExpectedTikTokHandle),HeaderText="TikTok",FillWeight=100,ReadOnly=true});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Email",DataPropertyName=nameof(ManagedAccount.Email),HeaderText="Email",FillWeight=150,ReadOnly=true});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="State",DataPropertyName=nameof(ManagedAccount.StateVi),HeaderText="Trạng thái",FillWeight=110,ReadOnly=true});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Used",DataPropertyName=nameof(ManagedAccount.UsedDisplay),HeaderText="Lịch sử",FillWeight=83,ReadOnly=true});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="UsedTime",DataPropertyName=nameof(ManagedAccount.UsedDurationDisplay),HeaderText="Đã chạy",FillWeight=100,ReadOnly=true,ToolTipText="Tổng thời gian đã ghi nhận (giờ:phút:giây); không đặt lại khi Stop/Start."});
        // V5.4: RemainingDisplay still exists for the scheduler; do not show a Remaining column.
        _accountGrid.CellToolTipTextNeeded+=(_,e)=>
        {
            if(e.RowIndex<0 || e.ColumnIndex<0 || _accountGrid.Columns[e.ColumnIndex].Name!="UsedTime")return;
            if(_accountGrid.Rows[e.RowIndex].DataBoundItem is not ManagedAccount account)return;
            e.ToolTipText=account.LegacyHistoryUnverified
                ? "Thời gian chỉ tính từ mốc theo dõi mới; những phiên V5 cũ chưa có bằng chứng đầy đủ."
                : "Thời gian đã ghi nhận dạng giờ:phút:giây (không reset sau 24 giờ).";
        };
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Result",DataPropertyName=nameof(ManagedAccount.LastResult),HeaderText="Ghi chú",FillWeight=180,ReadOnly=true});
        _accountGrid.CurrentCellDirtyStateChanged+=(_,_)=>{if(!_refreshingAccountGrid&&_accountGrid.IsCurrentCellDirty&&
            _accountGrid.CurrentCell?.OwningColumn.Name=="Checked")_accountGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        _accountGrid.CellValueChanged+=(_,e)=>{if(_refreshingAccountGrid||e.RowIndex<0||
            _accountGrid.Columns[e.ColumnIndex].Name!="Checked")return;var row=_accountGrid.Rows[e.RowIndex];
            if(row.DataBoundItem is not ManagedAccount account)return;
            bool selected=row.Cells["Checked"].Value is true;
            account.IsSelectedForRun=selected;
            _vault?.SetRunSelection(account.Id,selected);
            if(selected)_checkedAccountIds.Add(account.Id);else _checkedAccountIds.Remove(account.Id);
            UpdateAccountSelectionHeader();};
        _accountGrid.DataError+=(_,e)=>{Log("Không thể cập nhật ô chọn: "+e.Exception?.GetType().Name);e.ThrowException=false;};
        _accountGrid.DataSource=new List<ManagedAccount>();
        _checkAllAccounts.Click+=(_,_)=>SetCheckedForVisibleAccounts(_checkAllAccounts.CheckState!=CheckState.Checked);
        _accountGrid.Controls.Add(_checkAllAccounts);_accountGrid.Resize+=(_,_)=>PositionAccountSelectionHeader();
        _accountGrid.Scroll+=(_,_)=>PositionAccountSelectionHeader();poolLayout.Controls.Add(_accountGrid,0,1);
        poolTab.Controls.Add(poolLayout);tabs.TabPages.Add(controlTab);tabs.TabPages.Add(poolTab);
        split.Panel2.Controls.Add(tabs);

        // Full-width device inventory is the primary THIẾT BỊ workspace.
        // The existing controller/log/account pool remains available unchanged
        // through the TÀI KHOẢN and NHẬT KÝ navigation pages.
        void OpenExistingDeviceDetail(DeviceRecord device,int tabIndex)
        {
            ShowLegacyDetails(tabIndex);
            _refreshingDeviceGrid=true;
            try
            {
                _deviceGrid.ClearSelection();
                foreach(DataGridViewRow row in _deviceGrid.Rows)
                {
                    if(!string.Equals(row.Cells["Serial"].Value?.ToString(),device.Serial,
                        StringComparison.OrdinalIgnoreCase))continue;
                    row.Selected=true;_deviceGrid.CurrentCell=row.Cells["Name"];break;
                }
            }
            finally { _refreshingDeviceGrid=false; }
            UpdateSelectedDeviceInfo();
            if(tabIndex==1)_accountGrid.Focus();else _log.Focus();
            RefreshViews();
        }
        _dashboard=new DeviceDashboardControl(_settings,
            d=>_vault is null?0:_vault.Snapshot(d.Serial).Count,
            d=>StartOneDevice(d),
            d=>{_orchestrator?.Stop(d.Serial);RefreshViews();},
            d=>StartLiveForDevice(d),
            d=>OpenExistingDeviceDetail(d,1),
            d=>OpenExistingDeviceDetail(d,0),
            d=>ConfigureAccountLifecycle(d),
            d=>{_ = SafeAsync(()=>ChangeDeviceEndpointAsync(d));},
            async selectedDevices=>
            {
                _checkedDeviceSerials.Clear();
                foreach(var d in selectedDevices)_checkedDeviceSerials.Add(d.Serial);
                await StartSelectedDevicesAsync();
            },
            ()=>RefreshAsync(),
            Log);
        workspace.Controls.Add(_dashboard);
        _dashboard.BringToFront();
        split.Visible=false;

        var footer=new Panel {Dock=DockStyle.Fill,BackColor=Color.FromArgb(238,243,248)};
        _summary.Dock=DockStyle.Fill;_summary.AutoSize=false;_summary.TextAlign=ContentAlignment.MiddleLeft;_summary.Padding=new Padding(12,0,0,0);
        footer.Controls.Add(_summary);root.Controls.Add(footer,0,3);
        ResumeLayout(true);
    }

    // Retained temporarily as a compile-time reference while the master-detail
    // screen settles; it is never invoked and contains no data-layer behavior.
    void BuildLegacyLayout()
    {
        SuspendLayout();
        BackColor=Color.FromArgb(247,249,252);
        Text="ANDROID MULTI-DEVICE MANAGER — Giao diện gọn (V1.6.2.6)";
        MinimumSize=new Size(1060,690);
        Size=new Size(1390,830);

        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,
            Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.FromArgb(247,249,252)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,59));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,147));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,27));
        Controls.Add(root);

        var header=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Color.White};
        header.Controls.Add(new Label {Text="ANDROID MULTI-DEVICE MANAGER",Dock=DockStyle.Fill,
            Font=new Font("Segoe UI Semibold",14,FontStyle.Bold),ForeColor=Color.FromArgb(28,39,57),
            TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(20,0,0,0)});
        root.Controls.Add(header,0,0);

        var connectionBar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,
            FlowDirection=FlowDirection.LeftToRight,Margin=Padding.Empty,Padding=new Padding(10,9,4,3),
            BackColor=Color.FromArgb(240,244,249)};
        _adbPath.Width=263; _appium.Width=160; _remoteEndpoint.Width=188;
        _adbPath.Margin=new Padding(3,5,3,3);
        _appium.Margin=new Padding(3,5,3,3);
        _remoteEndpoint.Margin=new Padding(3,5,3,3);
        connectionBar.Controls.Add(Lab("ADB:"));connectionBar.Controls.Add(_adbPath);
        connectionBar.Controls.Add(Lab("Appium:"));connectionBar.Controls.Add(_appium);
        connectionBar.Controls.Add(Lab("Remote ADB:"));connectionBar.Controls.Add(_remoteEndpoint);
        var connect=Btn("Kết nối",90,async(_,_)=>await SafeAsync(ConnectRemoteFromTopAsync));
        MakePrimary(connect);
        connectionBar.Controls.Add(connect);
        connectionBar.Controls.Add(Btn("Refresh",86,async(_,_)=>await SafeAsync(RefreshAsync)));
        connectionBar.Controls.Add(Btn("Lưu",65,(_,_)=>SaveSettings()));
        connectionBar.Controls.Add(_unlock);
        _unlock.Margin=new Padding(5,3,3,3);
        _unlock.FlatStyle=FlatStyle.Flat;
        _unlock.FlatAppearance.BorderColor=Color.FromArgb(204,214,226);
        _unlock.Click+=(_,_)=>UnlockVault();
        root.Controls.Add(connectionBar,0,1);

        var work=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,
            Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.FromArgb(247,249,252)};
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        work.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.Controls.Add(work,0,2);

        var sidebar=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,
            Margin=Padding.Empty,Padding=new Padding(9,12,7,10),BackColor=Color.FromArgb(246,248,251)};
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<6;i++)sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        void AddNav(string text,int row,Action click,bool active=false,bool enabled=true)
        {
            var button=new Button {Text=text,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,5),
                FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,
                Padding=new Padding(12,0,0,0),Enabled=enabled,
                BackColor=active?Color.FromArgb(221,235,252):Color.FromArgb(246,248,251),
                ForeColor=active?Color.FromArgb(25,91,181):Color.FromArgb(51,65,85),
                Font=new Font("Segoe UI",9.3f,active?FontStyle.Bold:FontStyle.Regular)};
            button.FlatAppearance.BorderSize=0;
            button.Click+=(_,_)=>click();
            sidebar.Controls.Add(button,0,row);
        }
        AddNav("THIẾT BỊ",0,()=>_deviceGrid.Focus(),true);
        AddNav("TÀI KHOẢN",1,()=>_accountGrid.Focus());
        AddNav("CẤU HÌNH",2,ConfigureSystem);
        AddNav("NHẬT KÝ",3,()=>_log.Focus());
        AddNav("CẤU HÌNH LIVE",4,ConfigureLive);
        AddNav("TÌM LIVE",5,OpenLiveSearch);
        work.Controls.Add(sidebar,0,0);

        var center=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,
            Padding=new Padding(6,10,10,9),Margin=Padding.Empty,BackColor=Color.FromArgb(247,249,252)};
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        center.RowStyles.Add(new RowStyle(SizeType.Percent,61));
        center.RowStyles.Add(new RowStyle(SizeType.Percent,39));
        work.Controls.Add(center,1,0);

        var devicePanel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,
            Margin=new Padding(0,0,0,8),BackColor=Color.White};
        devicePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        devicePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,276));
        devicePanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        center.Controls.Add(devicePanel,0,0);

        var devices=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,
            BackColor=Color.White};
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        devices.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        devices.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var deviceTitle=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        deviceTitle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        deviceTitle.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        deviceTitle.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        deviceTitle.Controls.Add(SectionTitle("Danh sách thiết bị"),0,0);
        _deviceStatus.Dock=DockStyle.Fill;
        _deviceStatus.Margin=new Padding(0,13,12,0);
        deviceTitle.Controls.Add(_deviceStatus,1,0);
        devices.Controls.Add(deviceTitle,0,0);

        _deviceGrid.ReadOnly=false;
        _deviceGrid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Checked",HeaderText="",Width=70,MinimumWidth=70,
            AutoSizeMode=DataGridViewAutoSizeColumnMode.None,ThreeState=false,TrueValue=true,FalseValue=false,
            DefaultCellStyle=new DataGridViewCellStyle {NullValue=false,Alignment=DataGridViewContentAlignment.MiddleCenter}});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="Thiết bị",FillWeight=75});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Serial",HeaderText="Remote ADB / UDID",FillWeight=165});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Model",HeaderText="Model",FillWeight=72});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Connection",HeaderText="ADB",FillWeight=64});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Appium",HeaderText="Appium",FillWeight=68});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Port",HeaderText="Port",FillWeight=55});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Stage",HeaderText="Worker",FillWeight=90});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Account",HeaderText="Tài khoản",FillWeight=130});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Uptime",HeaderText="Uptime",FillWeight=72});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Error",HeaderText="Lỗi gần nhất",FillWeight=145});
        _deviceGrid.Columns.Add(new DataGridViewButtonColumn{Name="Start",HeaderText="",Text="Start",UseColumnTextForButtonValue=true,
            Width=58,MinimumWidth=58,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        _deviceGrid.Columns.Add(new DataGridViewButtonColumn{Name="Stop",HeaderText="",Text="Stop",UseColumnTextForButtonValue=true,
            Width=58,MinimumWidth=58,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        foreach(DataGridViewColumn col in _deviceGrid.Columns)
        {
            if(col.Name!="Checked")col.ReadOnly=true;
            col.MinimumWidth=col.Name switch {
                "Name"=>75,"Serial"=>145,"Connection"=>60,"Port"=>49,
                "Stage"=>75,"Account"=>95,"Error"=>120,"Start"=>58,"Stop"=>58,_=>50};
        }
        _deviceGrid.Margin=new Padding(8,0,4,7);
        _deviceGrid.MultiSelect=false;
        _deviceGrid.SelectionChanged+=(_,_)=>UpdateSelectedDeviceInfo();
        _deviceGrid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0) _=SafeAsync(CheckTikTokAsync);};
        _deviceGrid.CurrentCellDirtyStateChanged+=(_,_)=>{
            if(!_refreshingDeviceGrid && _deviceGrid.IsCurrentCellDirty &&
               _deviceGrid.CurrentCell?.OwningColumn.Name=="Checked")
                _deviceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _deviceGrid.CellValueChanged+=(_,e)=>{
            if(_refreshingDeviceGrid || e.RowIndex<0 || e.ColumnIndex<0 ||
               _deviceGrid.Columns[e.ColumnIndex].Name!="Checked")return;
            var row=_deviceGrid.Rows[e.RowIndex];
            string serial=row.Cells["Serial"].Value?.ToString()??"";
            if(row.Cells["Checked"].Value is true)_checkedDeviceSerials.Add(serial);
            else _checkedDeviceSerials.Remove(serial);
            UpdateDeviceSelectionHeader();
        };
        _deviceGrid.CellContentClick+=(_,e)=>{
            if(e.RowIndex<0)return;
            string serial=_deviceGrid.Rows[e.RowIndex].Cells["Serial"].Value?.ToString()??"";
            var d=_settings.Devices.FirstOrDefault(x=>x.Serial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(d is null)return;
            if(_deviceGrid.Columns[e.ColumnIndex].Name=="Start")StartOneDevice(d);
            else if(_deviceGrid.Columns[e.ColumnIndex].Name=="Stop")_orchestrator?.Stop(d.Serial);
        };
        _checkAllDevices.Click+=(_,_)=>SetCheckedForVisibleDevices(_checkAllDevices.CheckState!=CheckState.Checked);
        _deviceGrid.Controls.Add(_checkAllDevices);
        _deviceGrid.Resize+=(_,_)=>PositionDeviceSelectionHeader();
        _deviceGrid.Scroll+=(_,_)=>PositionDeviceSelectionHeader();
        _deviceGrid.ColumnWidthChanged+=(_,_)=>PositionDeviceSelectionHeader();
        devices.Controls.Add(_deviceGrid,0,1);
        devicePanel.Controls.Add(devices,0,0);

        var quick=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,
            Margin=new Padding(4,0,0,0),Padding=new Padding(10,3,9,5),
            BackColor=Color.FromArgb(250,251,253),AutoScroll=true};
        quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        quick.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        quick.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        quick.RowStyles.Add(new RowStyle(SizeType.Absolute,75));
        quick.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        quick.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        quick.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        quick.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        quick.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        quick.Controls.Add(SectionTitle("Thao tác thiết bị"),0,0);
        quick.Controls.Add(_selectedPhoneTitle,0,1);
        var startStop=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
        startStop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        startStop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        startStop.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        startStop.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        var start=Btn("Start Selected",98,async(_,_)=>await SafeAsync(StartSelectedDevicesAsync));MakePrimary(start);start.Dock=DockStyle.Fill;
        var stop=Btn("Stop Selected",98,(_,_)=>StopSelectedDevices());stop.Dock=DockStyle.Fill;
        var test=Btn("Chạy TK tick",105,async(_,_)=>await SafeAsync(StartCheckedAccountAsync));test.Dock=DockStyle.Fill;
        var inspect=Btn("Kiểm tra UI",103,async(_,_)=>await SafeAsync(CheckTikTokAsync));inspect.Dock=DockStyle.Fill;
        startStop.Controls.Add(start,0,0);startStop.Controls.Add(stop,1,0);
        startStop.Controls.Add(test,0,1);startStop.Controls.Add(inspect,1,1);
        quick.Controls.Add(startStop,0,2);
        _selectedPhoneDetail.Padding=new Padding(2,4,2,0);
        quick.Controls.Add(_selectedPhoneDetail,0,3);

        var minsRow=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
        minsRow.Controls.Add(Lab("Phút/máy:"));
        _minutes.Width=66;_minutes.Margin=new Padding(3,5,0,0);minsRow.Controls.Add(_minutes);
        minsRow.Controls.Add(Btn("Áp dụng",76,(_,_)=>SaveDeviceMinutes()));
        quick.Controls.Add(minsRow,0,4);
        var allRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        allRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));allRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        var startAll=Btn("Start tất cả",110,(_,_)=>StartAll());startAll.Dock=DockStyle.Fill;
        var stopAll=Btn("Stop tất cả",110,(_,_)=>_orchestrator?.StopAll());stopAll.Dock=DockStyle.Fill;
        allRow.Controls.Add(startAll,0,0);allRow.Controls.Add(stopAll,1,0);
        quick.Controls.Add(allRow,0,5);
        var other=Btn("Thao tác khác  ▾",220,(_,_)=>{});other.Dock=DockStyle.Fill;
        var deviceMenu=new ContextMenuStrip();
        deviceMenu.Items.Add("Refresh ADB",null,async(_,_)=>await SafeAsync(RefreshAsync));
        deviceMenu.Items.Add("Start LIVE cho máy đã tick",null,(_,_)=>StartLiveSelectedDevices());
        deviceMenu.Items.Add("Đổi cổng ADB",null,async(_,_)=>await SafeAsync(()=>ChangeDeviceEndpointAsync()));
        deviceMenu.Items.Add("Kết nối Remote",null,async(_,_)=>await SafeAsync(ConnectRemoteAsync));
        deviceMenu.Items.Add(new ToolStripSeparator());
        deviceMenu.Items.Add("Gỡ thiết bị",null,async(_,_)=>await SafeAsync(RemoveSelectedDeviceAsync));
        other.Click+=(_,_)=>deviceMenu.Show(other,0,other.Height);
        quick.Controls.Add(other,0,6);
        devicePanel.Controls.Add(quick,1,0);

        var accounts=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,BackColor=Color.White};
        accounts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        accounts.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        accounts.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var accountBar=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty,
            Padding=new Padding(7,4,4,0),WrapContents=false,AutoScroll=true,BackColor=Color.White};
        accountBar.Controls.Add(SectionTitle("Danh sách tài khoản"));
        accountBar.Controls.Add(Btn("Nhập TXT / Excel",120,(_,_)=>ImportAccounts()));
        accountBar.Controls.Add(Btn("Xóa",61,(_,_)=>DeleteAccountsFromVault()));
        accountBar.Controls.Add(Btn("Chọn tất cả",95,(_,_)=>SetCheckedForVisibleAccounts(true)));
        accountBar.Controls.Add(Btn("Bỏ chọn",76,(_,_)=>SetCheckedForVisibleAccounts(false)));
        var accountMore=Btn("Thêm ▾",78,(_,_)=>{});
        var accountMenu=new ContextMenuStrip();
        accountMenu.Items.Add("Đặt lại trạng thái",null,(_,_)=>ResetAccounts());
        accountMenu.Items.Add("TikTok handle",null,(_,_)=>SetHandle());
        accountMenu.Items.Add("Cấu hình hồ sơ",null,(_,_)=>ConfigureProfiles());
        accountMenu.Items.Add("Sửa selectors",null,(_,_)=>OpenSelectors());
        accountMore.Click+=(_,_)=>accountMenu.Show(accountMore,0,accountMore.Height);
        accountBar.Controls.Add(accountMore);
        accounts.Controls.Add(accountBar,0,0);

        _accountGrid.ReadOnly=false;
        _accountGrid.Columns.Add(new DataGridViewCheckBoxColumn {
            Name="Checked",HeaderText="",Width=78,MinimumWidth=78,
            AutoSizeMode=DataGridViewAutoSizeColumnMode.None,
            ThreeState=false,TrueValue=true,FalseValue=false,
            DefaultCellStyle=new DataGridViewCellStyle {NullValue=false,Alignment=DataGridViewContentAlignment.MiddleCenter}
        });
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Id",HeaderText="ID",Visible=false});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Handle",HeaderText="TikTok",FillWeight=112});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Email",HeaderText="Email Google",FillWeight=180});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Device",HeaderText="Máy",FillWeight=89});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="Trạng thái",FillWeight=110});
        _accountGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Result",HeaderText="Ghi chú",FillWeight=185});
        foreach(DataGridViewColumn col in _accountGrid.Columns)
        {
            if(col.Name!="Checked")col.ReadOnly=true;
            col.MinimumWidth=col.Name switch {
                "Checked"=>78,"Handle"=>100,"Email"=>170,"Device"=>75,
                "State"=>95,"Result"=>130,_=>10};
        }
        _accountGrid.Margin=new Padding(8,0,8,8);
        _accountGrid.CurrentCellDirtyStateChanged+=(_,_)=>{
            if(!_refreshingAccountGrid && _accountGrid.IsCurrentCellDirty &&
               _accountGrid.CurrentCell?.OwningColumn?.Name=="Checked")
                _accountGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _accountGrid.CellValueChanged+=(_,e)=>{
            if(_refreshingAccountGrid || e.RowIndex<0 || e.ColumnIndex<0 ||
               _accountGrid.Columns[e.ColumnIndex].Name!="Checked")return;
            var row=_accountGrid.Rows[e.RowIndex];
            if(!Guid.TryParse(row.Cells["Id"].Value?.ToString(),out var id))return;
            if(row.Cells["Checked"].Value is true)_checkedAccountIds.Add(id);
            else _checkedAccountIds.Remove(id);
            UpdateAccountSelectionHeader();
        };
        _accountGrid.DataError+=(_,e)=>{Log("Không thể cập nhật ô chọn: "+e.Exception?.GetType().Name);e.ThrowException=false;};
        _checkAllAccounts.Click+=(_,_)=>SetCheckedForVisibleAccounts(_checkAllAccounts.CheckState!=CheckState.Checked);
        _accountGrid.Controls.Add(_checkAllAccounts);
        _accountGrid.Resize+=(_,_)=>PositionAccountSelectionHeader();
        _accountGrid.Scroll+=(_,_)=>PositionAccountSelectionHeader();
        _accountGrid.ColumnWidthChanged+=(_,_)=>PositionAccountSelectionHeader();
        _accountGrid.HandleCreated+=(_,_)=>PositionAccountSelectionHeader();
        accounts.Controls.Add(_accountGrid,0,1);
        center.Controls.Add(accounts,0,1);

        var logs=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,
            Margin=new Padding(9,1,10,0),BackColor=Color.White};
        logs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        logs.RowStyles.Add(new RowStyle(SizeType.Absolute,33));
        logs.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var logbar=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        logbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        logbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));
        logbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,126));
        logbar.Controls.Add(SectionTitle("Nhật ký hoạt động"),0,0);
        _logFilter.Items.Add("All devices");_logFilter.SelectedIndex=0;
        _logFilter.Margin=new Padding(3,5,3,3);
        _logFilter.SelectedIndexChanged+=(_,_)=>RebuildLogView();
        logbar.Controls.Add(_logFilter,1,0);
        logbar.Controls.Add(Btn("Xóa hiển thị",114,(_,_)=>{lock(_logGate)_logLines.Clear();_log.Clear();}),2,0);
        logs.Controls.Add(logbar,0,0);
        _log.BackColor=Color.FromArgb(248,250,253);
        _log.ForeColor=Color.FromArgb(47,63,84);
        _log.BorderStyle=BorderStyle.FixedSingle;
        _log.Font=new Font("Consolas",9);
        logs.Controls.Add(_log,0,1);
        root.Controls.Add(logs,0,3);

        var footer=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Color.FromArgb(238,243,248)};
        _summary.Dock=DockStyle.Fill;_summary.AutoSize=false;
        _summary.TextAlign=ContentAlignment.MiddleLeft;_summary.Padding=new Padding(12,0,0,0);
        footer.Controls.Add(_summary);root.Controls.Add(footer,0,4);
        ResumeLayout(true);
    }

    static Label SectionTitle(string text)=>new(){Text=text,AutoSize=false,Width=170,Height=34,
        Font=new Font("Segoe UI Semibold",10,FontStyle.Bold),ForeColor=Color.FromArgb(30,43,61),
        Margin=new Padding(6,0,4,0),TextAlign=ContentAlignment.MiddleLeft};

    static void MakePrimary(Button button)
    {
        button.BackColor=Color.FromArgb(29,112,215);
        button.ForeColor=Color.White;
        button.FlatAppearance.BorderColor=Color.FromArgb(29,112,215);
        button.Font=new Font("Segoe UI Semibold",9,FontStyle.Bold);
    }

    void UpdateSelectedDeviceInfo()
    {
        var d=SelectedDevice();
        if(d is null)
        {
            if(_detailLogSerial is not null)
            {
                _detailLogSerial=null;
                RebuildLogView();
            }
            _selectedPhoneTitle.Text="Chưa chọn thiết bị";
            _selectedPhoneDetail.Text="Chọn một điện thoại từ bảng.";
            return;
        }
        _selectedPhoneTitle.Text=d.Name+"   ·   "+d.Connection;
        _selectedPhoneDetail.Text=$"ADB: {d.Serial}  |  systemPort: {d.SystemPort}  |  Appium: {d.AppiumStatus}\n"+
            (string.IsNullOrWhiteSpace(d.AccountEmail)?"Chưa có tài khoản":d.AccountEmail);
        if(!string.Equals(_detailLogSerial,d.Serial,StringComparison.OrdinalIgnoreCase))
        {
            _detailLogSerial=d.Serial;
            RebuildLogView();
        }
        string? filterItem=_logFilter.Items.Cast<string>().FirstOrDefault(item=>
            item.EndsWith($"[{d.Serial}]",StringComparison.OrdinalIgnoreCase));
        if(filterItem is not null && !Equals(_logFilter.SelectedItem,filterItem))
            _logFilter.SelectedItem=filterItem;
        // Do not overwrite an actively edited session duration on every timer refresh.
        if(!_minutes.Focused)_minutes.Value=Math.Clamp(d.SessionMinutes,1,1440);
    }

    // V4: lifecycle settings belong to ONE DeviceId. Legacy V3 settings are
    // migrated per device on first launch and never edited as a shared policy.
    void ConfigureAccountLifecycle(DeviceRecord? preferredDevice=null)
    {
        if(_settings.Devices.Count==0)
        {
            MessageBox.Show(this,"Chưa có điện thoại. Hãy kết nối ADB trước.",
                "Vòng đời tài khoản",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        var selected=preferredDevice??SelectedDevice()??_settings.Devices[0];
        using var dlg=new Form {Text="Vòng đời tài khoản riêng từng điện thoại — V5",
            StartPosition=FormStartPosition.CenterParent,Width=640,Height=510,
            FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,
            MinimizeBox=false,ShowInTaskbar=false};
        var deviceLabel=new Label {Text="Áp dụng cho đúng một điện thoại:",
            Left=18,Top=20,Width=275,Height=27};
        var devices=new ComboBox {Left=298,Top=18,Width=275,
            DropDownStyle=ComboBoxStyle.DropDownList};
        var orderedDevices=_settings.Devices.OrderBy(d=>d.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach(var device in orderedDevices)
            devices.Items.Add($"{device.Name}  |  {device.DisplayAlias}  |  {device.Serial}");
        var durationLabel=new Label {Text="Giới hạn thời gian chạy tài khoản (giờ):",
            Left=18,Top=75,Width=348,Height=28};
        // UI in hours, worker still stores integer minutes. 0.17 giờ ~= 10 phút.
        var duration=new NumericUpDown {Left=410,Top=73,Width=140,
            DecimalPlaces=2,Increment=0.25m,
            Minimum=AccountDurationDisplay.MinInputHours,
            Maximum=AccountDurationDisplay.MaxInputHours};
        var rotate=new CheckBox {Text="Tự chuyển sang tài khoản chưa dùng khi hết giờ",
            Left=18,Top=117,Width=550,Height=28};
        var singleUse=new CheckBox {Text="AUTO chỉ chọn tài khoản chưa dùng (luôn bật; chạy lại bằng nút thủ công)",
            Left=18,Top=154,Width=550,Height=28};
        var purge=new CheckBox {Text="Tự xóa thông tin đăng nhập của tài khoản đã dùng khỏi kho",
            Left=18,Top=191,Width=560,Height=28};
        var purgeLabel=new Label {Text="Xóa sau khi kết thúc phiên (giờ):",
            Left=18,Top=231,Width=348,Height=28};
        var purgeHours=new NumericUpDown {Left=410,Top=229,Width=140,Minimum=0,Maximum=8760};
        void LoadForSelected()
        {
            if(devices.SelectedIndex<0)return;
            var policy=_settings.AccountPolicyFor(orderedDevices[devices.SelectedIndex]);
            duration.Value=AccountDurationDisplay.MinutesToInputHours(policy.SessionLimitMinutes);
            rotate.Checked=policy.AutoRotateAccounts;
            singleUse.Checked=true;
            singleUse.Enabled=false;
            purge.Checked=policy.AutoPurgeUsedAccounts;
            purgeHours.Value=Math.Clamp(policy.PurgeUsedAfterHours,0,8760);
            purgeHours.Enabled=purge.Checked;
        }
        purge.CheckedChanged+=(_,_)=>purgeHours.Enabled=purge.Checked;
        devices.SelectedIndexChanged+=(_,_)=>LoadForSelected();
        devices.SelectedIndex=Math.Max(0,orderedDevices.FindIndex(d=>d.DeviceId==selected.DeviceId));
        var changeLabel=new Label {Text="Nếu máy đang chạy, áp dụng cấu hình:",
            Left=18,Top=268,Width=285,Height=25};
        var applyMode=new ComboBox {Left=305,Top=265,Width=286,
            DropDownStyle=ComboBoxStyle.DropDownList};
        applyMode.Items.AddRange(new object[] {
            "Từ phiên tài khoản tiếp theo",
            "Ngay cho phiên đã xác minh"
        });
        applyMode.SelectedIndex=0;
        var note=new Label {Left=18,Top=307,Width=600,Height=84,
            Text="Áp dụng ngay: hạn mới tính từ lúc tài khoản đã được xác minh, KHÔNG tính từ lúc Lưu. " +
                 "Worker Start LIVE chưa xác minh tài khoản không thể kích hoạt đồng hồ giữa phiên; " +
                 "cần Stop rồi Start LIVE để xác minh. Chỉ xóa dữ liệu khi một phiên đã xác minh hết hạn.",
            ForeColor=Color.FromArgb(71,85,105)};
        var ok=new Button {Text="Lưu riêng máy này",Left=345,Top=408,Width=125,
            DialogResult=DialogResult.OK};
        var cancel=new Button {Text="Hủy",Left=482,Top=408,Width=90,
            DialogResult=DialogResult.Cancel};
        dlg.Controls.AddRange([deviceLabel,devices,durationLabel,duration,rotate,
            singleUse,purge,purgeLabel,purgeHours,changeLabel,applyMode,note,ok,cancel]);
        dlg.AcceptButton=ok;dlg.CancelButton=cancel;
        if(dlg.ShowDialog(this)!=DialogResult.OK || devices.SelectedIndex<0)return;
        var target=orderedDevices[devices.SelectedIndex];
        target.AccountLifecycle=new AccountLifecyclePolicy
        {
            SessionLimitMinutes=AccountDurationDisplay.InputHoursToMinutes(duration.Value),
            AutoRotateAccounts=rotate.Checked,
            NeverReuseUsedAccounts=true, // V5.1 AUTO fresh only
            AutoPurgeUsedAccounts=purge.Checked,
            PurgeUsedAfterHours=(int)purgeHours.Value
        };
        Storage.SaveSettings(_settings);
        _orchestrator?.ApplyAccountPolicy();
        var applyResult=_orchestrator?.ApplyLifecycleToRunning(target,applyMode.SelectedIndex==1);
        if(applyResult is { } result)
        {
            Log($"ACCOUNT_LIFECYCLE_APPLY: device={target.Name}; immediate={applyMode.SelectedIndex==1}; {result.Detail}");
            if(applyMode.SelectedIndex==1 && !result.Applied &&
               _orchestrator?.IsRunning(target.Serial)==true)
                MessageBox.Show(this,result.Detail,"Giới hạn phiên hiện tại",
                    MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        CleanupUsedAccountsIfDue(force:true);
        Log($"ACCOUNT_LIFECYCLE_DEVICE_CONFIG: device={target.Name}; deviceId={target.DeviceId}; " +
            $"sessionMinutes={target.AccountLifecycle.SessionLimitMinutes}; " +
            $"autoRotate={target.AccountLifecycle.AutoRotateAccounts}; " +
            $"neverReuse={target.AccountLifecycle.NeverReuseUsedAccounts}; " +
            $"autoPurge={target.AccountLifecycle.AutoPurgeUsedAccounts}; " +
            $"purgeAfterHours={target.AccountLifecycle.PurgeUsedAfterHours}");
        RefreshViews();
    }

    void CleanupUsedAccountsIfDue(bool force=false)
    {
        if(_vault is null)return;
        var now=DateTimeOffset.UtcNow;
        if(!force && now-_lastAccountPurgePoll<TimeSpan.FromMinutes(1))return;
        _lastAccountPurgePoll=now;
        try
        {
            int count=_vault.PurgeDueUsedCredentials(now,
                serial=>_settings.Devices.Any(d=>d.Serial.Equals(serial,StringComparison.OrdinalIgnoreCase)) &&
                    _settings.AccountPolicyFor(serial).AutoPurgeUsedAccounts,
                serial=>_settings.AccountPolicyFor(serial).PurgeUsedAfterHours);
            if(count>0)Log($"ACCOUNT_CREDENTIALS_PURGED: {count} tài khoản đã đủ hạn " +
                "theo cấu hình máy tương ứng; chỉ xóa credential, giữ fingerprint.");
        }
        catch(Exception ex){Log("ACCOUNT_PURGE_ERROR: "+ex.GetType().Name+"; "+ex.Message);}
    }

    void ConfigureSystem()
    {
        using var dlg=new Form {Text="Cấu hình hệ thống",StartPosition=FormStartPosition.CenterParent,
            Width=500,Height=365,FormBorderStyle=FormBorderStyle.FixedDialog,
            MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false};
        var label=new Label {Text="Số điện thoại chạy song song tối đa:",Left=18,Top=25,Width=258,Height=26};
        var numeric=new NumericUpDown {Left=290,Top=23,Width=80,Minimum=1,Maximum=100,
            Value=_parallel.Value};
        var retryLabel=new Label {Text="Số lần tạo lại Appium session:",Left=18,Top=66,Width=258,Height=26};
        var retryNumeric=new NumericUpDown {Left=290,Top=64,Width=80,Minimum=0,Maximum=10,
            Value=Math.Clamp(_settings.MaxSessionRecoveryAttempts,0,10)};
        var roomLimitLabel=new Label {Text="Giới hạn thiết bị tối đa mỗi phòng LIVE:",
            Left=18,Top=107,Width=285,Height=26};
        var roomLimitNumeric=new NumericUpDown {Left=315,Top=104,Width=80,Minimum=1,Maximum=100,
            Value=Math.Clamp(_settings.MaxDevicesPerLiveRoom,1,100)};
        var autoProfile=new CheckBox {
            Text="Bật tự động cập nhật Profile (Tên, Tiểu sử, Ảnh)",
            Left=18,Top=145,Width=440,Height=27,Checked=_settings.AutoUpdateProfile,
            AutoSize=false
        };
        var note=new Label {Text="Mặc định tắt. Việc tắt chỉ bỏ qua Edit Profile; xác minh đăng nhập và username vẫn chạy đầy đủ.",
            Left=18,Top=180,Width=450,Height=43,ForeColor=Color.FromArgb(71,85,105)};
        var more=new Button {Text="Chi tiết Profile",Left=18,Top=250,Width=128};
        more.Click+=(_,_)=>ConfigureProfiles();
        var selectors=new Button {Text="Sửa selectors",Left=154,Top=250,Width=112};
        selectors.Click+=(_,_)=>OpenSelectors();
        var lifecycle=new Button {Text="Vòng đời tài khoản",Left=18,Top=288,Width=165};
        lifecycle.Click+=(_,_)=>ConfigureAccountLifecycle();
        var ok=new Button {Text="Lưu",Left=310,Top=250,Width=76,DialogResult=DialogResult.OK};
        var cancel=new Button {Text="Hủy",Left=395,Top=250,Width=76,DialogResult=DialogResult.Cancel};
        dlg.Controls.AddRange([label,numeric,retryLabel,retryNumeric,roomLimitLabel,
            roomLimitNumeric,autoProfile,note,more,selectors,lifecycle,ok,cancel]);
        dlg.AcceptButton=ok;dlg.CancelButton=cancel;
        if(dlg.ShowDialog(this)!=DialogResult.OK)return;
        int previousRetry=_settings.MaxSessionRecoveryAttempts;
        int previousRoomLimit=_settings.MaxDevicesPerLiveRoom;
        _parallel.Value=numeric.Value;
        _settings.MaxSessionRecoveryAttempts=(int)retryNumeric.Value;
        _settings.MaxDevicesPerLiveRoom=(int)roomLimitNumeric.Value;
        _settings.AutoUpdateProfile=autoProfile.Checked;
        SaveSettings();
        if(previousRetry!=_settings.MaxSessionRecoveryAttempts)
            Log($"APPIUM_RECOVERY_LIMIT_UPDATED: old={previousRetry}; " +
                $"new={_settings.MaxSessionRecoveryAttempts}; nextRecoveryCycle=true");
        if(previousRoomLimit!=_settings.MaxDevicesPerLiveRoom)
            Log($"ROOM_DEVICE_LIMIT_UPDATED: old={previousRoomLimit}; " +
                $"new={_settings.MaxDevicesPerLiveRoom}; nextAcquisition=true; existingRoomUnchanged=true");
        try
        {
            var liveSettings=LiveManagerSettingsStore.Load();
            liveSettings.MaxDevicesPerRoom=_settings.MaxDevicesPerLiveRoom;
            LiveManagerSettingsStore.Save(liveSettings);
        }
        catch(Exception ex)
        {
            Log("Không đồng bộ được giới hạn phòng sang cấu hình LIVE: "+ex.Message);
        }
        var profileOptions=ProfileUpdateStore.Read();
        profileOptions.Enabled=autoProfile.Checked;
        ProfileUpdateStore.Save(profileOptions);
    }

    void OpenLiveSearch()
    {
        var d=SelectedDevice();
        if(d is null){MessageBox.Show(this,"Chọn một điện thoại ở bảng thiết bị trước.");return;}
        if(_orchestrator?.IsRunning(d.Serial)==true)
        {
            MessageBox.Show(this,"Điện thoại đang có worker hoạt động. Stop máy và đợi kết thúc trước khi mở Tìm LIVE để tránh hai phiên Appium cùng điều khiển một thiết bị.",
                "Thiết bị đang bận",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        try
        {
            var config=LiveManagerSettingsStore.Load();
            using var dialog=new LiveSearchDialog(_settings,d,config,Log);
            dialog.ShowDialog(this);
        }
        catch(Exception ex){Log("LIVE Search: "+ex.Message);MessageBox.Show(this,ex.Message,"Không mở được LIVE Search");}
    }

    void ConfigureLive()
    {
        try
        {
            using var dialog = new LiveSettingsDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                int oldRoomLimit=_settings.MaxDevicesPerLiveRoom;
                _settings.MaxDevicesPerLiveRoom=LiveManagerSettingsStore.Load().MaxDevicesPerRoom;
                Storage.SaveSettings(_settings);
                if(oldRoomLimit!=_settings.MaxDevicesPerLiveRoom)
                    Log($"ROOM_DEVICE_LIMIT_UPDATED: old={oldRoomLimit}; new={_settings.MaxDevicesPerLiveRoom}; nextAcquisition=true");
                Log("Đã lưu cấu hình LIVE Manager. Tìm nguồn LIVE đã bật (chỉ đọc card, chưa gửi bình luận).");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không mở được cấu hình LIVE", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log("Cấu hình LIVE không mở được: " + ex.GetType().Name);
        }
    }

    void ConfigureProfiles()
    {
        using var dialog=new ProfileUpdateDialog();
        if(dialog.ShowDialog(this)==DialogResult.OK)
        {
            // Keep the legacy detailed Profile dialog and the new system-wide
            // master switch consistent in both directions.
            _settings.AutoUpdateProfile=ProfileUpdateStore.Read().Enabled;
            Storage.SaveSettings(_settings);
            Log("Đã lưu cấu hình hồ sơ dùng chung cho tất cả điện thoại. Chỉ áp dụng sau khi xác minh tài khoản.");
        }
    }

    void Log(string message)
    {
        string tagged=message;
        if(!message.StartsWith("[",StringComparison.Ordinal))
        {
            var device=_settings.Devices.FirstOrDefault(d=>
                message.StartsWith(d.Name+":",StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith(d.Name+" -",StringComparison.OrdinalIgnoreCase));
            tagged=device is null?$"[SYSTEM] {message}":$"[{device.Serial}] {message}";
        }
        string line=$"[{DateTime.Now:HH:mm:ss}] {tagged}";
        lock(_logGate)
        {
            try{Directory.CreateDirectory(Storage.Folder);File.AppendAllText(Storage.LogFile,line+Environment.NewLine);}catch{}
            _logLines.Add(line);
            if(_logLines.Count>3000)_logLines.RemoveRange(0,_logLines.Count-2500);
        }
        if(_closing||IsDisposed)return;
        if(InvokeRequired) {try{BeginInvoke(new Action(()=>AppendLog(line)));}catch(InvalidOperationException){} }
        else AppendLog(line);
    }
    void AppendLog(string line)
    {
        if(IsDisposed)return;
        if(!LogLineMatchesFilter(line))return;
        _log.AppendText(line+Environment.NewLine);
        if(_log.TextLength>100000)_log.Text=_log.Text[^65000..];
    }
    bool LogLineMatchesFilter(string line)
    {
        // The Master-Detail textbox is scoped to the selected phone. Match
        // exact serial metadata so logs from concurrent workers never mix.
        if(!string.IsNullOrWhiteSpace(_detailLogSerial))
            return line.Contains($"[{_detailLogSerial}]",StringComparison.OrdinalIgnoreCase);
        if(_logFilter.SelectedIndex<=0)return true;
        string selected=_logFilter.SelectedItem?.ToString()??"";
        int open=selected.LastIndexOf('['),close=selected.LastIndexOf(']');
        if(open<0 || close<=open)return true;
        string serial=selected[(open+1)..close];
        return line.Contains($"[{serial}]",StringComparison.OrdinalIgnoreCase);
    }
    void RebuildLogView()
    {
        if(IsDisposed)return;
        string[] lines;
        lock(_logGate)lines=_logLines.Where(LogLineMatchesFilter).ToArray();
        _log.Lines=lines;
        _log.SelectionStart=_log.TextLength;_log.ScrollToCaret();
    }
    void Notify()
    {
        if(_closing||IsDisposed||!IsHandleCreated)return;
        try {BeginInvoke(new Action(RefreshViews));}catch(InvalidOperationException){}
    }
    void RefreshViews()
    {
        if(_closing||IsDisposed)return;
        string? selected=SelectedDevice()?.Serial;
        if(!_deviceGrid.IsCurrentCellInEditMode)
        {
            _refreshingDeviceGrid=true;
            _checkedDeviceSerials.IntersectWith(_settings.Devices.Select(d=>d.Serial));
            _deviceGrid.Rows.Clear();
            foreach(var d in _settings.Devices)
            {
                string status=d.Connection=="OFFLINE"?"Disconnected":d.WorkerState;
                int i=_deviceGrid.Rows.Add(_checkedDeviceSerials.Contains(d.Serial),d.Name,status,
                    d.AccountEmail,d.Serial);
                if(d.Connection=="OFFLINE")_deviceGrid.Rows[i].DefaultCellStyle.ForeColor=Color.Gray;
                if(d.Stage is "ERROR" or "RESTRICTED" or "ACCOUNT_CHECKPOINT" or "WAIT_REVIEW" or "PROFILE_REVIEW")_deviceGrid.Rows[i].DefaultCellStyle.BackColor=Color.FromArgb(254,242,242);
            }
            if(selected is not null)
            {
                foreach(DataGridViewRow row in _deviceGrid.Rows)
                    if(string.Equals(row.Cells["Serial"].Value?.ToString(),selected,StringComparison.OrdinalIgnoreCase))
                    {row.Selected=true;_deviceGrid.CurrentCell=row.Cells["Name"];break;}
            }
            _refreshingDeviceGrid=false;
        }
        // Timer refresh must NEVER clear checkboxes selected for batch actions.
        _refreshingAccountGrid=true;
        int firstAccountRow=-1;
        try{firstAccountRow=_accountGrid.FirstDisplayedScrollingRowIndex;}catch(InvalidOperationException){}
        try
        {
            if(_vault is not null)
            {
                var selectedDevice=SelectedDevice();
                var accounts=selectedDevice is null?new List<ManagedAccount>():_vault.Snapshot(selectedDevice.Serial);
                _checkedAccountIds.Clear();
                foreach(var account in accounts.Where(a=>a.IsSelectedForRun))
                    _checkedAccountIds.Add(account.Id);
                _accountGrid.DataSource=accounts;
                foreach(DataGridViewRow row in _accountGrid.Rows)
                {
                    if(row.DataBoundItem is not ManagedAccount a)continue;
                    if(a.EverUsed || a.LastStart.HasValue)
                    {
                        row.DefaultCellStyle.ForeColor=Color.FromArgb(180,30,45);
                        row.DefaultCellStyle.BackColor=Color.FromArgb(254,237,237);
                        continue;
                    }
                    if(a.State is "NEEDS_REVIEW" or "RESTRICTED" or "ACCOUNT_CHECKPOINT" or "INVALID")
                        row.DefaultCellStyle.BackColor=Color.FromArgb(254,242,242);
                    if(a.State=="RETRY_PENDING")
                        row.DefaultCellStyle.BackColor=Color.FromArgb(255,251,235);
                }
                _summary.Text=$"Kho: {accounts.Count}  |  Chưa dùng: {accounts.Count(a=>a.State=="AVAILABLE" && !a.EverUsed && !a.LastStart.HasValue)}  |  Đã dùng: {accounts.Count(a=>a.EverUsed || a.LastStart.HasValue)}  |  Tick: {_checkedAccountIds.Count}";
            }
            else
            {
                _checkedAccountIds.Clear();
                _accountGrid.DataSource=new List<ManagedAccount>();
                _summary.Text=$"Thiết bị: {_settings.Devices.Count}  |  Kho tài khoản: CHƯA MỞ";
            }
        }
        finally { _refreshingAccountGrid=false; }
        if(firstAccountRow>=0 && firstAccountRow<_accountGrid.RowCount)
        {
            try{_accountGrid.FirstDisplayedScrollingRowIndex=firstAccountRow;}
            catch(InvalidOperationException){}
        }
        UpdateAccountSelectionHeader();
        PositionAccountSelectionHeader();
        UpdateDeviceSelectionHeader();
        PositionDeviceSelectionHeader();
        UpdateLogFilterDevices();
        int onlineCount=_settings.Devices.Count(x=>x.Connection=="ONLINE");
        _deviceStatus.Text=$"{_settings.Devices.Count} máy  ·  Online {onlineCount}";
        _deviceStatus.ForeColor=onlineCount>0?Color.FromArgb(21,128,61):Color.FromArgb(180,83,9);
        UpdateSelectedDeviceInfo();
        if(_dashboard?.Visible==true)_dashboard.RefreshData();
    }
    DeviceRecord? SelectedDevice()
    {
        if(_deviceGrid.SelectedRows.Count==0)return null;
        string? serial=_deviceGrid.SelectedRows[0].Cells["Serial"].Value?.ToString();
        return _settings.Devices.FirstOrDefault(x=>x.Serial==serial);
    }
    // Business actions use only explicit checkboxes, never blue row highlighting.
    List<Guid> SelectedAccountIds()
    {
        var device=SelectedDevice();
        if(_vault is null || device is null)return [];
        return _vault.Snapshot(device.Serial)
            .Where(account=>account.IsSelectedForRun)
            .Select(account=>account.Id)
            .ToList();
    }

    void SetCheckedForVisibleDevices(bool check)
    {
        _refreshingDeviceGrid=true;
        try
        {
            foreach(DataGridViewRow row in _deviceGrid.Rows)
            {
                string serial=row.Cells["Serial"].Value?.ToString()??"";
                row.Cells["Checked"].Value=check;
                if(check)_checkedDeviceSerials.Add(serial);else _checkedDeviceSerials.Remove(serial);
            }
        }
        finally{_refreshingDeviceGrid=false;}
        UpdateDeviceSelectionHeader();
    }
    void UpdateDeviceSelectionHeader()
    {
        int total=_deviceGrid.Rows.Count;
        int selected=_deviceGrid.Rows.Cast<DataGridViewRow>().Count(row=>
            _checkedDeviceSerials.Contains(row.Cells["Serial"].Value?.ToString()??""));
        _checkAllDevices.CheckState=selected==0?CheckState.Unchecked:
            selected==total && total>0?CheckState.Checked:CheckState.Indeterminate;
        if(_removeDevicesButton is not null)_removeDevicesButton.Enabled=selected>0;
        UpdateDashboardSummary();
    }
    void PositionDeviceSelectionHeader()
    {
        if(!_deviceGrid.IsHandleCreated || !_deviceGrid.Columns.Contains("Checked"))return;
        var r=_deviceGrid.GetCellDisplayRectangle(_deviceGrid.Columns["Checked"].Index,-1,true);
        _checkAllDevices.SetBounds(r.Left+4,r.Top+2,Math.Max(0,r.Width-8),Math.Max(22,r.Height-4));
        _checkAllDevices.Visible=r.Width>=28 && r.Height>=20;
        _checkAllDevices.BringToFront();
    }
    void UpdateLogFilterDevices()
    {
        string current=_logFilter.SelectedItem?.ToString()??"All devices";
        var desired=new[]{"All devices"}.Concat(_settings.Devices.Select(d=>$"{d.Name} [{d.Serial}]")).ToArray();
        if(_logFilter.Items.Cast<string>().SequenceEqual(desired))return;
        _logFilter.BeginUpdate();_logFilter.Items.Clear();_logFilter.Items.AddRange(desired);
        _logFilter.SelectedItem=desired.Contains(current)?current:"All devices";_logFilter.EndUpdate();
    }
    void UpdateDashboardSummary()
    {
        int accountCount=_vault?.Snapshot().Count??0;
        int running=_orchestrator?.RunningCount??0;
        int waiting=_orchestrator?.WaitingCount??0;
        int limit=_orchestrator?.ConcurrencyLimit??Math.Clamp(_settings.MaxParallel,1,100);
        _summary.Text=$"Thiết bị chọn: {_checkedDeviceSerials.Count}  |  Đang chạy: {running}/{limit}  |  Đợi slot: {waiting}  |  Online: {_settings.Devices.Count(x=>x.Connection=="ONLINE")}  |  Tài khoản: {accountCount}  |  Kho: {(_vault is null?"CHƯA MỞ":"ĐÃ MỞ")}";
    }

    void SetCheckedForVisibleAccounts(bool check)
    {
        _refreshingAccountGrid=true;
        try
        {
            var accounts=_accountGrid.Rows.Cast<DataGridViewRow>()
                .Select(row=>row.DataBoundItem as ManagedAccount)
                .Where(account=>account is not null)
                .Cast<ManagedAccount>()
                .ToList();
            _vault?.SetRunSelection(accounts.Select(account=>account.Id),check);
            foreach(var account in accounts)account.IsSelectedForRun=check;
            _accountGrid.Refresh();
            _checkedAccountIds.Clear();
            if(check)foreach(var account in accounts)_checkedAccountIds.Add(account.Id);
        }
        finally { _refreshingAccountGrid=false; }
        UpdateAccountSelectionHeader();
    }
    void UpdateAccountSelectionHeader()
    {
        int total=_accountGrid.Rows.Count;
        int checkedCount=_accountGrid.Rows.Cast<DataGridViewRow>()
            .Count(row=>row.DataBoundItem is ManagedAccount account && account.IsSelectedForRun);
        _checkAllAccounts.CheckState=checkedCount==0?CheckState.Unchecked:
            checkedCount==total?CheckState.Checked:CheckState.Indeterminate;
        _checkAllAccounts.Text="Tất cả";
        if(_vault is not null)
            _summary.Text=$"Thiết bị: {_settings.Devices.Count}  |  Online: {_settings.Devices.Count(x=>x.Connection=="ONLINE")}  |  Tài khoản: {_accountGrid.Rows.Count}  |  Đã chọn: {_checkedAccountIds.Count}  |  Kho: ĐÃ MỞ";
    }
    void PositionAccountSelectionHeader()
    {
        if(!_accountGrid.IsHandleCreated || _accountGrid.Columns.Count==0)return;
        var r=_accountGrid.GetCellDisplayRectangle(_accountGrid.Columns["Checked"].Index,-1,true);
        _checkAllAccounts.SetBounds(r.Left+5,r.Top+2,Math.Max(0,r.Width-10),Math.Max(22,r.Height-4));
        _checkAllAccounts.Visible=r.Width>=28 && r.Height>=20;
        _checkAllAccounts.BringToFront();
    }
    void SaveSettings()
    {
        _settings.AdbPath=_adbPath.Text.Trim();_settings.AppiumUrl=_appium.Text.Trim();
        _settings.MaxParallel=(int)_parallel.Value;
        Storage.SaveSettings(_settings);
        // Update the existing coordinator after persistence, even when workers
        // are active or waiting. Waiters are granted in FIFO order.
        _orchestrator?.UpdateConcurrencyLimit(_settings.MaxParallel);
        Log($"CONFIG_SAVED_APPLIED: maxParallel={_settings.MaxParallel}; " +
            $"sessionRetry={_settings.MaxSessionRecoveryAttempts}; " +
            $"roomLimit={_settings.MaxDevicesPerLiveRoom}; runtime=true");
        UpdateDashboardSummary();
    }
    void UnlockVault()
    {
        if(_vault is not null){Log("Kho đã mở.");return;}
        string? password=InputDialog.Show(this,"Mật khẩu kho được mã hóa", "Nhập mật khẩu kho (tối thiểu 8 ký tự; lần đầu là tạo mới):",true);
        if(password is null)return;
        try
        {
            _vault=new AccountVault(password);
            _vault.SingleUseForSerial=serial=>_settings.AccountPolicyFor(serial).NeverReuseUsedAccounts;
            _orchestrator=new DeviceOrchestrator(_settings,_vault,Log,Notify);
            CleanupUsedAccountsIfDue(force:true);
            _unlock.Text="Kho đã mở";_unlock.Enabled=false;
            Log("Kho tài khoản đã mở. Mật khẩu tài khoản được bảo vệ trong account-vault.json.");
            RefreshViews();
        }
        catch(Exception ex){_vault?.Dispose();_vault=null;MessageBox.Show(this,ex.Message,"Không mở được kho",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    async Task SafeAsync(Func<Task> action)
    {
        try{await action();}
        catch(Exception ex){Log("Lỗi: "+ex.Message);MessageBox.Show(this,ex.Message,"Thao tác lỗi",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    async Task RefreshAsync()
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        SaveSettings();await _orchestrator.RefreshAsync();RefreshViews();
    }
    async Task ConnectRemoteAsync()
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        string? endpoint=InputDialog.Show(this,"Kết nối XCloudPhone", "Nhập Remote ADB hiện tại (host:port):",false,_remoteEndpoint.Text.Trim());
        if(endpoint is null)return;
        _remoteEndpoint.Text=endpoint.Trim();
        await ConnectRemoteFromTopAsync();
    }
    async Task ConnectRemoteFromTopAsync()
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho tài khoản trước khi kết nối thiết bị.");return;}
        string endpoint=_remoteEndpoint.Text.Trim();
        if(string.IsNullOrWhiteSpace(endpoint)) {MessageBox.Show(this,"Nhập địa chỉ Remote ADB dạng host:port.");return;}
        SaveSettings();
        await _orchestrator.ConnectAsync(endpoint);
        RefreshViews();
    }
    async Task ChangeDeviceEndpointAsync(DeviceRecord? selected=null)
    {
        if(_orchestrator is null || _vault is null)
        {MessageBox.Show(this,"Mở kho trước.");return;}
        var device=selected??SelectedDevice();
        if(device is null){MessageBox.Show(this,"Chọn máy cần cập nhật ADB.");return;}
        if(_orchestrator.IsRunning(device.Serial))
        {MessageBox.Show(this,"Hãy Stop máy này và chờ Appium giải phóng trước khi cập nhật ADB.");return;}
        string oldSerial=device.Serial;
        string? endpoint=InputDialog.Show(this,"Sửa Remote ADB — " + device.Name,
            "Nhập host:port MỚI của cùng Phone; không cần xóa máy:",false,oldSerial);
        if(endpoint is null)return;
        endpoint=DeviceIdentity.NormalizeSerial(endpoint);
        if(endpoint.Equals(oldSerial,StringComparison.OrdinalIgnoreCase))return;
        int accounts=_vault.Snapshot(oldSerial).Count;
        if(MessageBox.Show(this,
            $"Cập nhật Remote ADB cho {device.Name}?\n\n"+
            $"Cũ: {oldSerial}\nMới: {endpoint}\n\n"+
            $"Giữ nguyên {accounts} tài khoản, DeviceId, lịch sử và cấu hình vòng đời.\n"+
            "Chỉ thực hiện nếu hai endpoint là cùng một điện thoại thuê.",
            "Xác nhận giữ kho tài khoản",MessageBoxButtons.YesNo,MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        var result=await _orchestrator.RebindEndpointAsync(device,endpoint);
        if(!result.Success)
        {MessageBox.Show(this,result.Detail,"Không đổi ADB",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        _checkedDeviceSerials.Remove(oldSerial);
        _detailLogSerial=string.Equals(_detailLogSerial,oldSerial,StringComparison.OrdinalIgnoreCase)
            ? endpoint : _detailLogSerial;
        Log(result.Detail);
        RefreshViews();
    }
    async Task RemoveSelectedDeviceAsync()
    {
        var device=SelectedDevice();
        if(device is null)
        {
            MessageBox.Show(this,"Chọn thiết bị cần gỡ trong danh sách.","Gỡ thiết bị");
            return;
        }
        var answer=MessageBox.Show(this,
            "Bạn có chắc chắn muốn xóa thiết bị này khỏi danh sách quản lý không?\n\n"+
            $"Thiết bị: {device.Name}\nADB Serial: {device.Serial}\n\n"+
            "Worker đang chạy sẽ được dừng và Appium session sẽ được giải phóng.\n"+
            "Kho tài khoản và diagnostics cũ được giữ lại.",
            "Xác nhận gỡ thiết bị",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if(answer!=DialogResult.Yes)return;

        var result=await RemoveDeviceFromManagementAsync(device);
        if(!result.Success)
        {
            MessageBox.Show(this,result.Detail,"Chưa thể gỡ thiết bị",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);
            RefreshViews();
            return;
        }
        _checkedDeviceSerials.Remove(device.Serial);
        RefreshViews();
        MessageBox.Show(this,"Đã gỡ thiết bị khỏi danh sách quản lý.",
            "Hoàn tất",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    async Task RemoveCheckedDevicesAsync()
    {
        var devices=_settings.Devices
            .Where(device=>_checkedDeviceSerials.Contains(device.Serial))
            .ToList();
        if(devices.Count==0)
        {
            MessageBox.Show(this,"Tick ít nhất một thiết bị trong cột đầu tiên.","Gỡ thiết bị");
            return;
        }
        string names=string.Join("\n",devices.Take(8).Select(device=>
            $"• {device.Name} — {device.Serial}"));
        if(devices.Count>8)names+=$"\n• ... và {devices.Count-8} thiết bị khác";
        var answer=MessageBox.Show(this,
            "Bạn có chắc chắn muốn xóa thiết bị này khỏi danh sách quản lý không?\n\n"+
            names+"\n\nWorker đang chạy sẽ được dừng và Appium session sẽ được giải phóng.\n"+
            "Kho tài khoản và diagnostics cũ được giữ lại.",
            $"Xác nhận gỡ {devices.Count} thiết bị",MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2);
        if(answer!=DialogResult.Yes)return;

        var failures=new List<string>();
        int removed=0;
        foreach(var device in devices)
        {
            var result=await RemoveDeviceFromManagementAsync(device);
            if(result.Success)
            {
                removed++;
                _checkedDeviceSerials.Remove(device.Serial);
            }
            else failures.Add($"{device.Name}: {result.Detail}");
        }
        RefreshViews();
        string summary=$"Đã gỡ {removed}/{devices.Count} thiết bị khỏi danh sách quản lý.";
        if(failures.Count>0)summary+="\n\nChưa thể gỡ:\n"+string.Join("\n",failures);
        MessageBox.Show(this,summary,failures.Count==0?"Hoàn tất":"Kết quả gỡ thiết bị",
            MessageBoxButtons.OK,failures.Count==0?MessageBoxIcon.Information:MessageBoxIcon.Warning);
    }

    async Task<(bool Success,string Detail)> RemoveDeviceFromManagementAsync(DeviceRecord device)
    {
        if(_orchestrator is not null)
        {
            var result=await _orchestrator.RemoveDeviceAsync(device);
            if(!result.Success)return result;
            Log($"Đã gỡ {device.Name} ({device.Serial}) khỏi danh sách. Kho tài khoản được giữ nguyên.");
        }
        else
        {
            // Without an unlocked vault no worker/session can exist in this UI.
            // Still disconnect a remote ADB endpoint before persisting removal.
            if(device.Serial.Contains(':'))
            {
                try
                {
                    var adb=new AdbClient(()=>_settings.AdbPath);
                    await adb.DisconnectAsync(device.Serial,CancellationToken.None);
                }
                catch(Exception ex)
                {Log($"[{device.Serial}] Cảnh báo ngắt ADB khi gỡ máy: {ex.GetType().Name}.");}
            }
            _settings.Devices.RemoveAll(d=>
                d.Serial.Equals(device.Serial,StringComparison.OrdinalIgnoreCase));
            if(!_settings.RemovedDeviceSerials.Contains(device.Serial,StringComparer.OrdinalIgnoreCase))
                _settings.RemovedDeviceSerials.Add(device.Serial);
            Storage.SaveSettings(_settings);
            Log($"Đã gỡ {device.Name} ({device.Serial}) khỏi danh sách; không có worker đang chạy.");
        }
        return (true,"Đã gỡ thiết bị khỏi danh sách quản lý và lưu cấu hình.");
    }
    async Task CheckTikTokAsync()
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        var d=SelectedDevice();
        if(d is null){MessageBox.Show(this,"Chọn một điện thoại trước.");return;}
        var observation=await _orchestrator.CheckSelectedStateAsync(d);
        if(observation is not null)
            MessageBox.Show(this,$"Trạng thái TikTok: {observation.State}\n{observation.Detail}",
                "Kiểm tra TikTok",MessageBoxButtons.OK,
                observation.State is TikTokUiState.Unknown or TikTokUiState.SecurityChallenge or TikTokUiState.AccountRestricted
                    ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }
    // No account tick: old device queue mode. Exactly one tick: selected
    // account mode; stop and await old worker before starting the new one.
    async Task StartSelectedAsync()
    {
        if(_orchestrator is null || _vault is null){MessageBox.Show(this,"Mở kho trước.");return;}
        SaveSettings();
        var d=SelectedDevice();if(d is null){MessageBox.Show(this,"Chọn một điện thoại ở bảng trên.");return;}
        var ids=SelectedAccountIds();
        if(ids.Count>1)
        {
            MessageBox.Show(this,"Anh đang tick nhiều tài khoản. Để thử riêng một tài khoản, chỉ tick đúng một ô.\n"+
                "Muốn chạy cả hàng đợi của máy, hãy bấm Bỏ chọn rồi Start máy.","Chọn tài khoản thử");
            return;
        }
        if(ids.Count==1)
        {
            await StartSpecificOnDeviceAsync(d,ids[0]);
            return;
        }
        if(!_orchestrator.Start(d))MessageBox.Show(this,
            string.IsNullOrWhiteSpace(d.Detail)?"Không thể Start. Xem nhật ký.":d.Detail,
            "Không thể Start",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }

    void StartOneDevice(DeviceRecord device)
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        SaveSettings();
        if(!_orchestrator.Start(device))
            MessageBox.Show(this,string.IsNullOrWhiteSpace(device.Detail)?"Không thể Start. Xem nhật ký.":device.Detail,
                "Không thể Start",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }

    async Task StartSelectedDevicesAsync()
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        if(_checkedDeviceSerials.Count==0)
        {MessageBox.Show(this,"Tick ít nhất một thiết bị trong cột đầu tiên.","Start Selected");return;}
        SaveSettings();
        foreach(var device in _settings.Devices.Where(d=>_checkedDeviceSerials.Contains(d.Serial)))
        {
            var ids=_vault?.Snapshot(device.Serial)
                .Where(account=>account.IsSelectedForRun)
                .Select(account=>account.Id).ToList()??[];
            if(ids.Count>1)
            {
                Log($"[{device.Serial}] START_SELECTED_SKIPPED: Local Pool đang tick nhiều hơn một tài khoản; chỉ được chọn đúng một.");
                continue;
            }
            if(ids.Count==1)await _orchestrator.SwitchToSelectedAccountAsync(device,ids[0]);
            else _orchestrator.Start(device);
        }
        RefreshViews();
    }

    void StopSelectedDevices()
    {
        if(_checkedDeviceSerials.Count==0)
        {MessageBox.Show(this,"Tick ít nhất một thiết bị trong cột đầu tiên.","Stop Selected");return;}
        foreach(string serial in _checkedDeviceSerials.ToArray())_orchestrator?.Stop(serial);
        RefreshViews();
    }

    void StartLiveSelectedDevices()
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        if(_checkedDeviceSerials.Count==0)
        {MessageBox.Show(this,"Tick ít nhất một thiết bị trong cột đầu tiên.","Start LIVE Selected");return;}
        var config=LiveManagerSettingsStore.Load();
        if(!config.KeywordSearchEnabled)
        {MessageBox.Show(this,"Bật Tìm LIVE bằng từ khóa trong Cấu hình LIVE trước.");return;}
        SaveSettings();
        foreach(var device in _settings.Devices.Where(d=>_checkedDeviceSerials.Contains(d.Serial)))
            _orchestrator.StartLive(device,config,safeIdleRequested:true,
                card=>Log($"[{device.Serial}] LIVE_CARD: @{card.Creator}; viewers={card.Viewers?.ToString()??"?"}"));
        RefreshViews();
    }

    void StartLiveForCurrentDevice()
    {
        var device=SelectedDevice();
        if(device is null){MessageBox.Show(this,"Chọn một thiết bị trước.");return;}
        StartLiveForDevice(device);
    }

    void StartLiveForDevice(DeviceRecord device)
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        var config=LiveManagerSettingsStore.Load();
        if(!config.KeywordSearchEnabled)
        {MessageBox.Show(this,"Bật Tìm LIVE bằng từ khóa trong Cấu hình LIVE trước.");return;}
        SaveSettings();
        _orchestrator.StartLive(device,config,safeIdleRequested:true,
            card=>Log($"[{device.Serial}] LIVE_CARD: @{card.Creator}; viewers={card.Viewers?.ToString()??"?"}"));
        RefreshViews();
    }

    async Task StartCheckedAccountAsync()
    {
        if(_orchestrator is null || _vault is null){MessageBox.Show(this,"Mở kho trước.");return;}
        SaveSettings();
        var d=SelectedDevice();if(d is null){MessageBox.Show(this,"Chọn điện thoại ở bảng trên.");return;}
        var ids=SelectedAccountIds();
        if(ids.Count!=1)
        {
            MessageBox.Show(this,"Tick đúng MỘT tài khoản ở bảng dưới để chạy thử riêng.",
                "Chạy tài khoản đã tick");
            return;
        }
        await StartSpecificOnDeviceAsync(d,ids[0]);
    }

    async Task StartSpecificOnDeviceAsync(DeviceRecord d,Guid id)
    {
        var account=_vault!.Snapshot().FirstOrDefault(a=>a.Id==id);
        if(account is null || !string.Equals(account.DeviceSerial,d.Serial,StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,"Tài khoản đã tick phải được gán cho đúng điện thoại đang chọn.");
            return;
        }
        Log($"{d.Name}: yêu cầu TEST đúng @{account.ExpectedTikTokHandle}; sẽ dừng worker cũ nếu đang chạy.");
        if(!await _orchestrator!.SwitchToSelectedAccountAsync(d,id))
            MessageBox.Show(this,string.IsNullOrWhiteSpace(d.Detail)?"Không thể chạy tài khoản đã tick.":d.Detail,
                "Không thể chuyển tài khoản",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        RefreshViews();
    }
    // V5.1: deliberate, EXACTLY ONE checked account. Not reachable from
    // Auto/StartSelected/StartAll. Current TikTok owner must match in Profile;
    // no pm clear or Google login when identity is unknown/mismatched.
    async Task StartManualResumeAsync()
    {
        if(_orchestrator is null || _vault is null)
        {MessageBox.Show(this,"Mở kho tài khoản trước.");return;}
        var device=SelectedDevice();
        if(device is null){MessageBox.Show(this,"Chọn máy cần chạy tiếp.");return;}
        var ids=SelectedAccountIds();
        if(ids.Count!=1){MessageBox.Show(this,"Tick đúng MỘT tài khoản cũ trong kho máy.");return;}
        var account=_vault.Snapshot(device.Serial).FirstOrDefault(a=>a.Id==ids[0]);
        if(account is null){MessageBox.Show(this,"Tài khoản không còn trong kho của máy.");return;}
        var policy=_settings.AccountPolicyFor(device);
        if(!account.RunDurationKnown)
        {
            MessageBox.Show(this,"Thời lượng của tài khoản V5 cũ chưa xác định. " +
                "Bấm 'Xác nhận phút đã chạy' trước; không tự đặt lại về 0.",
                "Chưa đủ lịch sử",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        var available=_vault.PreviewManualResume(device.Serial,account.Id,
            policy.SessionLimitMinutes);
        if(available is null)
        {
            MessageBox.Show(this,"Không thể chạy tiếp: đã hết tổng giới hạn, tài khoản " +
                "không hợp lệ, đang chạy hoặc cần kiểm tra. Không sửa EverUsed.",
                "Không đủ điều kiện",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        double remaining=Math.Max(0,policy.SessionLimitMinutes*60-account.AccumulatedRunSeconds);
        string display="@"+account.ExpectedTikTokHandle;
        if(MessageBox.Show(this,
            $"CHẠY TIẾP THỦ CÔNG tài khoản {display} trên {device.Name}?\n"+
            $"Đã chạy cộng dồn: {account.AccumulatedRunSeconds/60:F2} phút\n"+
            $"Giới hạn máy: {policy.SessionLimitMinutes} phút; còn {remaining/60:F2} phút.\n\n"+
            "Tool sẽ đối chiếu chính xác username đang đăng nhập. Nếu khác/không xác minh " +
            "được, sẽ dừng an toàn, KHÔNG pm clear. Auto không được chọn lại tài khoản này.",
            "Xác nhận chạy tiếp",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)
            !=DialogResult.Yes)return;
        var mode=MessageBox.Show(this,
            "Chọn cách chạy tiếp tài khoản đã dùng:\n\n"+
            "YES: Tài khoản này VẪN đang đăng nhập trên máy — xác minh rồi Start LIVE nhanh, " +
            "không pm clear.\n\n"+
            "NO: Đăng nhập lại bằng luồng Preflight/Google bình thường. " +
            "Nếu cần chuyển tài khoản, công đoạn này CÓ THỂ pm clear dữ liệu TikTok/GMS.\n\n"+
            "CANCEL: Không thao tác.",
            "Chạy tiếp thủ công — chọn chế độ",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(mode==DialogResult.Cancel)return;
        SaveSettings();
        bool started;
        if(mode==DialogResult.Yes)
        {
            var live=LiveManagerSettingsStore.Load();
            if(!live.KeywordSearchEnabled)
            {MessageBox.Show(this,"Bật Tìm LIVE bằng từ khóa trong Cấu hình LIVE trước.");return;}
            started=_orchestrator.StartLive(device,live,safeIdleRequested:true,
                card=>Log($"[{device.Serial}] LIVE_CARD: @{card.Creator}; viewers={card.Viewers?.ToString()??"?"}"),
                manualResumeAccountId:account.Id);
        }
        else
        {
            started=await _orchestrator.SwitchToSelectedAccountAsync(device,account.Id,
                manualResume:true);
        }
        if(!started)MessageBox.Show(this,device.Detail,"Không thể chạy tiếp",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        else Log($"MANUAL_RESUME_REQUESTED: device={device.Name}; accountId={account.Id}; " +
            $"elapsedSeconds={account.AccumulatedRunSeconds:F0}; remainingSeconds={remaining:F0}; " +
            $"mode={(mode==DialogResult.Yes?"CURRENT_LOGIN":"GOOGLE_PREFLIGHT")}; " +
            "autoSelection=false; verifiedOwnerRequired=true");
        RefreshViews();
    }

    void ConfirmLegacyAccountMinutes()
    {
        if(_orchestrator is null || _vault is null)
        {MessageBox.Show(this,"Mở kho tài khoản trước.");return;}
        var device=SelectedDevice();
        var ids=SelectedAccountIds();
        if(device is null || ids.Count!=1)
        {MessageBox.Show(this,"Chọn thiết bị và tick đúng MỘT tài khoản.");return;}
        if(_orchestrator.IsRunning(device.Serial))
        {MessageBox.Show(this,"Stop worker và đợi dừng hoàn toàn trước khi xác nhận thời lượng.");return;}
        var a=_vault.Snapshot(device.Serial).SingleOrDefault(x=>x.Id==ids[0]);
        if(a is null || (!a.EverUsed && !a.LastStart.HasValue))
        {MessageBox.Show(this,"Chức năng chỉ dành cho tài khoản từng được chạy.");return;}
        if(a.RunDurationKnown)
        {MessageBox.Show(this,"Tài khoản đã có thời lượng được ghi nhận; không đặt lại đồng hồ.");return;}
        int limit=_settings.AccountPolicyFor(device).SessionLimitMinutes;
        using var form=new Form {Text="Xác nhận thời lượng V5 cũ — "+device.Name,
            Width=560,Height=305,StartPosition=FormStartPosition.CenterParent,
            FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
        var instruction=new Label{Left=15,Top=15,Width=515,Height=82,
            Text=$"Tài khoản @{a.ExpectedTikTokHandle} đã có lịch sử. V5 không lưu đủ tổng phút.\n"+
            "Nhập số phút thực tế đã sử dụng (KHÔNG tự mặc định 0). " +
            "Chỉ xác nhận khi đã đối chiếu nhật ký / bằng chứng.",
            ForeColor=Color.FromArgb(90,55,25)};
        var minutes=new NumericUpDown {Left=15,Top=111,Width=125,
            DecimalPlaces=2,Increment=0.25M,Minimum=0,Maximum=limit,
            Value=(decimal)Math.Clamp(a.RunDurationKnown ? a.AccumulatedRunSeconds/60 : 0,0,limit)};
        var caption=new Label{Left=150,Top=115,Width=300,Height=30,Text=$"phút / giới hạn {limit} phút"};
        var confirm=new CheckBox {Left=15,Top=157,Width=510,Height=46,
            Text="Tôi đã kiểm tra và xác nhận tổng thời gian thực tế; không xóa dấu Đã dùng."};
        var save=new Button {Text="Lưu thời lượng",Left=337,Top=215,Width=140,Height=32,
            DialogResult=DialogResult.OK};
        form.Controls.AddRange([instruction,minutes,caption,confirm,save]);
        form.AcceptButton=save;
        if(form.ShowDialog(this)!=DialogResult.OK)return;
        if(!confirm.Checked)
        {MessageBox.Show(this,"Chưa xác nhận đã đối chiếu lịch sử.");return;}
        try
        {
            _vault.ConfirmLegacyRunMinutes(device.Serial,a.Id,(double)minutes.Value,limit);
            Log($"MANUAL_DURATION_CONFIRMED: device={device.Name}; accountId={a.Id}; " +
                $"elapsedMinutes={minutes.Value}; remainingMinutes={limit-minutes.Value}; operatorReviewed=true");
            RefreshViews();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Không thể xác nhận thời lượng");}
    }

    void StartAll()
    {
        if(_orchestrator is null){MessageBox.Show(this,"Mở kho trước.");return;}
        SaveSettings();foreach(var d in _settings.Devices)_orchestrator.Start(d);
    }
    void StopSelected()
    {
        var d=SelectedDevice();if(d is not null)_orchestrator?.Stop(d.Serial);
    }
    void SaveDeviceMinutes()
    {
        var d=SelectedDevice();if(d is null)return;
        d.SessionMinutes=(int)_minutes.Value;Storage.SaveSettings(_settings);Log($"{d.Name}: đặt {d.SessionMinutes} phút/phiên.");RefreshViews();
    }
    void ImportAccounts()
    {
        if(_vault is null){MessageBox.Show(this,"Mở kho trước.");return;}
        var device=SelectedDevice();
        if(device is null){MessageBox.Show(this,"Chọn thiết bị ở Panel Trái trước khi nhập tài khoản.");return;}
        using var fd=new OpenFileDialog {
            Filter="TXT/Excel (*.txt;*.xlsx)|*.txt;*.xlsx|Text (*.txt)|*.txt|Excel (*.xlsx)|*.xlsx",
            Title="Nhập tài khoản: username|email|password hoặc Excel 3 cột"
        };
        if(fd.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            IEnumerable<string> rows=Path.GetExtension(fd.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase)
                ? ExcelAccountImporter.ReadLines(fd.FileName)
                : File.ReadLines(fd.FileName);
            var result=_vault.ImportLines(device.Serial,rows);
            Log($"[{device.Serial}] LOCAL_POOL_IMPORT: thêm {result.added}, bổ sung username {result.enriched}, trùng {result.duplicates}, không hợp lệ/xung đột {result.invalid}.");
            MessageBox.Show(this,
                $"Thêm mới: {result.added}\nBổ sung username vào kho cũ: {result.enriched}\n"+
                $"Trùng: {result.duplicates}\nKhông hợp lệ/xung đột: {result.invalid}\n\n"+
                "TXT: username|email|password\nExcel: cột Username | Email | Password (sheet đầu).\n"+
                "Kho cũ và trạng thái tài khoản được giữ nguyên.",
                "Kết quả nhập",MessageBoxButtons.OK,MessageBoxIcon.Information);
            RefreshViews();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Lỗi nhập dữ liệu");}
    }
    void DeleteAccountsFromVault()
    {
        if(_vault is null || _orchestrator is null)
        { MessageBox.Show(this,"Mở kho tài khoản trước.");return; }
        var ids=SelectedAccountIds();
        if(ids.Count==0)
        { MessageBox.Show(this,"Chọn một hoặc nhiều tài khoản trong bảng KHO TÀI KHOẢN.");return; }
        var selected=_vault.Snapshot().Where(a=>ids.Contains(a.Id)).ToList();
        if(selected.Count==0){RefreshViews();return;}
        if(selected.Any(a=>a.State is "RESERVED" or "ACTIVE" ||
            (!string.IsNullOrWhiteSpace(a.DeviceSerial) && _orchestrator.IsRunning(a.DeviceSerial))))
        {
            MessageBox.Show(this,
                "Có tài khoản đang RESERVED/ACTIVE hoặc worker trên máy được gán vẫn đang chạy.\n"+
                "Hãy Stop, chờ worker kết thúc và kiểm tra lại trước khi xóa.",
                "Chưa thể xóa",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        string details=string.Join("\n",selected.Take(8).Select(a=>
            $"- @{a.ExpectedTikTokHandle} | {a.Email} | "+
            (string.IsNullOrWhiteSpace(a.DeviceSerial)?"Chưa gán":
              _settings.Devices.FirstOrDefault(d=>d.Serial==a.DeviceSerial)?.Name ?? a.DeviceSerial)));
        if(selected.Count>8)details+=$"\n... và {selected.Count-8} tài khoản khác";
        var answer=MessageBox.Show(this,
            $"XÓA VĨNH VIỄN {selected.Count} tài khoản khỏi kho mã hóa?\n\n{details}\n\n"+
            "Sẽ xóa username, email, mật khẩu và phân công trong kho.\n"+
            "Không thể hoàn tác bằng chức năng Gỡ khỏi máy.\n"+
            "Không xóa tài khoản thật trên TikTok/Google và không tự đăng xuất trên điện thoại.",
            "Xác nhận XÓA KHỎI KHO",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if(answer!=DialogResult.Yes)return;
        try
        {
            // Recheck under the same orchestrator lock used by Start. An active
            // worker appearing between the dialog and commit blocks deletion.
            int count=_orchestrator.DeleteAccounts(ids);
            if(count==0){RefreshViews();return;}
            try{new SessionCheckpointStore().RemoveAccounts(ids);}
            catch(Exception ex){Log("Cảnh báo: chưa dọn được checkpoint tham chiếu tài khoản đã xóa: "+ex.GetType().Name);}
            Log($"Đã xóa {count} tài khoản khỏi kho mã hóa theo yêu cầu. Không thao tác trên điện thoại.");
            RefreshViews();
            MessageBox.Show(this,$"Đã xóa {count} tài khoản khỏi kho mã hóa.",
                "Hoàn tất",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"Không thể xóa tài khoản",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);
            RefreshViews();
        }
    }

    void ResetAccounts()
    {
        if(_vault is null)return;
        var ids=SelectedAccountIds();if(ids.Count==0)return;
        if(MessageBox.Show(this,"Đặt lại cho tài khoản lỗi kỹ thuật hoặc đã được kiểm tra. " +
            "Không dùng Đặt lại để bỏ qua yêu cầu xác minh hoặc hạn chế của TikTok/Google. Tiếp tục?",
            "Đặt lại trạng thái",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        _vault.ResetToAvailable(ids);Log("Đã đặt lại tài khoản sau xác nhận người vận hành.");RefreshViews();
    }
    void SetHandle()
    {
        if(_vault is null)return;
        var ids=SelectedAccountIds();
        if(ids.Count!=1){MessageBox.Show(this,"Chọn đúng một tài khoản ở bảng dưới.");return;}
        var account=_vault.Snapshot().FirstOrDefault(x=>x.Id==ids[0]);
        if(account is null)return;
        string? handle=InputDialog.Show(this,"TikTok handle dự kiến","Nhập TikTok handle chính xác (bỏ trống nếu muốn xác nhận thủ công):",false,account.ExpectedTikTokHandle);
        if(handle is null)return;
        try{_vault.SetHandle(ids[0],handle.TrimStart('@'));RefreshViews();}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Không cập nhật được");}
    }
    void OpenSelectors()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"Config","selectors.json");
        if(File.Exists(path))Process.Start(new ProcessStartInfo("notepad.exe") {ArgumentList={path},UseShellExecute=true});
        else MessageBox.Show(this,"Không tìm thấy Config/selectors.json");
    }
    void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(!_closing && _settings.Devices.Any(x=>_orchestrator?.IsRunning(x.Serial)==true))
        {
            if(MessageBox.Show(this,"Có phiên đang chạy. Đóng tool sẽ ngắt điều khiển và tài khoản được đánh dấu cần kiểm tra. Vẫn thoát?",
                "Xác nhận thoát",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes){e.Cancel=true;return;}
        }
        _closing=true;_timer.Stop();_orchestrator?.StopAll();
        // Leave the vault key in memory until process termination so cancelled workers can checkpoint safely.
        Storage.SaveSettings(_settings);
    }
}

internal static class InputDialog
{
    public static string? Show(IWin32Window owner,string title,string prompt,bool secret,string initial="")
    {
        using var f=new Form{Text=title,StartPosition=FormStartPosition.CenterParent,Width=520,Height=188,
                             FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false};
        var label=new Label{Text=prompt,Left=15,Top=14,Width=480,Height=37};
        var txt=new TextBox{Left=15,Top=57,Width=475,Text=initial,UseSystemPasswordChar=secret};
        var ok=new Button{Text="Đồng ý",Left=305,Top=99,Width=86,DialogResult=DialogResult.OK};
        var cancel=new Button{Text="Hủy",Left=402,Top=99,Width=86,DialogResult=DialogResult.Cancel};
        f.Controls.AddRange([label,txt,ok,cancel]);f.AcceptButton=ok;f.CancelButton=cancel;
        return f.ShowDialog(owner)==DialogResult.OK?txt.Text:null;
    }
}
