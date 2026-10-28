# V2.1.18 – Điều chỉnh tự do độ kéo LIVE Search

- Cài đặt độ kéo thay từ 15–40% thành **1–75%** chiều cao màn hình. Nhập trực tiếp trong hộp số, không ép về 30% khi nạp giá trị dưới 15%.
- Appium `dragGesture` dùng khoảng cách tỷ lệ thực (làm tròn pixel), không còn chặn ngầm ở 42% chiều cao màn hình.
- Điểm bắt đầu cử chỉ ở khoảng 87% chiều cao, kết thúc thấp nhất khoảng 12%; nhằm tránh kéo từ/thẳng vào vùng thanh trạng thái trên cùng.
- Giữ nguyên thuật toán nhận diện NEW/SEEN/PENDING, timeout 240s/600s và cấu hình khác của V2.1.17.
- Các mức kéo lớn có thể bỏ qua một hàng LIVE. Thử lần lượt 25, 40, 50, 60%, quan sát các log NEW/SEEN/PENDING và độ dịch thực tế.
- Chưa build/kiểm thử thiết bị Android trong môi trường tạo gói; chạy BUILD_RUN.bat trên Windows với .NET 8 SDK.
