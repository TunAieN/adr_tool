# V1.6.0.4 — LIVE / POST-VERIFICATION NAVIGATION RECOVERY

## Những lỗi được sửa
- TikTok còn hiện màn hình LIVE hoặc màn hình toàn màn hình khác khiến tab Profile không hiển thị: nhận diện trạng thái thiếu điều hướng sau 20 giây nếu có UI TikTok có thể tương tác, không chờ 75 giây rồi kết luận sai.
- Sau khi người dùng hoàn thành CAPTCHA/Google verification, nếu không trở về Profile: tự thử **Android Back một lần**, chờ tối đa 12 giây; nếu chưa được, đóng/mở TikTok tối đa một lần nhưng **không xóa dữ liệu**.
- Chưa hoàn tất xác minh hoặc xuất hiện cảnh báo khóa tài khoản: KHÔNG Back, KHÔNG khởi động lại, KHÔNG bỏ qua xác minh.
- Không đăng xuất/chuyển tài khoản nếu username là tài khoản không được quản lý. Các lỗi UI điều hướng được phân loại là lỗi kỹ thuật để thử lại có kiểm soát.

## Cài đặt
Đặt file `AndroidMultiDeviceV1/Core/TikTokStateManager.cs` của bản PATCH vào đúng đường dẫn trong dự án V1.6.0.3 và ghi đè. Sau đó chạy BUILD_RUN.bat.
Giữ nguyên thư mục kho tài khoản, cấu hình và các file .json cá nhân.

## Kiểm thử
1. Dùng tài khoản được phép quản lý, chờ xác minh thủ công hoàn tất. Tool tự quan sát; khi challenge biến mất sẽ phục hồi giao diện nếu thiếu thanh Profile.
2. Kiểm tra đang xem TikTok LIVE nhưng không có challenge, bấm Start: tool thử Back để rời LIVE; nếu không về Home/Profile, khởi động lại app một lần.
3. Trong khi hiện CAPTCHA/2FA thực sự: tool phải đợi, không tự đóng ứng dụng.
4. Trên thiết bị có tài khoản không thuộc kho quản lý: tool phải dừng thay vì đăng xuất.

Đây là bản mã nguồn; không có môi trường .NET Windows và XCloudPhone để xác thực build/runtime tại đây.
