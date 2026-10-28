# V1.6.1.7 - Chạy đúng tài khoản đã tick (Single Account Test)

- `Start máy`: nếu **không tick** tài khoản nào -> chạy hàng đợi cũ; nếu **tick đúng một** -> chỉ chạy tài khoản đó; nếu tick nhiều hơn một -> yêu cầu chọn lại.
- `Chạy TK tick`: nút riêng, bắt buộc tick **chính xác một** tài khoản trong bảng và chọn đúng điện thoại.
- Nếu Phone đang chạy: gửi yêu cầu Stop, đợi worker cũ và Appium session đóng hoàn toàn, sau đó mới bắt đầu phiên TEST của tài khoản tick.
- Nếu worker cũ chưa đóng trong 60 giây: không khởi chạy phiên mới trùng thiết bị.
- Trạng thái tài khoản cũ được chuyển sang INTERRUPTED bởi worker khi dừng, **không xóa tài khoản, không giả định đã logout**.
- `ReserveSpecific` lấy **đúng Account ID**, không fallback sang `ReserveNext()`.
- Chạy TEST **một tài khoản duy nhất**, hết thời gian thì không tự chuyển sang tài khoản tiếp theo; việc đăng xuất cuối phiên vẫn theo quy tắc cũ.
- Tài khoản bị RESTRICTED/DISABLED hoặc trạng thái không đủ điều kiện sẽ không tự động chạy.
- Multi-Device, account vault, profile updates, Excel và checkbox giữ nguyên.

## Cài đặt

Sao lưu toàn bộ dự án và `%LOCALAPPDATA%\AndroidMultiDeviceV1`. Đóng tool. Giải nén PATCH vào thư mục dự án V1.6.1.6 và ghi đè đúng 3 file:

- `AndroidMultiDeviceV1/MainForm.cs`
- `AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs`
- `AndroidMultiDeviceV1/Core/Storage.cs`

Chạy `BUILD_RUN.bat`. Kiểm thử **chỉ Phone 01** và một tài khoản thử nghiệm có quyền sử dụng.

**Lưu ý:** Chưa thể build WinForms .NET 8 trên Linux hoặc kiểm thử Appium XCloudPhone của người dùng tại đây. Nếu build báo lỗi, gửi CMD để sửa chính xác.
