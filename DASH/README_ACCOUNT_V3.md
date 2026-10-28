# Android Multi-Device Manager — Account Lifecycle V3

Nâng cấp từ **DeviceV2 FAST**, không thay đổi các file `LiveSearchEngine.cs`, `LiveVisualOcr.cs`, `LoginWorkflow.cs`, `LiveVerifiedSessionHandoff.cs` hoặc Phase 5.

## Cài đặt

1. **Thoát tool**, đợi các worker dừng và Appium session giải phóng.
2. Sao lưu `%LOCALAPPDATA%\AndroidMultiDeviceV1\account-vault.json` và `settings.json` ở nơi bảo mật. Không chia sẻ vault hoặc mật khẩu.
3. FULL: giải nén `AccountV3.zip` và chạy `DASH\BUILD_RUN.bat`. PATCH: chép các file từ `PatchV3.zip` vào **cùng cấu trúc thư mục** của bản DeviceV2, ghi đè.
4. Trong tool, mở Kho → tab **TÀI KHOẢN** → **Vòng đời TK**; hoặc **CẤU HÌNH** → **Vòng đời tài khoản**.
5. Cấu hình mặc định: 360 phút, tự chuyển tài khoản **bật**, không dùng lại **bật**, xóa credential sau **24 giờ**.

## Chính sách trạng thái

- Tài khoản có `LastStart` từ bản cũ được xem là **đã từng dùng** (cách nâng cấp thận trọng). Dữ liệu cũ vẫn ở kho, tô đỏ và không được Start/TEST khi Single Use bật.
- Một tài khoản AVAILABLE nhưng đã có dấu dùng cũng không được đưa vào hàng đợi. `Đặt lại` không xóa dấu `EverUsed`.
- Sau khi xác minh TikTok username, `SessionStartedAtUtc`, `SessionDeadlineUtc`, `EverUsed` được lưu vào vault mã hóa. Đồng hồ **không** bắt đầu từ lúc nhập mật khẩu Google.
- Worker tài khoản có một `CancellationTokenSource` riêng, hết giờ mới ngừng luồng LIVE hiện tại. Khi session đã đóng, xóa dữ liệu TikTok + Google Play Services đúng thiết bị qua quy trình đang dùng. Không tác động tới worker máy khác.
- Start thường có thể tiếp tục bằng một tài khoản AVAILABLE mới của chính máy. TEST 1 tài khoản chỉ test 1 tài khoản; khi hết giờ vẫn xóa dữ liệu nhưng **không** lấy tài khoản khác.
- Khi Appium/ADB/LIVE thoát sớm trước hạn, dừng an toàn thay vì âm thầm xoay tài khoản. Khi CAPTCHA/2FA xuất hiện, không tự xử lý vượt xác minh.
- Khi hết tài khoản mới, worker ngừng và báo `ACCOUNT_POOL_EXHAUSTED`.
- Xóa tự động: sau 24h từ lúc phiên kết thúc, trong lúc **ứng dụng đang chạy và vault đang mở**, vault xóa email, mật khẩu, handle và ẩn dòng khỏi kho. Bản ghi dấu HMAC trong vault mã hóa ngăn nhập trùng trở lại. Nếu phần mềm đang tắt, dọn bù lần tiếp theo mở khóa vault. Không xóa tài khoản thật trên TikTok/Google.
- **Start LIVE trực tiếp** chỉ là worker LIVE đọc/chạy giao diện không có phiên AccountVault được xác minh, nên không kích hoạt bộ đếm vòng đời V3; chạy qua **Start tài khoản** để áp dụng.

## Kiểm thử trên Windows

1. Chạy `DASH\BUILD_CHECK_ONLY_V2161.bat` hoặc `DASH\BUILD_RUN.bat` trên .NET 8 Windows.
2. Nhập ít nhất hai tài khoản **mới** vào đúng pool Phone 02; cấu hình thử nghiệm 1–2 phút.
3. Nhấn Start thiết bị (không dùng nút Start LIVE). Sau `ACCOUNT_SESSION_STARTED`, theo dõi cột **Còn lại**.
4. Khi hết hạn: log `ACCOUNT_SESSION_EXPIRED`, `ACCOUNT_ROTATION_AFTER_LIMIT`, `ACCOUNT_POOL_EXHAUSTED` (nếu hết kho). Xác nhận `pm clear` đúng serial; máy khác không bị ngắt.
5. Dừng thử một account và Start lại: account đã dùng phải đỏ, không tự chọn/TEST được. Thử nhập lại credential sau khi dọn: phải bị chặn.
6. Cài giờ xóa 0 để thử tính năng rồi **khôi phục 24 giờ**; dữ liệu bị xóa khỏi kho là không hoàn tác được nếu không có bản sao lưu.
7. Xác nhận ứng dụng không tự vượt CAPTCHA/2FA.

## Xác minh kỹ thuật trong gói

`TEST_ACCOUNT_LIFECYCLE_V3.py` và các bài hồi quy Python hiện hữu kiểm tra source/regression tĩnh. Gói này **chưa được biên dịch và chạy thử trên Windows với điện thoại thật trong môi trường tạo**. Hãy kiểm tra với một máy, sử dụng kho test, trước khi triển khai nhiều máy.
