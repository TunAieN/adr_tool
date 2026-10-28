# V1.5.9 — TikTok Welcome Back → Add another account → Google Login

## Lỗi xác định từ ảnh thực tế

Khi TikTok đã đăng xuất nhưng nhớ tài khoản từng dùng, tab Profile mở trang:

- Welcome back [username cũ]
- Nút đỏ **Log in** (KHÔNG chọn vì có thể đăng nhập lại tài khoản nhớ sẵn)
- **Add another account**, resource-id `com.ss.android.ugc.trill:id/z78`

Phiên bản V1.5.8 có thể đi tìm **Continue with Google** quá sớm và báo lỗi 30 giây dù màn hình lựa chọn phương thức Google chưa mở.

## Sửa đổi

- `Core/LoginWorkflow.cs`: sau Start, nhận diện trạng thái TikTok theo màn hình; nếu có Welcome back thì nhấn **Add another account** (z78) đúng một lần, chờ UI mới rồi tìm **Continue with Google**. Nếu khởi đầu từ Home/Profile, tự mở Profile và đi qua các bước cần thiết. Không chọn nút đỏ Log in của Welcome back. Không nhấn lặp khi màn hình chưa đổi. Có log từng trạng thái và giới hạn chờ.
- `Config/selectors.json`: thêm `tiktokWelcomeBack`, `tiktokAddAnotherAccount`, `tiktokLoginMethods`. Các selector Google chooser/email/password cũ vẫn giữ nguyên.

## Cài đặt

Đóng phần mềm. Sao lưu dự án và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
Giải nén ZIP PATCH tại thư mục chứa BUILD_RUN.bat, ghi đè 2 file theo cấu trúc thư mục.
Mở lại Appium Server, bảo đảm ADB nhận thiết bị rồi chạy BUILD_RUN.bat.
Không cần xóa kho tài khoản hoặc nhập lại Excel.

## Log kỳ vọng

1. `Preflight: LoggedOut`
2. `TikTok: Welcome back -> đã chọn Add another account (z78)`
3. `TikTok: đã nhấn Continue with Google`
4. `Google: đã hiển thị Choose an account / Email`
5. `Google: đã chọn Add another account...` nếu chooser xuất hiện
6. Đăng nhập/xác minh TikTok handle do SessionReconciler đảm nhiệm

## An toàn và giới hạn

Nếu TikTok chưa được xác nhận đang ở màn hình Welcome back, code không tự giả định nút z78 mang ý nghĩa Add another account. Code không sử dụng thao tác đăng nhập tài khoản được ghi nhớ và không vượt CAPTCHA/2FA. Mỗi máy độc lập; không đổi quy tắc kho, phân công, retry hoặc logout.

Chưa thể thực thi dotnet build hoặc kết nối XCloudPhone trong môi trường tạo bản vá này. Cần kiểm thử thực tế màn hình TikTok vì resource-id và cây Compose thay đổi theo phiên bản.
