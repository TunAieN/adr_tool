# V1.6.1.1 — Name Editor Navigation Fix

Bản vá nối tiếp `ANDROID_MULTI_DEVICE_V1_WELCOME_BACK_LOGOUT_V1610_FULL`.

## Nguyên nhân

`WaitTap(profileNameEntry)` trước đây coi một lần click WebDriver thành công là đã mở màn hình Name. Màn hình vẫn có thể ở `Edit profile`. Khi đó tìm `profileEditorInput` sẽ báo thiếu ô nhập.

## Thay đổi

- Giữ `profileNameEntry` gốc để ĐỌC tên hiển thị từ Edit Profile.
- Thêm `profileNameOpenCandidates` để THỬ click mục Name theo nhiều locator (không chọn Username).
- Sau mỗi click phải thấy trình sửa Name thật sự và EditText, hoặc dấu hiệu hạn chế đổi tên 7 ngày.
- Nếu vô tình tới trình sửa khác Name, quay lại và không nhập dữ liệu.
- Nếu không mở được Name, ghi lỗi kỹ thuật có thể retry và **không gửi Save**.
- Giữ nguyên lịch sử `VERIFIED`, `SAVE_PENDING`, `COOLDOWN` theo từng tài khoản để tránh đổi lặp.

## Cài đặt

Đóng tool. Sao lưu dự án và `%LOCALAPPDATA%\AndroidMultiDeviceV1`. Giải nén patch tại thư mục gốc dự án V1.6.1.0, ghi đè `Core/ProfileUpdates.cs` và `Config/selectors.json` ở đường dẫn đầy đủ `AndroidMultiDeviceV1/...`. Chạy `BUILD_RUN.bat`.

## Kiểm thử

Thử trên tài khoản thử nghiệm được phép quản lý. Kiểm tra log `Tên hiển thị: đã click ứng viên dòng Name ...` và `đã xác minh màn hình Name có ô nhập`/`đang giới hạn sửa tên`. Nếu vẫn không mở được, cần XML UI trên màn hình Edit Profile thực tế (không kèm mật khẩu) để bổ sung locator đúng theo từng phiên bản TikTok.

**Chưa biên dịch hoặc chạy trên Windows/XCloudPhone trong môi trường này.**
