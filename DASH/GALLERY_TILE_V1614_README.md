# V1.6.1.4 — Gallery crop based on selection badge

## Chẩn đoán đúng từ log
- `l37` trả về nút tròn chọn ảnh kích thước 21×21, KHÔNG phải thumbnail.
- Gallery Recents trong ảnh người dùng có 3 ô ảnh ~88×88 px, nút tròn nằm góc trên bên phải.
- V1.6.1.3 loại toàn bộ ảnh vì vùng 21×21 nhỏ hơn 40×40 nên điểm 0.000.

## Thay đổi
- `GalleryPhotoMatcher.FindAsync` lấy vị trí các nút tròn `l37`, suy kích thước ô ảnh từ khoảng cách giữa các vòng cùng hàng.
- Cắt phần ô ảnh lớn để so sánh pixel với ảnh nguồn, nhưng khi click vẫn sử dụng element ID của vòng chọn tương ứng.
- Không mặc định ảnh đầu tiên là ảnh vừa tải; **chỉ click** khi score >= 0.88 và hơn ảnh gần nhất >= 0.08.
- Nếu thiếu 2 nút cùng hàng, hình ảnh không đủ rõ, ảnh trùng hoặc gallery chưa index ảnh mới thì báo lý do; không click theo vị trí.
- Chưa tự bật Save & post. Chính sách hiện có giữ nguyên.

## Cách cập nhật
Đóng tool và sao lưu dự án. Giải nén bản vá lên mã nguồn **V1.6.1.3**, ghi đè `AndroidMultiDeviceV1/Core/GalleryPhotoMatcher.cs`. Chạy `BUILD_RUN.bat`. Không xóa kho.

## Log cần xem
`Ảnh: Gallery lượt ... so khớp 3; điểm tốt nhất=...`.
Nếu ảnh tốt nhất vẫn thấp, cần screenshot thực tế của Gallery và đường dẫn ảnh đã chuẩn hóa (KHÔNG gửi dữ liệu đăng nhập).

Bản cập nhật đã kiểm tra cú pháp cấu trúc và thuật toán định vị với screenshot Gallery người dùng gửi, **chưa biên dịch trên Windows / kiểm thử trên XCloudPhone**.
