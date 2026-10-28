# V2.1.37 – Nhận diện username trên giao diện mới và cũ

## Nguyên nhân
Bản V2.1.36 chỉ xác nhận Profile bằng `t_k`. Sau Google login, TikTok mới có thể hiển thị nút Sửa hồ sơ `tlh`, khiến Preflight ghi `PROFILE_NAVIGATION_NOT_CONFIRMED` dù chủ sở hữu đã vào Profile.

## Sửa
- Giữ nguyên nhánh cũ `profileUsernameButton` (`t_k`) và `profileHandle`/`profileLoggedInMarker`.
- Khi đã thực sự yêu cầu nhấn tab Profile, chấp nhận nút `tlh` hiển thị như bằng chứng trang Profile **mới** đã mở.
- Trong trang Profile mới, nhấn `tlh` tối đa **một lần**, đợi trang edit profile rồi chỉ đọc `mud[2]`. Không lấy `mud[1]` vì có thể là tên hiển thị.
- Chuẩn hóa và **đối chiếu chính xác** handle với tài khoản đã gán; không coi thành công chỉ vì đọc được username khác.
- Nếu sau 15 giây không có `mud[2]` hợp lệ, ghi `PROFILE_EDIT_USERNAME_NOT_FOUND`, trả `Unknown`, không tự động đăng xuất hoặc xóa tài khoản.
- Giữ nguyên xử lý Terms, Google OAuth, contacts permission và màn hình hướng dẫn Swipe.

## Log mong đợi
```
PROFILE_TAP_REQUESTED: lần 1/2
PROFILE_PAGE_CONFIRMED: đã thấy nút Sửa hồ sơ tlh (giao diện mới)
PROFILE_EDIT_ENTRY_FOUND: tlh hiện trên trang Profile
PROFILE_EDIT_TAP_REQUESTED: đã nhấn tlh; chờ trang Sửa hồ sơ và mud[2]
PROFILE_EDIT_USERNAME_FOUND: đọc username từ mud[2], đối chiếu chính xác với kho
```

Nếu ứng dụng dùng giao diện cũ `t_k`, không cần vào Sửa hồ sơ.

## Thử nghiệm
1. Giải nén vào thư mục mới; mở khóa Kho và Refresh thiết bị Phone 02.
2. Đảm bảo ADB hiện `adb.xcloudphone.net:25153 device`.
3. Tắt Appium Inspector trên cùng máy trước khi chạy; chạy `BUILD_RUN.bat`.
4. Chọn TEST tài khoản đã gán. Quan sát log `PROFILE_*`.
5. Nếu vẫn Unknown, gửi XML sau khi nhấn Sửa hồ sơ (có thể ẩn dữ liệu nhạy cảm).

Mã nguồn đã được cập nhật và đóng gói, **chưa build hoặc thử thực tế trên Windows/Android** do máy xử lý không cài .NET 8 SDK/thiết bị.
