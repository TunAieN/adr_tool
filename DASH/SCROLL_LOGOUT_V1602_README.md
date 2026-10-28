# Android Multi-Device V1.6.0.2 — Scroll to Log out

Bản vá áp dụng trên **V1.6.0.1** (bản Profile Update đã sửa CS0121).

## Nguyên nhân

TikTok đặt `Log out` ở cuối màn hình **Settings and privacy**. Bản trước dùng `WaitTap("logout",20,...)` nên không cuộn trang và không tìm thấy nút, dù XPath đúng.

## Thay đổi

- `Core/AdbAndAppium.cs`: thêm `AppiumSession.ScrollDownAsync()` dùng Appium UiAutomator2 `mobile: scrollGesture` (POST `/session/{id}/execute/sync`), lấy kích thước màn hình từ `/window/rect`.
- `Core/LoginWorkflow.cs`: thêm `TapSettingsLogoutWithScrollAsync()`, kiểm tra XPath trước rồi cuộn từng lượt, tối đa **12** lượt; sau mỗi lượt chờ 550 ms và tìm/click lại `logout`.
- Nếu đến cuối danh sách không thấy `Log out`, Appium không cuộn được, hoặc rời TikTok, dừng an toàn và ghi log; **không** tự động chuyển sang Google Login.
- Giữ nguyên 2 selector logout khác nhau và bước xác nhận logout + xác minh giao diện đã đăng xuất.
- Không thay kho tài khoản, Multi-Device, profile updater, selectors hoặc cấu hình máy.

## Cách áp dụng bản PATCH

1. Tắt tool đang chạy, sao lưu thư mục mã nguồn và dữ liệu `%LOCALAPPDATA%\\AndroidMultiDeviceV1`.
2. Giải nén `ANDROID_MULTI_DEVICE_V1_SCROLL_LOGOUT_V1602_PATCH.zip` vào thư mục dự án V1.6.0.1; ghi đè:
   - `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`
   - `AndroidMultiDeviceV1/Core/LoginWorkflow.cs`
3. Chạy `BUILD_RUN.bat` và kiểm tra log.

## Dòng log mong đợi

```text
Phone 01: Logout: đã mở Profile menu.
Phone 01: Logout: đã mở Settings and privacy.
Phone 01: Logout: đã tìm và chọn Log out sau N lần cuộn Settings.
Phone 01: Logout: đã xác nhận hộp thoại; chờ TikTok về màn hình chưa đăng nhập.
Phone 01: Logout: Profile ở trạng thái chưa đăng nhập.
```

> Bước cuối phụ thuộc UI thực tế; nếu không khớp phải xác minh trước khi chuyển tài khoản. Chạy thử với một điện thoại/tài khoản được phép quản lý trước.

## Giới hạn kiểm thử

Đã kiểm tra mã nguồn thay đổi, đóng gói ZIP và JSON; **chưa biên dịch và chưa kiểm tra trên thiết bị Windows/XCloudPhone** do môi trường tạo bản vá không có .NET SDK hoặc thiết bị Android của người dùng.
