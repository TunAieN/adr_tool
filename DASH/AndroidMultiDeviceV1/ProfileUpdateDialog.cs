using System.Drawing;
using System.Windows.Forms;
using AndroidMultiDeviceV1.Core;

namespace AndroidMultiDeviceV1;

public sealed class ProfileUpdateDialog : Form
{
    readonly ProfileUpdateOptions _options;
    readonly CheckBox enabled=new(){Text="Tự cập nhật sau khi xác minh đúng TikTok username",AutoSize=true};
    readonly CheckBox nameEnabled=new(){Text="Đổi tên hiển thị",AutoSize=true};
    readonly CheckBox bioEnabled=new(){Text="Đổi tiểu sử",AutoSize=true};
    readonly CheckBox photoEnabled=new(){Text="Đổi ảnh đại diện",AutoSize=true};
    readonly CheckBox randomName=new(){Text="Random tên trong danh sách",AutoSize=true};
    readonly CheckBox randomBio=new(){Text="Random tiểu sử trong danh sách",AutoSize=true};
    readonly CheckBox uniquePhoto=new(){Text="Không dùng trùng ảnh trên toàn bộ máy/tài khoản",AutoSize=true};
    readonly CheckBox allowPhotoSaveAndPost=new(){Text="Cho phép tự động Save & post (có thể tạo bài đăng công khai)",AutoSize=true};
    readonly TextBox names=new(){Multiline=true,ScrollBars=ScrollBars.Vertical,Width=430,Height=135};
    readonly TextBox bios=new(){Multiline=true,ScrollBars=ScrollBars.Vertical,Width=430,Height=135};
    readonly TextBox folder=new(){Width=485};
    readonly Label inventory=new(){AutoSize=true,ForeColor=Color.FromArgb(71,85,105)};
    readonly PictureBox preview=new(){SizeMode=PictureBoxSizeMode.Zoom,Width=175,Height=175,BackColor=Color.WhiteSmoke,BorderStyle=BorderStyle.FixedSingle};

    static FlowLayoutPanel Bar() => new(){Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,Margin=new Padding(6)};
    static Label Note(string s)=>new(){Text=s,AutoSize=true,ForeColor=Color.FromArgb(71,85,105),Margin=new Padding(6,5,5,7)};
    public ProfileUpdateDialog()
    {
        _options=ProfileUpdateStore.Read();
        Text="Cấu hình hồ sơ TikTok — tất cả thiết bị";
        MinimumSize=new Size(780,650);Size=new Size(950,730);
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(10),BackColor=Color.White};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,120));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        Controls.Add(root);
        var bar=Bar();bar.Controls.Add(enabled);bar.Controls.Add(Note("Chỉ cập nhật sau khi đăng nhập và xác minh đúng tài khoản; không đổi lại khi đã hoàn tất."));
        root.Controls.Add(bar,0,0);
        var tabs=new TabControl{Dock=DockStyle.Fill};root.Controls.Add(tabs,0,1);
        var namePage=new TabPage("TÊN HIỂN THỊ"){Padding=new Padding(15)};
        var nameLayout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        nameLayout.Controls.Add(nameEnabled);nameLayout.Controls.Add(Note("Mỗi dòng là một tên. Danh sách dùng chung cho tất cả điện thoại."));
        nameLayout.Controls.Add(names);nameLayout.Controls.Add(randomName);nameLayout.Controls.Add(Note("Giá trị đã gán sẽ giữ ổn định cho cùng tài khoản khi tool khởi động lại."));
        namePage.Controls.Add(nameLayout);tabs.TabPages.Add(namePage);
        var bioPage=new TabPage("TIỂU SỬ"){Padding=new Padding(15)};
        var bioLayout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        bioLayout.Controls.Add(bioEnabled);bioLayout.Controls.Add(Note("Mỗi dòng là một bio riêng. TikTok có giới hạn độ dài; bản này kiểm tra tối đa 80 ký tự."));
        bioLayout.Controls.Add(bios);bioLayout.Controls.Add(randomBio);
        bioPage.Controls.Add(bioLayout);tabs.TabPages.Add(bioPage);
        var photoPage=new TabPage("ẢNH ĐẠI DIỆN"){Padding=new Padding(15)};
        var photoLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4};
        photoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,72));photoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));
        photoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        photoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        photoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        photoLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        photoLayout.Controls.Add(photoEnabled,0,0);
        var folderBar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
        folderBar.Controls.Add(folder);
        var choose=new Button{Text="Chọn thư mục ảnh",Width=135,Height=29};
        choose.Click+=(_,_)=>{using var f=new FolderBrowserDialog{Description="Thư mục ảnh nguồn trên máy tính",SelectedPath=Directory.Exists(folder.Text)?folder.Text:""};
            if(f.ShowDialog(this)==DialogResult.OK){folder.Text=f.SelectedPath;RefreshPhotoInventory();}};
        folderBar.Controls.Add(choose);photoLayout.Controls.Add(folderBar,0,1);
        photoLayout.Controls.Add(uniquePhoto,0,2);
        var photoNotes=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        photoNotes.Controls.Add(allowPhotoSaveAndPost);
        photoNotes.Controls.Add(inventory);
        photoNotes.Controls.Add(Note("Ảnh nguồn >= 250×250 px; chuẩn hóa vuông 720×720 JPEG, chất lượng 90%."));
        photoNotes.Controls.Add(Note("Tool so khớp thumbnail qua ảnh chụp màn hình; ảnh không rõ hoặc nhiều ảnh giống nhau sẽ được bỏ qua, không click mù."));
        photoLayout.Controls.Add(photoNotes,0,3);
        photoLayout.Controls.Add(preview,1,1);photoLayout.SetRowSpan(preview,3);
        photoPage.Controls.Add(photoLayout);tabs.TabPages.Add(photoPage);

        var summary=new Label{AutoSize=false,Dock=DockStyle.Fill,ForeColor=Color.FromArgb(71,85,105),
            Text="Thiết lập áp dụng cho TẤT CẢ máy. Mỗi account được lưu tiến độ riêng. Không áp dụng cho các tài khoản chưa xác minh username, bị hạn chế hoặc không được phép quản lý.",
            Padding=new Padding(9,18,5,8)};
        root.Controls.Add(summary,0,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        var cancel=new Button{Text="Hủy",Width=100,Height=32,DialogResult=DialogResult.Cancel};
        var save=new Button{Text="Lưu cấu hình",Width=133,Height=32};
        save.Click+=(_,_)=>SaveChanges();
        actions.Controls.Add(cancel);actions.Controls.Add(save);root.Controls.Add(actions,0,3);
        CancelButton=cancel;
        enabled.Checked=_options.Enabled;nameEnabled.Checked=_options.ChangeName;bioEnabled.Checked=_options.ChangeBio;
        photoEnabled.Checked=_options.ChangePhoto;randomName.Checked=_options.RandomName;
        randomBio.Checked=_options.RandomBio;uniquePhoto.Checked=_options.StrictUniquePhotos;
        allowPhotoSaveAndPost.Checked=_options.AllowPhotoSaveAndPost;
        names.Text=_options.Names;bios.Text=_options.Bios;folder.Text=_options.PhotoFolder;
        RefreshPhotoInventory();
    }
    void RefreshPhotoInventory()
    {
        preview.Image?.Dispose();preview.Image=null;
        if(!Directory.Exists(folder.Text)){inventory.Text="Chưa có thư mục ảnh hợp lệ.";return;}
        var paths=Directory.EnumerateFiles(folder.Text).Where(s=>new[]{".jpg",".jpeg",".png",".bmp"}
            .Contains(Path.GetExtension(s),StringComparer.OrdinalIgnoreCase)).ToArray();
        int good=0,tooSmall=0;
        string? first=null;
        foreach(var path in paths)
        {
            try{using var img=Image.FromFile(path);if(img.Width>=250 && img.Height>=250){good++;first??=path;}else tooSmall++;}catch{tooSmall++;}
        }
        inventory.Text=$"Ảnh hợp lệ: {good}  |  Không hợp lệ/quá nhỏ: {tooSmall}";
        if(first is not null)
        {
            try{using var img=Image.FromFile(first);preview.Image=new Bitmap(img);}catch{}
        }
    }
    void SaveChanges()
    {
        if(enabled.Checked)
        {
            if(nameEnabled.Checked && !names.Lines.Any(s=>!string.IsNullOrWhiteSpace(s))){MessageBox.Show(this,"Chưa nhập tên hiển thị.");return;}
            if(bioEnabled.Checked && !bios.Lines.Any(s=>!string.IsNullOrWhiteSpace(s))){MessageBox.Show(this,"Chưa nhập tiểu sử.");return;}
            if(photoEnabled.Checked && !Directory.Exists(folder.Text)){MessageBox.Show(this,"Chưa chọn thư mục ảnh hợp lệ.");return;}
            if(uniquePhoto.Checked==false && photoEnabled.Checked)
            { MessageBox.Show(this,"V1.6 chỉ hỗ trợ chống trùng ảnh nghiêm ngặt. Hãy bật Tránh trùng ảnh.");return; }
        }
        _options.Enabled=enabled.Checked;_options.ChangeName=nameEnabled.Checked;_options.ChangeBio=bioEnabled.Checked;
        _options.ChangePhoto=photoEnabled.Checked;_options.RandomName=randomName.Checked;_options.RandomBio=randomBio.Checked;
        _options.StrictUniquePhotos=uniquePhoto.Checked;_options.AllowPhotoSaveAndPost=allowPhotoSaveAndPost.Checked;
        _options.Names=names.Text;_options.Bios=bios.Text;
        _options.PhotoFolder=folder.Text.Trim();
        ProfileUpdateStore.Save(_options);DialogResult=DialogResult.OK;Close();
    }
}
