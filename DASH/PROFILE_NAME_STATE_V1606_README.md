# V1.6.0.6 — PROFILE NAME STATE FIX

Bản nền: V1.6.0.5 AFTER LIVE (C# .NET 8 WinForms).

## Vì sao lỗi
V1.6.0.5 chỉ ghi `NameVerified = true` sau khi **mở lại trang chỉnh tên** và đọc được nội dung. TikTok khóa việc đổi nickname trong 7 ngày ngay sau cập nhật, nên việc mở lại trình sửa tên không đáng tin cậy và có thể dẫn tới báo lỗi dù thao tác Save đã thành công. Khi khởi động lại, trạng thái không được ghi bền vững làm tool thử đổi tên lần nữa.

## Sửa đổi
- Thêm trạng thái riêng theo AccountId trong `profile-update-records.json`: `NameChangeState`, `NameAttemptedAt`, `NameEligibleAt`, `NameObserved`; vẫn tương thích với dữ liệu cũ.
- `VERIFIED`: đã đọc được tên thực trên hàng Name ở màn hình Edit Profile, bỏ qua đổi tên mọi phiên sau.
- `SAVE_PENDING`: ghi **trước** khi gửi Save để tránh phát lại sau gián đoạn, chỉ kiểm tra bằng cách đọc **hàng Name**, không mở lại ô sửa tên.
- `COOLDOWN`: phát hiện cảnh báo “Your nickname can only be changed every 7 days”, ghi mốc hiển thị nếu trích xuất được (nếu không, dùng ngày dự kiến sau 7 ngày), **không cố đổi lại**. Trạng thái này không đồng nghĩa đã xác nhận tên trùng dữ liệu cấu hình.
- Nếu chỉ tên bị giới hạn/chưa thể đối chiếu, trả về `PROFILE_PARTIAL` rồi tiếp tục các mục khác và phiên đã xác minh; không nói đã hoàn tất toàn bộ hồ sơ.
- Nếu lỗi kỹ thuật UI trong quá trình cập nhật hồ sơ, chuyển tài khoản sang `RETRY_PENDING` qua bộ giới hạn thử lại có sẵn. Lần sau không phát lại Save tên khi đã lưu `SAVE_PENDING`.

## Cài đặt
1. Đóng tool và sao lưu mã nguồn + `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén bản vá vào thư mục gốc dự án hiện tại, ghi đè 2 file C#.
3. Chạy `BUILD_RUN.bat`, giữ Appium Server như trước.
4. Kiểm tra bằng một tài khoản được phép sử dụng. Sau khi đã đổi tên, dừng/Start lại và xác nhận log nói bỏ qua đổi tên.

## Lưu ý về dữ liệu cũ
Những lần Save ở V1.6.0.5 mà **chưa ghi kết quả** không thể tự suy ra là thành công. Bản mới sẽ đọc tên hiện tại; nếu TikTok đã khóa do 7 ngày, ghi `COOLDOWN` và không gửi Save tiếp. Không tự kết luận thành công nếu tên trên Profile chưa khớp.

Không xóa `profile-update-records.json` hoặc `account-vault.json`. Kho tên random có thể thay đổi nhưng tên tài khoản đã xác minh sẽ không bị đổi lại tự động. Muốn chủ động đổi tên lần nữa trong tương lai cần một luồng đổi tên riêng với xác nhận rõ ràng sau khi hết giới hạn nền tảng.

## Giới hạn kiểm thử
Đã kiểm tra cấu trúc mã nguồn và bản ZIP bằng công cụ tĩnh. Chưa biên dịch bằng .NET SDK trên Windows hoặc chạy với TikTok/XCloudPhone thật; phải thử trên thiết bị của anh.
