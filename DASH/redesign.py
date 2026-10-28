from pathlib import Path
p=Path('/mnt/data/ui_redesign_v200/AndroidMultiDeviceV1/MainForm.cs')
s=p.read_text()
s=s.replace('    readonly Button _unlock=new(){Text="Mở kho tài khoản",Width=133,Height=30};','''    readonly Button _unlock=new(){Text="Mở kho",Width=96,Height=34};
    readonly Label _selectedPhoneTitle=new(){Text="Chưa chọn thiết bị",Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",11,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
    readonly Label _selectedPhoneDetail=new(){Text="Chọn một dòng ở danh sách thiết bị.",Dock=DockStyle.Fill,Font=new Font("Segoe UI",9),ForeColor=Color.FromArgb(71,85,105)};
''')
s=s.replace('''            BorderStyle=BorderStyle.FixedSingle,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToResizeRows=false,ColumnHeadersHeight=32,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing
''','''            BorderStyle=BorderStyle.FixedSingle,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToResizeRows=false,ColumnHeadersHeight=34,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles=false,GridColor=Color.FromArgb(225,231,238),
            ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {BackColor=Color.FromArgb(246,248,251),ForeColor=Color.FromArgb(31,41,55),Font=new Font("Segoe UI Semibold",9,FontStyle.Bold)},
            DefaultCellStyle=new DataGridViewCellStyle {SelectionBackColor=Color.FromArgb(220,236,252),SelectionForeColor=Color.FromArgb(15,23,42)},
            RowTemplate=new DataGridViewRow {Height=32}
''')
# Ensure property RowTemplate not in object initializers? DataGridView.RowTemplate property is settable yes.
s=s.replace('''        var b=new Button {Text=label,Width=width,Height=31,Margin=new Padding(2,3,3,3),FlatStyle=FlatStyle.System};
''','''        var b=new Button {Text=label,Width=width,Height=32,Margin=new Padding(3),FlatStyle=FlatStyle.Flat,
            BackColor=Color.White,ForeColor=Color.FromArgb(38,50,66)};
        b.FlatAppearance.BorderColor=Color.FromArgb(204,214,226);
''')
start=s.index('    void BuildLayout()\n    {')
end=s.index('    void ConfigureProfiles()',start)
new=r'''    // V2 interface: device list scales vertically, commands belong to the selected device,
    // and low-frequency controls are moved into menus. No change to business workflows.
    void BuildLayout()
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
        AddNav("LIVE (sắp làm)",4,()=>{},enabled:false);
        work.Controls.Add(sidebar,0,0);

        var center=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,
            Padding=new Padding(6,10,10,9),Margin=Padding.Empty,BackColor=Color.FromArgb(247,249,252)};
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        center.RowStyles.Add(new RowStyle(SizeType.Percent,52));
        center.RowStyles.Add(new RowStyle(SizeType.Percent,48));
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

        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="Thiết bị",FillWeight=80});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Serial",HeaderText="Remote ADB / UDID",FillWeight=165});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Connection",HeaderText="ADB",FillWeight=64});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Port",HeaderText="Port",FillWeight=55});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Stage",HeaderText="Trạng thái",FillWeight=112});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Account",HeaderText="Tài khoản",FillWeight=130});
        _deviceGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Detail",HeaderText="Tác vụ hiện tại",FillWeight=170});
        _deviceGrid.Margin=new Padding(8,0,4,7);
        _deviceGrid.SelectionChanged+=(_,_)=>UpdateSelectedDeviceInfo();
        _deviceGrid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0) _=SafeAsync(CheckTikTokAsync);};
        devices.Controls.Add(_deviceGrid,0,1);
        devicePanel.Controls.Add(devices,0,0);

        var quick=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,
            Margin=new Padding(4,0,0,0),Padding=new Padding(10,3,9,5),
            BackColor=Color.FromArgb(250,251,253)};
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
        var start=Btn("Start máy",98,async(_,_)=>await SafeAsync(StartSelectedAsync));MakePrimary(start);start.Dock=DockStyle.Fill;
        var stop=Btn("Stop máy",98,(_,_)=>StopSelected());stop.Dock=DockStyle.Fill;
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
        deviceMenu.Items.Add("Đổi cổng ADB",null,async(_,_)=>await SafeAsync(ChangeDeviceEndpointAsync));
        deviceMenu.Items.Add("Kết nối Remote",null,async(_,_)=>await SafeAsync(ConnectRemoteAsync));
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
        accountBar.Controls.Add(Btn("Gán máy",84,(_,_)=>AssignAccounts()));
        accountBar.Controls.Add(Btn("Gỡ máy",82,(_,_)=>UnassignAccounts()));
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
        foreach(DataGridViewColumn col in _accountGrid.Columns)if(col.Name!="Checked")col.ReadOnly=true;
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
        var logbar=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        logbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        logbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,126));
        logbar.Controls.Add(SectionTitle("Nhật ký hoạt động"),0,0);
        logbar.Controls.Add(Btn("Xóa hiển thị",114,(_,_)=>_log.Clear()),1,0);
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
            _selectedPhoneTitle.Text="Chưa chọn thiết bị";
            _selectedPhoneDetail.Text="Chọn một điện thoại từ bảng.";
            return;
        }
        _selectedPhoneTitle.Text=d.Name+"   ·   "+d.Connection;
        _selectedPhoneDetail.Text=$"Port {d.SystemPort}  |  {d.Stage}\n"+
            (string.IsNullOrWhiteSpace(d.AccountEmail)?"Chưa có tài khoản":d.AccountEmail);
        // Do not overwrite an actively edited session duration on every timer refresh.
        if(!_minutes.Focused)_minutes.Value=Math.Clamp(d.SessionMinutes,1,1440);
    }

    void ConfigureSystem()
    {
        using var dlg=new Form {Text="Cấu hình hệ thống",StartPosition=FormStartPosition.CenterParent,
            Width=420,Height=225,FormBorderStyle=FormBorderStyle.FixedDialog,
            MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false};
        var label=new Label {Text="Số điện thoại chạy song song tối đa:",Left=18,Top=25,Width=258,Height=26};
        var numeric=new NumericUpDown {Left=290,Top=23,Width=80,Minimum=1,Maximum=100,
            Value=_parallel.Value};
        var note=new Label {Text="Thiết lập này có hiệu lực sau khi mở lại kho hoặc khởi động chương trình.",
            Left=18,Top=67,Width=357,Height=43,ForeColor=Color.FromArgb(71,85,105)};
        var more=new Button {Text="Sửa selectors",Left=18,Top=128,Width=128};
        more.Click+=(_,_)=>OpenSelectors();
        var ok=new Button {Text="Lưu",Left=210,Top=128,Width=76,DialogResult=DialogResult.OK};
        var cancel=new Button {Text="Hủy",Left=295,Top=128,Width=76,DialogResult=DialogResult.Cancel};
        dlg.Controls.AddRange([label,numeric,note,more,ok,cancel]);
        dlg.AcceptButton=ok;dlg.CancelButton=cancel;
        if(dlg.ShowDialog(this)!=DialogResult.OK)return;
        _parallel.Value=numeric.Value;SaveSettings();
    }

'''
s=s[:start]+new+s[end:]
s=s.replace('''        int onlineCount=_settings.Devices.Count(x=>x.Connection=="ONLINE");
        _deviceStatus.Text=$"Đã nhận: {_settings.Devices.Count}  |  Online: {onlineCount}";
        _deviceStatus.ForeColor=onlineCount>0?Color.FromArgb(21,128,61):Color.FromArgb(180,83,9);
''','''        int onlineCount=_settings.Devices.Count(x=>x.Connection=="ONLINE");
        _deviceStatus.Text=$"{_settings.Devices.Count} máy  ·  Online {onlineCount}";
        _deviceStatus.ForeColor=onlineCount>0?Color.FromArgb(21,128,61):Color.FromArgb(180,83,9);
        UpdateSelectedDeviceInfo();
''')
p.write_text(s)
print('new mainform lines',len(s.splitlines()))
