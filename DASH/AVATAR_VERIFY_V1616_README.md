# V1.6.1.6 — Xác minh avatar sau Save & post (read-only)

## Đã sửa
- Xác định nguyên nhân: điểm avatar 0.822 thấp hơn ngưỡng 0.88, không phải thiếu số lần quan sát như nhật ký cũ nói.
- Ảnh avatar tròn thường khác thumbnail vuông. Đưa vào hai mức xác minh CHỈ khi đã có trạng thái SAVE_PENDING:
  - >= 0.88 qua 2 ảnh chụp liên tiếp; hoặc
  - >= 0.80 qua 3 ảnh chụp liên tiếp có độ dao động <= 0.05.
- Mỗi lượt log điểm, có tìm được nút avatar không, số lượt khớp liên tiếp.
- Không thực hiện Save & post lại nếu đã ghi SAVE_PENDING. Nếu chưa đủ bằng chứng, tiếp tục giữ trạng thái để đối chiếu sau.
- Các mục tên, bio, checkbox, điều khiển nhiều máy, kho tài khoản không thay đổi.

## Cài đặt
1. Đóng tool, sao lưu thư mục mã nguồn và dữ liệu `%LOCALAPPDATA%\AndroidMultiDeviceV1` (đặc biệt `profile-update-records.json`).
2. Giải nén PATCH vào thư mục gốc dự án V1.6.1.5, ghi đè `AndroidMultiDeviceV1\Core\ProfileUpdates.cs`.
3. Chạy `BUILD_RUN.bat`.
4. Bắt đầu lại phiên Phone 01 bình thường, KHÔNG xóa lịch sử hồ sơ và KHÔNG xóa `SAVE_PENDING`. Theo dõi các dòng `Ảnh: đọc avatar 1/7...`.

## Giới hạn
- Đây là đối chiếu ảnh hiển thị, không phải biên nhận từ máy chủ TikTok. Mức 0.80 chỉ được dùng với 3 quan sát liên tiếp ổn định, vì ảnh avatar được cắt hình tròn.
- Ngưỡng chưa được hiệu chuẩn với ảnh thật từ XCloudPhone. Nếu các ảnh khác nhau nhưng giống nhau về màu/bố cục, có khả năng nhận nhầm; cần xác minh thêm qua ảnh chụp Profile.
- Chưa có .NET SDK/thiết bị Windows XCloudPhone để compile/chạy end-to-end tại môi trường này.
