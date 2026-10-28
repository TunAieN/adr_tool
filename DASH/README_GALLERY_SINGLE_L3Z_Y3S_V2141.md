# V2.1.41 — Gallery mới một ảnh: l3z & y3s

Dựa trên V2.1.40; Name và Bio đã được xác nhận lưu thành công trên Cloud Phone.

## Nguyên nhân
Giao diện Recents hiện chỉ có **một** thumbnail. Bản cũ tìm nút l37; người vận hành xác định nút mới l3z. Ngoài ra, matcher cũ chỉ suy được kích thước thumbnail từ **ít nhất hai nút chọn cùng hàng**. Nút Next cũ y1v nay là y3s.

## Thay đổi
- `profileGalleryItems`: ưu tiên `l3z` (mới), dự phòng `l37` (cũ).
- `profilePhotoNext`: ưu tiên `y3s` (mới), dự phòng `y1v` (cũ).
- Matcher hỗ trợ nút `l3z` có bounds là cả ô ảnh và trường hợp chỉ có một nút chọn nhỏ trong Recents. Với một nút nhỏ, ước lượng ô ~1/4 bề rộng màn hình nhưng **chỉ chọn nếu so khớp ảnh gốc >= 0.88**; không chọn ảnh đầu chỉ do vị trí. Nếu không đủ độ tin cậy, dừng và ghi log.
- Log giải thích locator, số nút chọn, số thumbnail thực so sánh, điểm similarity, hình học.
- Giữ chuỗi chọn ảnh → Next → Confirm → Save & post (chỉ nếu cờ cho phép đang bật) và cơ chế nhật ký SAVE_PENDING để tránh gửi lặp khi chưa rõ kết quả.

## Kiểm thử
1. Giải nén sang thư mục mới; chạy BUILD_RUN.bat trên máy Windows có .NET 8 và Appium.
2. Kết nối Phone 02 cổng ADB mới, không dùng cổng máy thuê cũ.
3. Chạy thử đúng tài khoản đã gán. Vì Name/Bio đã được đánh dấu xác minh, chương trình chỉ cần tiếp tục ở Avatar.
4. Xem log: `Ảnh: Gallery sẵn sàng`, `Gallery lượt ... locator=l3z`, `đã chọn thumbnail khớp hình ảnh`, sau đó `Next` và bước xác minh avatar.
5. Nếu similarity không đạt, gửi log cùng XML của trang Recents. Không xóa/đặt lại trạng thái Photo SAVE_PENDING chỉ để thử lại.

Đã kiểm tra mã nguồn/selector offline. Không có .NET SDK hoặc thiết bị Android trong môi trường này; chưa xác nhận build hay gửi Save & post thật.
