# ANDROID MULTI-DEVICE MANAGER — LIVE MANAGER / GIAI ĐOẠN 1

Bản phát triển từ **ANDROID_MULTI_DEVICE_UI_GON_V200_FULL** (giao diện gọn V2.0, logic đăng nhập và cập nhật hồ sơ V1.6.2.6).

## Những gì đã được bổ sung

- Menu trái **CẤU HÌNH LIVE** (có thể mở ngay cả khi chưa mở kho tài khoản).
- Cửa sổ WinForms gọn gồm 3 tab:
  1. **Nguồn LIVE**: bật/tắt Search, danh sách từ khóa, thứ tự/ngẫu nhiên, timeout, số card, lượt cuộn, bộ lọc Viewer.
  2. **Phiên & thiết bị**: thời lượng phiên, số lần vuốt thử LIVE khác, thời gian quay lại, số máy/phòng, điều kiện UI ổn định, thời gian giữa thao tác điều khiển và nghỉ vòng.
  3. **Thư viện nội dung**: nội dung NHÁP theo dòng, gợi ý thứ tự/ngẫu nhiên/trộn, nhập/xuất UTF-8 TXT. KHÔNG tự gửi bình luận.
- Kiểm tra cấu hình trước khi Lưu; nút Mặc định chỉ reset các ô trên cửa sổ, không ghi dữ liệu nếu chưa bấm Lưu.
- Lưu bằng ghi file nguyên tử riêng vào `%LOCALAPPDATA%\AndroidMultiDeviceV1\live-manager-settings.json`.
- Nếu file JSON đã tồn tại nhưng bị hỏng, hiển thị thông báo thay vì tự động ghi đè.

## Những gì CHƯA được triển khai

- Tìm LIVE thật trên TikTok, quét card, xếp hạng số người xem, chuyển phòng.
- Kết nối trạng thái live thực tế với worker Appium/ADB.
- Tự động gửi, lên lịch gửi hoặc phát tán bình luận. Thư viện là dữ liệu nháp để xem xét.
- Nhập Excel vào thư viện nội dung (chỉ hỗ trợ TXT ở GĐ1). Nhập Excel tài khoản cũ vẫn giữ nguyên.

## Cài đặt

1. Đóng Android Multi-Device Manager; sao lưu thư mục mã nguồn và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Dùng **FULL.zip** và giải nén vào thư mục MỚI để kiểm tra, hoặc giải nén **PATCH.zip** đè lên đúng bản giao diện gọn V2.0 (không vá lên bản V1.6.2.6 với MainForm cũ).
3. Chạy `BUILD_RUN.bat` trên máy Windows có .NET 8 SDK.
4. Mở menu **CẤU HÌNH LIVE**, thay thử một từ khóa và số người xem, bấm **Lưu cấu hình**.
5. Đóng và mở lại tool, vào **CẤU HÌNH LIVE** để kiểm tra giá trị được khôi phục.
6. Thử nhập nội dung TXT từ thư viện nội dung; không có thao tác nào gửi tin lên TikTok.

## Kiểm thử cần đạt

- Có thể mở cửa sổ cấu hình dù chưa nhập mật khẩu kho.
- Các trường mặc định hiển thị chính xác: 65s/từ, 130s/toàn đợt, 8 card, 120s/Live, 30 phút xem xét lại, 2 thiết bị/phòng, chờ UI tối thiểu 5s.
- Cấu hình hợp lệ lưu và mở lại được qua restart.
- Nhập timeout toàn đợt nhỏ hơn timeout từ khóa phải báo lỗi và không lưu.
- Nhập ngưỡng Viewer tối đa nhỏ hơn tối thiểu phải báo lỗi.
- Bấm Hủy hoặc đóng cửa sổ không được thay đổi file.
- Menu đăng nhập, danh sách thiết bị, checkbox kho và lệnh Start/Stop vẫn hoạt động như phiên bản giao diện gọn trước đó.
- Không có thay đổi đến `account-vault.json`, `settings.json` hoặc `selectors.json`.

## Các file thay đổi

- **MỚI**: `AndroidMultiDeviceV1/Core/LiveManagerSettings.cs` (model, validate, lưu JSON).
- **MỚI**: `AndroidMultiDeviceV1/LiveSettingsDialog.cs` (giao diện 3 tab).
- **CẬP NHẬT**: `AndroidMultiDeviceV1/MainForm.cs` (mở dialog qua menu trái).
- `BUILD_RUN.bat` giữ nguyên.

## Môi trường thử nghiệm

Mã nguồn đã được soát tĩnh và ZIP được kiểm tra tính toàn vẹn tại môi trường đóng gói. Môi trường này **không có .NET SDK/Windows**, vì vậy chưa thể thực hiện biên dịch WinForms hoặc chạy trên XCloudPhone. Người vận hành cần gửi log `BUILD_RUN.bat` nếu có lỗi biên dịch.
