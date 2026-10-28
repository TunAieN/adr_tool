# Android Multi-Device V1.5.8 — LOGIN STATE FIX

## Lỗi được giải quyết

Log thực tế cho thấy `Preflight: LoggedOut` là **đúng**. Lỗi bắt đầu sau khi click nút `Log in (z78)`:
- 00:23:29: log `đã mở Log in (z78)`;
- 00:23:50: 21 giây sau, tool nhảy sang chờ xác minh mà không có log `đã chọn Continue with Google`.

Code V1.5.7 tìm `continueGoogle` trong 20 giây. Nếu không tìm/click được, nó trả `NeedsHuman`; thiết bị chuyển `WAIT_VERIFY` và tự quan sát 3 phút nhưng không thể vượt qua màn hình chọn phương thức đăng nhập. Đây là lỗi phân loại trạng thái, không phải TikTok không nhận ra LoggedOut.

## Thay đổi

- `LoginWorkflow.cs`: Sau nút Log in, phát hiện Google chooser/email hoặc tìm & bấm Continue with Google trong tối đa 30 giây. Không bấm lặp khi đã tới Google. Nếu không tìm được thì trả lỗi kỹ thuật kèm foreground package (không đọc mật khẩu).
- `TikTokStateManager.cs`: Giữ thông điệp chi tiết khi thực sự cần người dùng xử lý xác minh; không coi mọi lỗi đăng nhập là chờ xác minh chung.
- `DeviceOrchestrator.cs`: Hiển thị lý do trước khi đợi Google/2FA.
- `selectors.json`: Thêm phương án tìm nút Continue with Google theo nhãn/text/descendant bên cạnh XPath Compose gốc. Các XPath có thể vẫn cần hiệu chỉnh theo app thực tế.

## Cài

Đóng tool; sao lưu mã nguồn và `%LOCALAPPDATA%\AndroidMultiDeviceV1`; giải nén PATCH vào thư mục chứa BUILD_RUN.bat (ghi đè 4 file theo cấu trúc). Không xóa kho `account-vault.json`. Chạy BUILD_RUN.bat, giữ Appium Server, thử một máy trước.

## Log mong đợi

`Preflight: LoggedOut` -> `TikTok: đã nhấn Log in (z78)` -> `TikTok: đã nhấn Continue with Google` -> `Google: đã chọn Add another account` -> tiến tới nhập email/password, hoặc báo lỗi CHÍNH XÁC tại bước còn thiếu.

Nếu còn báo `không tìm/click được Continue with Google`, hãy cung cấp ảnh màn hình điện thoại và thuộc tính nút trong Appium Inspector sau khi nhấn Log in. **Không cung cấp mật khẩu, XML chứa thông tin bí mật hoặc log Appium gửi credentials.**

## Giới hạn kiểm thử

Đã kiểm tra cấu trúc JSON, mã nguồn và ZIP. Chưa chạy dotnet build / Windows hoặc kết nối tới XCloudPhone trong môi trường xây dựng, nên chưa xác nhận đã sửa dứt điểm trên UI thực tế. Không thay đổi hệ quản lý tài khoản, phân bổ máy hoặc chính sách hạn chế tài khoản.
