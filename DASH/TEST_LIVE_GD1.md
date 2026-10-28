# TEST — Cấu hình LIVE Manager Giai đoạn 1

1. Giải nén `ANDROID_MULTI_DEVICE_LIVE_GD1_V201_FULL.zip` vào thư mục mới trên Windows và chạy `BUILD_RUN.bat`.
2. Giao diện chính có menu `CẤU HÌNH LIVE` (không còn `LIVE (sắp làm)`).
3. Mở menu, tab `Nguồn LIVE`, kiểm tra mặc định: Keywords = 3, Per keyword = 65, Total = 130, Batch = 8.
4. Đổi `Viewer tối thiểu` thành 2500, sửa danh sách từ khóa, bấm Lưu. Đóng và mở tool, cấu hình phải không mất.
5. Đổi `Timeout toàn đợt` thành 20 trong khi timeout/từ khóa là 65, bấm Lưu. Tool phải báo lỗi, không đóng dialog.
6. Đổi Viewer tối đa thành 10, Viewer tối thiểu thành 2500. Tool phải báo cấu hình không hợp lệ.
7. Tab `Phiên & thiết bị`: thử 120 giây ở LIVE, 5 giây chờ tối thiểu, 3 lần UI ổn định, tối đa 2 máy/phòng. Lưu/mở lại khớp.
8. Tab `Thư viện nội dung`: nhập 3 dòng nháp, xuất TXT, đóng và mở lại thấy 3 dòng. Không có nút gửi LIVE.
9. Đóng dialog bằng Hủy, đảm bảo thay đổi chưa lưu không ảnh hưởng file.
10. Kiểm tra `Kho tài khoản`, `Start máy`, `Chạy TK tick`, `Refresh` không bị mất chức năng.

Cấu hình lưu tại `%LOCALAPPDATA%\AndroidMultiDeviceV1\live-manager-settings.json`.
File `settings.json`, `account-vault.json`, `selectors.json` không bị thay đổi khi chỉ lưu cấu hình LIVE.
