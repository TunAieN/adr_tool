# Android Multi-Device Manager — Bản làm lại giao diện (UI FIRST)

**Nguồn:** bản đầy đủ `ANDROID_MULTI_DEVICE_V1_GALLERY_PERMISSION_V1626_FULL.zip` (V1.6.2.6).

**Phạm vi bản này:** chỉ làm lại màn hình `MainForm.cs`. Không thêm LIVE Search, không gửi bình luận và không sửa logic quản lý tài khoản, Appium, Google Login, cập nhật hồ sơ hoặc truy cập Gallery.

## Bố cục mới

- **Thanh trên:** ADB exe, Appium, Remote ADB, Kết nối, Refresh, Lưu, Mở kho.
- **Thanh trái:** Thiết bị, Tài khoản, Cấu hình, Nhật ký. LIVE (sắp làm) được vô hiệu hóa, không giả vờ tính năng đã hoạt động.
- **Bảng thiết bị:** hiển thị một đến nhiều máy, cuộn khi số máy tăng; chọn một dòng để mở các hành động trong khung phải.
- **Khung thao tác máy:** Start, Stop, Chạy TK tick, Kiểm tra UI, Phút/máy, Start/Stop tất cả. Menu "Thao tác khác": Refresh ADB, Đổi cổng ADB, Kết nối Remote.
- **Bảng tài khoản:** vẫn có checkbox từng dòng, ô chọn tất cả; nhập TXT/Excel, gán, gỡ, xóa. Menu "Thêm": đặt lại, TikTok handle, cấu hình hồ sơ, selectors.
- **Nhật ký:** nằm gọn phía dưới; nút "Xóa hiển thị" chỉ xóa nội dung ô log, KHÔNG xóa file log đã lưu.
- **Cấu hình hệ thống:** có thể chỉnh số thiết bị chạy song song trong menu trái.

## Cách cài bản vá

1. Đóng Android Multi-Device Manager.
2. Sao lưu toàn bộ thư mục dự án **và** `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
3. Giải nén bản vá và chép `AndroidMultiDeviceV1\MainForm.cs` vào mã nguồn V1.6.2.6, ghi đè file cũ.
4. Chạy `BUILD_RUN.bat` trong thư mục dự án.

## Cách dùng bản FULL

1. Giải nén toàn bộ ZIP vào thư mục mới (không giải nén đè thư mục tool đang chạy).
2. Chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK.
3. Mở kho và kết nối thiết bị từ thanh trên.

## Kiểm thử giao diện trên Windows

- [ ] Build thành công, mở cửa sổ và hiển thị đầy đủ các khu vực.
- [ ] 1 điện thoại: chọn thiết bị, khung phải hiện đúng Phone/ADB/port/trạng thái.
- [ ] Nhiều điện thoại: bảng cuộn được; đổi máy không làm đổi tài khoản không chủ ý.
- [ ] Start/Stop một máy và Start/Stop tất cả đúng như phiên bản cũ.
- [ ] Chọn 1 tài khoản bằng checkbox rồi Chạy TK tick; không tự lấy nhầm account từ queue.
- [ ] Checkbox không mất sau lần tự Refresh 5 giây.
- [ ] Nhập TXT/Excel, gán, gỡ, xóa, đặt lại và cấu hình hồ sơ có thể truy cập.
- [ ] Điều chỉnh "Song song" từ Cấu hình, "Phút/máy" ở khung phải, lưu và khởi động lại.
- [ ] Thử thu nhỏ cửa sổ; vùng bảng có thanh cuộn thay vì chồng nút.
- [ ] Không đụng đến `account-vault.json`, `profile-update-records.json`.

**Hạn chế:** Tại môi trường tạo bản vá này không có .NET 8 Windows toolchain và thiết bị XCloudPhone, nên chưa thể chạy GUI và kiểm thử thực tế. Nếu phát sinh lỗi biên dịch, gửi nguyên văn lỗi .NET.
