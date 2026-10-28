# V2.1.38 – Sửa bàn giao trạng thái Profile → Edit Profile sau xác minh username

## Lỗi thực tế

17:51:09 tool nhấn `tlh` để đọc username mới; 17:51:11 `mud[2]` khớp tài khoản.
Nhưng ProfileUpdateWorkflow v2.1.37 vẫn luôn nhấn Profile → Edit lần hai,
khiến báo `Không tìm thấy Profile hoặc Edit sau xác minh đăng nhập`.

## Những gì sửa

1. Trước thao tác cập nhật tên/Bio/ảnh, đọc page source của chính Appium session vừa xác minh.
2. Nếu đang ở trang `Edit profile` với hàng `Name` và `Bio`, giữ nguyên màn hình.
   - Nếu có `mud[2]`, xác minh lại **khớp hoàn toàn** với username tài khoản phân công; không sửa nếu lệch hoặc không đọc được.
   - Không tự click tab Profile, không nhấn Sửa lần hai.
3. Nếu chưa ở Edit profile: **giữ đường cũ** mở tab Profile rồi nút Edit (selector `profileEditButton`); đồng thời hỗ trợ giao diện mới `tlh` (`profileEditEntry`).
4. Sau nhấn Edit cần xác minh trang đã mở thật, không coi click thành công đồng nghĩa đã chuyển trang.
5. `ReturnToEditAsync` và nhánh cập nhật ảnh cho phép quay về trang Edit bằng `tlh` nếu Profile mới xuất hiện.
6. Bổ sung cách **đọc** Name từ `mud[1]` khi giao diện mới không có `mtq[1]`; vẫn giữ selector cũ ở vị trí ưu tiên. Không đổi username.
7. Giữ toàn bộ kho, journal tránh lưu lặp, luồng đăng nhập và XPath cũ.

## Log mẫu

```text
PROFILE_EDIT_USERNAME_FOUND
Preflight kết thúc -> Ready
PROFILE_UPDATE_HANDLE_RECHECK: mud[2] khớp đúng username tài khoản đã xác minh.
PROFILE_UPDATE_EDIT_REUSED: sau xác minh đã ở Edit profile; bỏ qua nhấn Profile/Sửa hồ sơ lần hai.
Tên hiển thị: ...
```

Nếu giao diện cũ:

```text
PROFILE_UPDATE_LEGACY_NAVIGATION: chưa ở Edit profile; mở Profile như luồng cũ.
PROFILE_UPDATE_OPEN_LEGACY: tìm nút Edit giao diện cũ.
PROFILE_UPDATE_EDIT_READY: đã xác nhận Edit profile trước khi sửa thông tin.
```

## Kiểm thử

- Dùng Phone 02 / ADB `adb.xcloudphone.net:25153`, đóng Appium Inspector trước khi chạy tool.
- Mở Kho, chạy TEST tài khoản đúng đã gán và kiểm tra `PROFILE_UPDATE_EDIT_REUSED`.
- Tên/Bio/ảnh vẫn phụ thuộc XPath các trường sửa riêng của phiên bản TikTok trên thiết bị; nếu trường nào không có sẽ dừng an toàn và ghi log, **không tự xác nhận thành công**.
- Môi trường xử lý không có .NET SDK và thiết bị Android nên **chưa xác nhận build hoặc chạy thiết bị thực tế**.
