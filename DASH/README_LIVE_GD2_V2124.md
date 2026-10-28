# V2.1.24 – VIEWER OCR DIAGNOSTICS

- Giữ giới hạn lấy mẫu 4+2+2 card, dừng sau 8 card quan sát.
- Chỉ OCR hai card hàng dưới trên màn hình sau khi kéo; tránh làm OCR lại card chồng lấp.
- Nới vùng đọc số Viewer từ 48 lên 60 pixel và điều chỉnh điểm bắt đầu từ +14 sang +12 (so với rìa nhãn LIVE đỏ).
- Khi xung đột/không đọc được, thử Tesseract PSM 13 để so lại với ít nhất một cách tiền xử lý trước đó. Kết quả vẫn là ứng viên OCR, không phải số xác minh tuyệt đối.
- Lưu ảnh cắt `badge-XX-header.png`, `badge-XX-roi.png` theo từng viewport và lượt chụp vào thư mục chẩn đoán `live-scroll-*` ở LocalAppData.
- Nếu nhãn LIVE không xác định được vẫn giữ Viewer trống; không tự điền số hoặc chọn Like.

## Thử nghiệm
Chạy BUILD_RUN.bat trên Windows có .NET 8 SDK, Appium, Tesseract. Sau khi quét từ khóa, gửi log cùng thư mục `live-scroll-*` để so sánh với ảnh badge đã lưu. Không gửi ảnh chứa thông tin riêng tư khi chưa kiểm tra.

Chưa build hoặc kiểm thử trên thiết bị trong môi trường tạo bản vá.
