# ANDROID MULTI DEVICE V1.6.0.5 — SAU KHI THOÁT LIVE VẪN PHẢI KIỂM TRA PROFILE

Dựa trên V1.6.0.4 (LIVE_RECOVERY). Bản này sửa lỗi log dừng ở:

`Đã thấy giao diện TikTok có nút Profile/Login sau khi thoát màn hình phụ.`

## Đã sửa

1. Sau `Back` thành công, ghi log rõ đang đọc lại Profile và username, thay vì im lặng trong một lần `DetectAsync` chạy lại từ đầu.
2. Trước khi điều hướng tới Profile, tận dụng `pageSource` đã đọc để nhận dạng màn hình login, không thực hiện 14 lệnh XPath tuần tự không cần thiết.
3. Chỉ tìm và nhấn nút Profile khi XML đang cho thấy có tab Profile; nếu chưa thấy thì chờ UI hiện.
4. Báo tiến độ `Preflight UI` khi đợi UI, điều hướng Profile và đọc username.
5. Đặt giới hạn thời gian cho lệnh Appium UI: current_package 20 giây, source 35 giây, truy vấn phần tử 25 giây. Nếu UiAutomator2 treo, trả về lỗi kỹ thuật thay vì đứng im tới hạn HTTP 150 giây.
6. Giữ nguyên giới hạn xác minh danh tính trước thao tác logout; không reset dữ liệu ứng dụng và không bỏ qua các màn hình bảo mật.

## File thay thế

- `AndroidMultiDeviceV1/Core/TikTokStateManager.cs`
- `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`

## Hướng dẫn

1. Đóng tool và sao lưu thư mục mã nguồn / dữ liệu `%LOCALAPPDATA%\\AndroidMultiDeviceV1`.
2. Giải nén PATCH vào thư mục chứa `BUILD_RUN.bat` của bản **V1.6.0.4**, giữ đúng đường dẫn và ghi đè hai file trên.
3. Chạy `BUILD_RUN.bat`.
4. Trong lượt thử đầu, dùng một thiết bị kiểm thử đang có TikTok ở LIVE, chọn Start và đọc dòng `Preflight UI` trong log.

Nếu còn mắc, sao chép log từ `Preflight UI: đã trở lại...` tới lỗi tiếp theo. Không cần cung cấp mật khẩu hoặc token.

## Kiểm tra bản phát hành

- Đã kiểm tra khác biệt so với 1.6.0.4: chỉ có hai file C# thay đổi.
- Kiểm tra dấu ngoặc mã nguồn bằng lexer và thử tính toàn vẹn ZIP.
- **Chưa biên dịch/kiểm thử trên Windows XCloudPhone;** yêu cầu chạy `BUILD_RUN.bat` để xác nhận hoạt động thực tế.
