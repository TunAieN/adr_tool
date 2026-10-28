# V1.6.0.9 — Sửa cuộn Settings để tìm Log out

Áp dụng trực tiếp trên **V1.6.0.8**. Bản vá chỉ đổi hai file:

- `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`
- `AndroidMultiDeviceV1/Core/LoginWorkflow.cs`

## Sửa gì?

- Không còn suy luận `scrollGesture=false` là đã tới cuối Settings sau một lần cuộn.
- Ưu tiên `mobile: scrollGesture` theo phần tử Android `scrollable=true` nếu UI cung cấp; nếu không thì cuộn trong vùng nội dung.
- Nếu cuộn không đổi UI hoặc API trả `false`, thử thêm `mobile: swipeGesture` từ dưới lên, luân phiên vùng trung tâm / vùng phải của nội dung.
- Chờ giao diện ổn định và tìm XPath `//android.view.View[@content-desc="Log out"]` sau mỗi lượt.
- Giới hạn tối đa 16 lượt; nếu UI không đổi 4 lượt liên tiếp, thông báo không thể cuộn, **không tuyên bố đã đến cuối**.
- Sau khi nhấn mục Logout, vẫn phải nhấn nút xác nhận và xác minh trạng thái đã đăng xuất mới được thực hiện bước đăng nhập tiếp theo.
- Không thay đổi kho tài khoản, SQLite/JSON lưu trữ, đa thiết bị, dữ liệu đăng nhập hoặc cấu hình cập nhật hồ sơ.

## Cài đặt

1. Đóng Android Multi-Device Manager, sao lưu cả mã nguồn và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén `ANDROID_MULTI_DEVICE_V1_SCROLL_SETTINGS_V1609_PATCH.zip` vào thư mục dự án V1.6.0.8; ghi đè hai file C# theo đường dẫn ở trên.
3. Chạy `BUILD_RUN.bat`. Mở kho và kết nối Remote ADB, Appium như trước.
4. Chạy thử một điện thoại trên tài khoản anh được phép quản lý.

## Log cần kiểm tra

- `Logout: đã mở Settings and privacy.`
- `Logout: cuộn Settings lượt 1/16; Appium còn cuộn=no/uncertain; UI thay đổi=yes/no; ...`
- `Logout: đã tìm và chọn Log out sau ... lượt cuộn Settings.`
- `Logout: đã xác nhận hộp thoại; chờ TikTok về màn hình chưa đăng nhập.`

Nếu vẫn báo *nội dung Settings không di chuyển*, chụp screenshot và xuất **UI source Settings** bằng Inspector để xác định chính xác node `scrollable` và bounds của vùng cuộn. Không gửi mật khẩu hoặc log Appium có chứa thông tin đăng nhập.

**Đã kiểm tra ở môi trường tạo bản vá:** mã nguồn đúng bản V1.6.0.8, chỉ có hai file C# thay đổi, các file ZIP toàn vẹn. Chưa build/kiểm thử trực tiếp bằng .NET Windows và XCloudPhone.
