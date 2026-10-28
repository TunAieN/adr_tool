using System.Drawing;
using System.Windows.Forms;
using AndroidMultiDeviceV1.Core;

namespace AndroidMultiDeviceV1;

/// <summary>
/// Stage 1: compact, persistent LIVE configuration only. No Appium action here.
/// </summary>
public sealed class LiveSettingsDialog : Form
{
    readonly CheckBox _searchEnabled = new() { Text = "Bật tìm LIVE bằng TikTok Search", AutoSize = true };
    readonly ComboBox _sourceMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310 };
    readonly TextBox _chromeProfile = new() { Width = 310, ReadOnly = true };
    readonly NumericUpDown _chromeTimeout = Num(5, 120);
    readonly TextBox _keywords = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    readonly ComboBox _keywordMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    readonly NumericUpDown _perKeyword = Num(5, 3600);
    readonly NumericUpDown _totalSearch = Num(5, 7200);
    readonly NumericUpDown _cards = Num(1, 50);
    readonly NumericUpDown _minimumViewers = Num(0, 100000000);
    readonly NumericUpDown _maximumViewers = Num(0, 100000000);
    readonly NumericUpDown _searchScrolls = Num(1, 200);
    readonly NumericUpDown _searchSwipe = Num(1, 75);
    readonly NumericUpDown _afterSwipe = Num(1200, 20000);
    readonly NumericUpDown _perCard = Num(0, 5000);

    readonly NumericUpDown _sessionSeconds = Num(10, 36000);
    readonly NumericUpDown _swipeChecks = Num(1, 200);
    readonly NumericUpDown _revisitMinutes = Num(0, 10080);
    readonly NumericUpDown _roomDeviceLimit = Num(1, 100);
    readonly NumericUpDown _uiTimeout = Num(5, 600);
    readonly NumericUpDown _uiMinimumWait = Num(0, 120);
    readonly NumericUpDown _extraMin = Num(0, 120);
    readonly NumericUpDown _extraMax = Num(0, 120);
    readonly NumericUpDown _stableReads = Num(1, 20);
    readonly NumericUpDown _uiPoll = Num(200, 10000);
    readonly NumericUpDown _actionMin = Num(0, 60000);
    readonly NumericUpDown _actionMax = Num(0, 60000);
    readonly NumericUpDown _roundMin = Num(0, 600000);
    readonly NumericUpDown _roundMax = Num(0, 600000);

    readonly TextBox _drafts = new() { Multiline = true, AcceptsReturn = true, WordWrap = false, ScrollBars = ScrollBars.Both, ShortcutsEnabled = true };
    readonly ComboBox _draftMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _draftCount = new() { AutoSize = true, ForeColor = Color.FromArgb(89, 101, 118) };

    readonly Button _save = new() { Text = "Lưu cấu hình", Width = 134, Height = 34, FlatStyle = FlatStyle.Flat };
    readonly Button _cancel = new() { Text = "Hủy", Width = 82, Height = 34 };
    readonly Button _defaults = new() { Text = "Mặc định", Width = 88, Height = 34 };

    static NumericUpDown Num(decimal min, decimal max) => new()
    {
        Minimum = min, Maximum = max, ThousandsSeparator = true, Width = 180,
        TextAlign = HorizontalAlignment.Right, DecimalPlaces = 0, Margin = new Padding(4, 6, 8, 5)
    };

    public LiveSettingsDialog()
    {
        Text = "Cấu hình LIVE Manager — Giai đoạn 2";
        Font = new Font("Segoe UI", 9.4f);
        Size = new Size(820, 650);
        MinimumSize = new Size(690, 540);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(249, 250, 252);
        ShowInTaskbar = false;
        MaximizeBox = true;

        var title = new Label
        {
            Text = "CẤU HÌNH LIVE", Dock = DockStyle.Top, Height = 52,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 0, 0),
            Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold),
            BackColor = Color.White, ForeColor = Color.FromArgb(30, 43, 61)
        };
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 55, ColumnCount = 2,
            Padding = new Padding(14, 7, 12, 6), BackColor = Color.White
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var scope = new Label
        {
            Text = "Tìm nguồn LIVE ở mục TÌM LIVE. Chưa vào phòng hoặc gửi bình luận.",
            ForeColor = Color.FromArgb(89, 101, 118), AutoSize = true,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        };
        footer.Controls.Add(scope, 0, 0);
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill,
            AutoSize = true, WrapContents = false, Margin = Padding.Empty
        };
        _save.BackColor = Color.FromArgb(30, 112, 215); _save.ForeColor = Color.White;
        _save.FlatAppearance.BorderColor = Color.FromArgb(30, 112, 215);
        _save.Font = new Font("Segoe UI Semibold", 9.3f, FontStyle.Bold);
        _save.Click += (_, _) => SaveAndClose();
        _cancel.DialogResult = DialogResult.Cancel;
        _defaults.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Đưa các ô cấu hình về mặc định? Chỉ thay đổi trên màn hình đến khi bấm Lưu.",
                "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Apply(new LiveManagerSettings());
        };
        buttons.Controls.Add(_save); buttons.Controls.Add(_cancel); buttons.Controls.Add(_defaults);
        footer.Controls.Add(buttons, 1, 0);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill, Margin = new Padding(11, 9, 11, 7),
            Padding = new Point(12, 7)
        };
        tabs.TabPages.Add(BuildSearchTab());
        tabs.TabPages.Add(BuildSessionTab());
        tabs.TabPages.Add(BuildDraftTab());

        Controls.Add(tabs);
        Controls.Add(footer);
        Controls.Add(title);
        AcceptButton = _save;
        CancelButton = _cancel;
        _keywordMode.Items.Add("LRU — ưu tiên từ khóa lâu chưa dùng");
        _sourceMode.Items.AddRange([
            "TikTok Search trên điện thoại (Appium)",
            "TikTok Web — Chrome profile CDP riêng"
        ]);
        _chromeProfile.Text = TikTokChromeCdpBrowser.ProfileDirectory;
        _sourceMode.SelectedIndexChanged += (_, _) => UpdateSourceControls();
        _draftMode.Items.AddRange(["Trộn, ưu tiên câu chưa xem", "Ngẫu nhiên", "Theo thứ tự"]);
        _drafts.TextChanged += (_, _) => UpdateDraftCount();
        Apply(LiveManagerSettingsStore.Load());
    }

    static Panel FormPanel(TabPage tab, out TableLayoutPanel grid)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = 0, Height = 0,
            AutoSize = false, Padding = new Padding(15, 8, 20, 16),
            Margin = Padding.Empty, BackColor = Color.White
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(grid); tab.Controls.Add(scroll);
        return scroll;
    }
    static void Add(TableLayoutPanel g, Control left, Control right, int height)
    {
        var row = g.RowCount++;
        g.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        g.Height += height;
        left.Dock = DockStyle.Fill;
        left.Margin = new Padding(5, 5, 4, 3);
        right.Margin = new Padding(4, 5, 8, 3);
        if (right is TextBox) right.Dock = DockStyle.Fill;
        else if (right is CheckBox) right.Anchor = AnchorStyles.Left;
        else right.Anchor = AnchorStyles.Left;
        g.Controls.Add(left, 0, row); g.Controls.Add(right, 1, row);
    }
    static void Field(TableLayoutPanel g, string label, Control control, int height = 40)
    {
        Add(g, new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(50, 65, 82) }, control, height);
    }
    static void Section(TableLayoutPanel g, string heading)
    {
        var row = g.RowCount++;
        g.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); g.Height += 37;
        var label = new Label
        {
            Text = heading, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(29, 71, 125), Padding = new Padding(3, 0, 0, 0)
        };
        g.Controls.Add(label, 0, row); g.SetColumnSpan(label, 2);
    }
    static void Note(TableLayoutPanel g, string note, int height = 41)
    {
        var row = g.RowCount++;
        g.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); g.Height += height;
        var label = new Label
        {
            Text = note, Dock = DockStyle.Fill, AutoSize = false,
            ForeColor = Color.FromArgb(100, 116, 139), Padding = new Padding(7, 1, 12, 0)
        };
        g.Controls.Add(label, 0, row); g.SetColumnSpan(label, 2);
    }

    TabPage BuildSearchTab()
    {
        var tab = new TabPage("Nguồn LIVE") { BackColor = Color.White };
        FormPanel(tab, out var g);
        Section(g, "Tìm LIVE theo từ khóa");
        Field(g, "Bật tìm", _searchEnabled);
        Field(g, "Nguồn tìm", _sourceMode);
        Field(g, "Profile Chrome CDP riêng", _chromeProfile);
        Field(g, "Timeout tải TikTok Web (giây)", _chromeTimeout);
        Note(g, "Chrome CDP riêng trên localhost, không điều khiển Chrome cá nhân. Viewer đọc từ SVG người, loại SVG like. Android tìm theo username rồi xác minh lại username và Viewer thực tế.", 88);
        Field(g, "Cách chọn từ khóa", _keywordMode);
        Field(g, "Từ khóa (mỗi dòng một từ)", _keywords, 98);
        Field(g, "Timeout mỗi từ khóa (giây)", _perKeyword);
        Field(g, "Timeout toàn đợt tìm (giây)", _totalSearch);
        Field(g, "Số card đánh giá mỗi lượt", _cards);
        Field(g, "Số lượt cuộn Search tối đa", _searchScrolls);
        Field(g, "Độ dài kéo lên (1–75% màn hình)", _searchSwipe);
        Field(g, "Chờ sau mỗi lần vuốt (ms)", _afterSwipe);
        Field(g, "Chờ đọc từng card (ms)", _perCard);
        Note(g, "Gợi ý 30%: kéo khoảng 187px trên màn hình 624px, ngắn hơn một hàng card (~339px). Chờ tải UI và đọc card MỚI trước khi kéo tiếp.", 52);
        Section(g, "Bộ lọc phòng LIVE");
        Field(g, "Người xem tối thiểu", _minimumViewers);
        Field(g, "Người xem tối đa (0 = không giới hạn)", _maximumViewers);
        Note(g, "Số người xem cần đọc từ nhãn Viewer của LIVE, không dùng số lượt thích. Bộ tìm kiếm chỉ đánh dấu đủ điều kiện khi đã xác minh nhãn Viewer.", 50);
        return tab;
    }
    TabPage BuildSessionTab()
    {
        var tab = new TabPage("Phiên & thiết bị") { BackColor = Color.White };
        FormPanel(tab, out var g);
        Section(g, "Điều hướng và thời lượng");
        Field(g, "Thời lượng ở một LIVE (giây)", _sessionSeconds);
        Field(g, "Kiểm tra LIVE kế tiếp tối đa (lượt)", _swipeChecks);
        Field(g, "Cooldown phòng đã ghé (phút)", _revisitMinutes);
        Field(g, "Thiết bị tối đa cùng phòng", _roomDeviceLimit);
        Section(g, "Chờ giao diện LIVE ổn định");
        Field(g, "Thời gian chờ UI tối đa (giây)", _uiTimeout);
        Field(g, "Chờ tối thiểu sau khi vào LIVE (giây)", _uiMinimumWait);
        Field(g, "Chờ bổ sung tối thiểu (giây)", _extraMin);
        Field(g, "Chờ bổ sung tối đa (giây)", _extraMax);
        Field(g, "Số lần UI ổn định liên tiếp", _stableReads);
        Field(g, "Chu kỳ kiểm tra UI (mili giây)", _uiPoll);
        Section(g, "Thời gian giữa tác vụ điều khiển");
        Field(g, "Chờ thao tác tối thiểu (ms)", _actionMin);
        Field(g, "Chờ thao tác tối đa (ms)", _actionMax);
        Field(g, "Nghỉ giữa vòng tối thiểu (ms)", _roundMin);
        Field(g, "Nghỉ giữa vòng tối đa (ms)", _roundMax);
        Note(g, "Thời gian là tham số cấu hình; khi lập trình LIVE, tool vẫn phải kiểm tra UI thật trước khi thao tác.", 53);
        return tab;
    }
    TabPage BuildDraftTab()
    {
        var tab = new TabPage("Thư viện nội dung") { BackColor = Color.White };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(18, 14, 18, 10), BackColor = Color.White
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.Controls.Add(new Label
        {
            Text = "Nhập hoặc dán nhiều dòng (Ctrl+V); nhấn Enter để xuống dòng. Mỗi dòng = 1 nội dung nháp, chưa tự gửi.",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(71, 85, 105)
        }, 0, 0);
        _drafts.Dock = DockStyle.Fill;
        _drafts.Font = new Font("Segoe UI", 9.5f);
        root.Controls.Add(_drafts, 0, 1);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.Add(new Label
        {
            Text = "Cách gợi ý:", AutoSize = true, Margin = new Padding(1, 10, 12, 3)
        });
        _draftMode.Width = 215; _draftMode.Margin = new Padding(1, 6, 16, 4);
        options.Controls.Add(_draftMode);
        _draftCount.Margin = new Padding(4, 11, 0, 0);
        options.Controls.Add(_draftCount);
        root.Controls.Add(options, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var bulkAdd = new Button { Text = "Thêm nhiều dòng", Width = 150, Height = 32 };
        var import = new Button { Text = "Nhập TXT", Width = 104, Height = 32 };
        var export = new Button { Text = "Xuất TXT", Width = 104, Height = 32 };
        bulkAdd.Click += (_, _) => AddDraftsInBulk();
        import.Click += (_, _) => ImportDrafts();
        export.Click += (_, _) => ExportDrafts();
        actions.Controls.Add(bulkAdd); actions.Controls.Add(import); actions.Controls.Add(export);
        root.Controls.Add(actions, 0, 3);
        tab.Controls.Add(root);
        return tab;
    }
    void UpdateDraftCount()
    {
        int count = Lines(_drafts.Text).Count;
        _draftCount.Text = count.ToString("N0") + " dòng nội dung";
    }
    static List<string> Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(s => s.Length > 0).ToList();

    void Apply(LiveManagerSettings s)
    {
        _searchEnabled.Checked = s.KeywordSearchEnabled;
        _sourceMode.SelectedIndex = s.LiveSourceMode == "ChromeTikTokWeb" ? 1 : 0;
        _chromeProfile.Text = TikTokChromeCdpBrowser.ProfileDirectory;
        _chromeTimeout.Value = Math.Clamp(s.ChromePageLoadTimeoutSeconds, 5, 120);
        _keywordMode.SelectedIndex = 0;
        _keywords.Text = string.Join(Environment.NewLine, s.Keywords);
        _perKeyword.Value = s.PerKeywordTimeoutSeconds;
        _totalSearch.Value = s.TotalSearchTimeoutSeconds;
        _cards.Value = s.CardsPerBatch;
        _minimumViewers.Value = s.MinimumViewers;
        _maximumViewers.Value = s.MaximumViewers;
        _searchScrolls.Value = s.MaxSearchScrolls;
        _searchSwipe.Value = s.SearchSwipePercent;
        _afterSwipe.Value = s.SearchAfterSwipeWaitMilliseconds;
        _perCard.Value = s.SearchPerCardWaitMilliseconds;
        _sessionSeconds.Value = s.SessionDurationSeconds;
        _swipeChecks.Value = s.MaxLiveSwipeChecks;
        _revisitMinutes.Value = s.RoomCooldownMinutes;
        _roomDeviceLimit.Value = s.MaxDevicesPerRoom;
        _uiTimeout.Value = s.UiReadyTimeoutSeconds;
        _uiMinimumWait.Value = s.MinimumReadyWaitSeconds;
        _extraMin.Value = s.ExtraWaitMinSeconds;
        _extraMax.Value = s.ExtraWaitMaxSeconds;
        _stableReads.Value = s.StableUiReadings;
        _uiPoll.Value = s.UiPollMilliseconds;
        _actionMin.Value = s.ActionWaitMinMilliseconds;
        _actionMax.Value = s.ActionWaitMaxMilliseconds;
        _roundMin.Value = s.RoundRestMinMilliseconds;
        _roundMax.Value = s.RoundRestMaxMilliseconds;
        _drafts.Text = string.Join(Environment.NewLine, s.CommentDrafts);
        _draftMode.SelectedIndex = s.DraftSuggestionMode switch
        {
            "Sequential" => 2,
            "Random" => 1,
            _ => 0
        };
        UpdateDraftCount();
        UpdateSourceControls();
    }
    void UpdateSourceControls()
    {
        bool chrome = _sourceMode.SelectedIndex == 1;
        _chromeProfile.Enabled = chrome;
        _chromeTimeout.Enabled = chrome;
    }
    LiveManagerSettings CaptureSettings() => new()
    {
        KeywordSearchEnabled = _searchEnabled.Checked,
        LiveSourceMode = _sourceMode.SelectedIndex == 1 ? "ChromeTikTokWeb" : "DeviceTikTokSearch",
        ChromePageLoadTimeoutSeconds = (int)_chromeTimeout.Value,
        KeywordSelectionMode = "LRU",
        Keywords = Lines(_keywords.Text).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        PerKeywordTimeoutSeconds = (int)_perKeyword.Value,
        TotalSearchTimeoutSeconds = (int)_totalSearch.Value,
        CardsPerBatch = (int)_cards.Value,
        MinimumViewers = (int)_minimumViewers.Value,
        MaximumViewers = (int)_maximumViewers.Value,
        MaxSearchScrolls = (int)_searchScrolls.Value,
        SearchSwipePercent = (int)_searchSwipe.Value,
        SearchAfterSwipeWaitMilliseconds = (int)_afterSwipe.Value,
        SearchPerCardWaitMilliseconds = (int)_perCard.Value,
        SessionDurationSeconds = (int)_sessionSeconds.Value,
        MaxLiveSwipeChecks = (int)_swipeChecks.Value,
        RoomCooldownMinutes = (int)_revisitMinutes.Value,
        MaxDevicesPerRoom = (int)_roomDeviceLimit.Value,
        UiReadyTimeoutSeconds = (int)_uiTimeout.Value,
        MinimumReadyWaitSeconds = (int)_uiMinimumWait.Value,
        ExtraWaitMinSeconds = (int)_extraMin.Value,
        ExtraWaitMaxSeconds = (int)_extraMax.Value,
        StableUiReadings = (int)_stableReads.Value,
        UiPollMilliseconds = (int)_uiPoll.Value,
        ActionWaitMinMilliseconds = (int)_actionMin.Value,
        ActionWaitMaxMilliseconds = (int)_actionMax.Value,
        RoundRestMinMilliseconds = (int)_roundMin.Value,
        RoundRestMaxMilliseconds = (int)_roundMax.Value,
        CommentDrafts = Lines(_drafts.Text),
        DraftSuggestionMode = _draftMode.SelectedIndex switch { 1 => "Random", 2 => "Sequential", _ => "Shuffle" }
    };
    void SaveAndClose()
    {
        try
        {
            var s = CaptureSettings();
            var errors = s.Validate();
            if (errors.Count != 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Kiểm tra cấu hình LIVE",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LiveManagerSettingsStore.Save(s);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không lưu được cấu hình LIVE", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    void AddDraftsInBulk()
    {
        using var dialog = new Form
        {
            Text = "Thêm nhiều dòng nội dung",
            Size = new Size(680, 470),
            MinimumSize = new Size(500, 350),
            Font = Font,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.White,
            ShowInTaskbar = false,
            MaximizeBox = true
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label
        {
            Text = "Dán hoặc gõ nhiều câu, mỗi câu một dòng. Nhấn Enter để xuống dòng; " +
                   "các câu mới được thêm vào danh sách hiện có.",
            Dock = DockStyle.Fill, AutoSize = false
        }, 0, 0);
        var input = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Both, WordWrap = false,
            ShortcutsEnabled = true, Font = _drafts.Font
        };
        layout.Controls.Add(input, 0, 1);
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var add = new Button { Text = "Thêm vào danh sách", Width = 160, Height = 32 };
        var cancel = new Button { Text = "Hủy", Width = 80, Height = 32, DialogResult = DialogResult.Cancel };
        var count = new Label { AutoSize = true, Margin = new Padding(10, 11, 12, 3) };
        input.TextChanged += (_, _) => count.Text = $"{Lines(input.Text).Count:N0} dòng";
        count.Text = "0 dòng";
        add.Click += (_, _) =>
        {
            var incoming = Lines(input.Text);
            var current = Lines(_drafts.Text);
            if (incoming.Count == 0)
            {
                MessageBox.Show(dialog, "Hãy nhập ít nhất một dòng nội dung.", "Chưa có nội dung",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (current.Count + incoming.Count > 10000 || incoming.Any(line => line.Length > 500))
            {
                MessageBox.Show(dialog, "Tổng thư viện tối đa 10.000 dòng, mỗi dòng tối đa 500 ký tự.",
                    "Quá giới hạn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            current.AddRange(incoming);
            _drafts.Text = string.Join(Environment.NewLine, current);
            _drafts.SelectionStart = _drafts.TextLength;
            _drafts.ScrollToCaret();
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        footer.Controls.Add(add);
        footer.Controls.Add(cancel);
        footer.Controls.Add(count);
        layout.Controls.Add(footer, 0, 2);
        dialog.Controls.Add(layout);
        dialog.CancelButton = cancel;
        // No default AcceptButton here: Enter must create a new line in the TextBox.
        dialog.ShowDialog(this);
    }

    void ImportDrafts()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Chọn file nội dung nháp", Filter = "Text UTF-8 (*.txt)|*.txt|Tất cả file (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var lines = Lines(File.ReadAllText(dlg.FileName));
            _drafts.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không đọc được file", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    void ExportDrafts()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Lưu nội dung nháp", Filter = "Text UTF-8 (*.txt)|*.txt", FileName = "noi-dung-live.txt"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dlg.FileName, string.Join(Environment.NewLine, Lines(_drafts.Text)), new System.Text.UTF8Encoding(false)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không xuất được file", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
