# V1.6.2.1 - TikTok notification permission after pm clear

## Cập nhật
- Từ dự án V1.6.2.0, sao lưu mã nguồn trước khi ghi đè.
- Ghi đè `AndroidMultiDeviceV1/Core/TikTokStateManager.cs` rồi chạy `BUILD_RUN.bat`.

## Sửa đổi
- Sau ADB `pm clear ... = Success` và mở TikTok lần đầu, nhận diện đúng hộp thoại Android `Allow TikTok to send you notifications?`.
- Chỉ nhấn `Don't allow` nếu đồng thời nhận diện tên TikTok, quyền thông báo và nút `com.android.permissioncontroller:id/permission_deny_button`.
- Selector theo ID và XPath được dùng; không chạm nhầm các quyền Contacts hoặc màn hình bảo mật.
- Sau thao tác, chờ Home/Profile/Login xuất hiện để thực hiện login theo luồng cũ.
- Nếu không thể đọc UI, log chi tiết thông báo Appium thay vì chỉ in `InvalidOperationException`.
- Không `pm clear` lần thứ hai nếu app vẫn đang tải hoặc gặp lỗi Appium.

## Giới hạn
Chưa chạy thử trên Windows và XCloudPhone. Nếu sau khi từ chối Android popup mà vẫn còn màn hình TikTok `Allow permissions` ở bên dưới, cần thêm dữ liệu UI của màn hình đó để xác định thao tác tiếp theo; không click theo tọa độ mò.
