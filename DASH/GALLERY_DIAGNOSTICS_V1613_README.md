# V1.6.1.3 — Gallery pixel-matching diagnostics

Bản này sửa vấn đề `3 thumbnail; điểm=0.000` trong V1.6.1.2. `VisibleCount` là số phần tử `l37` được tìm, KHÔNG phải số vùng ảnh đã được so sánh. Nếu Appium trả vùng `rect` nhỏ hoặc tọa độ không khớp screenshot, V1.6.1.2 loại hết ứng viên và để điểm 0.000 nhưng không cho biết nguyên nhân.

### Thay đổi

- Chỉ báo cả số phần tử `l37` và số ảnh thật được cắt/so khớp.
- Ghi tóm tắt screenshot/window/scale, các tọa độ nút mẫu, nguyên nhân loại (`quá nhỏ`, `ngoài màn hình`, `rect lỗi`) khi mọi ứng viên bị loại.
- Thử lại một vài lần ngay trên trang Gallery khi KHÔNG có ảnh so khớp, không cuộn liên tục bỏ lỡ ảnh mới upload.
- Sửa điều kiện lệch tỷ lệ ảnh screenshot/window vốn quá cứng khi Appium đang dùng mật độ màn hình khác; vẫn dừng nếu tọa độ lệch lớn.
- Không chọn ảnh theo thứ tự, không tự bấm Save & post. Luồng bảo toàn các thành phần khác.

### Cài đặt

Đóng tool. Sao lưu thư mục dự án và `%LOCALAPPDATA%\AndroidMultiDeviceV1`. Áp dụng PATCH lên bản V1.6.1.2 (không cần xóa kho). Chạy `BUILD_RUN.bat`.

### Cần đọc trong log

`Ảnh: Gallery lượt 1/4; tìm thấy 3 phần tử; so khớp 0; ...`
`Ảnh: lỗi nhận diện thumbnail: ... quá nhỏ=...; ngoài màn hình=...; screenshot=...; window=...; node=(...) ...`

Nếu `so khớp=0`, cần chụp UI Gallery và XML của các node `l37` thực tế để phân biệt nút chọn nhỏ nằm trên ảnh và ảnh thật. Không thể suy luận chắc chỉ từ số 0.000.

Mã nguồn chưa được biên dịch trên Windows và chưa thử trực tiếp trên thiết bị XCloudPhone.
