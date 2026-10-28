# TikTok LIVE Search V2.1.4 — Cuộn ngắn, đọc từng card

## Vấn đề V2.1.3
- Mỗi lượt vuốt thường chỉ cách nhau khoảng 4 giây; việc đọc card trong log bị gộp thành một bước.
- Tool có thể chuyển màn hình khi ảnh/video và nhãn card còn đang tải.
- Khi card không có tiêu đề, Creator hoặc Viewer trong XML, V2.1.3 dựa vào vị trí ô trên màn hình để tạo ID; việc đếm card mới có thể sai.

## Điều chỉnh
- Vuốt ngắn trong vùng kết quả LIVE, mặc định **24%** một vùng scroll an toàn; không vuốt giữa các phòng LIVE.
- Chờ **3.600 ms** sau mỗi lượt vuốt (chỉnh được trong Cấu hình LIVE → Nguồn LIVE).
- Đọc ít nhất **3 snapshot UI cùng cấu trúc** cách nhau 1.500 ms trước khi xử lý. Số người xem đổi trong khi LIVE không làm reset bộ nhận diện ổn định.
- Log **từng card** đang hiển thị rồi chờ mặc định **650 ms/card** trước khi vuốt tiếp.
- Ô card không có thông tin định danh: không coi tọa độ được lặp lại qua lần cuộn là bằng chứng xuất hiện một LIVE mới.
- Viewer chưa có bằng chứng trong XML vẫn là **chưa xác định**, không tự đoán số qua ảnh.

## Cách cập nhật
Đóng chương trình. Sao lưu thư mục dự án và kho tài khoản. Nếu dùng bản PATCH, chép bốn file sau vào nguồn V2.1.3 và ghi đè đúng đường dẫn:
- `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs`
- `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`
- `AndroidMultiDeviceV1/Core/LiveManagerSettings.cs`
- `AndroidMultiDeviceV1/LiveSettingsDialog.cs`

Chạy `BUILD_RUN.bat`, mở `Cấu hình LIVE → Nguồn LIVE` để chỉnh tốc độ.

## Nhịp vận hành kỳ vọng
1. Search → tab LIVE → đợi nguồn hiển thị.
2. Đọc 3 lần ổn định; log `đọc card 1/4`, `đọc card 2/4`, ...
3. Lưu các card đọc được và đánh giá nhóm tối đa 8 card.
4. Nếu chưa đủ điều kiện, **vuốt ngắn 24% đúng một lần**, chờ 3600ms rồi quay lại bước 2.
5. Hết điều kiện/budget tìm mới chuyển từ khóa.

## Giới hạn kiểm thử
Bản vá chỉ tìm nguồn, không vào LIVE hoặc gửi bình luận. Nếu Page Source không chứa Viewer, phần đọc Viewer vẫn báo chưa xác định; cần XML thực tế (sau khi xóa dữ liệu cá nhân) để sửa bộ đọc. Mã nguồn chưa được biên dịch trên Windows hoặc kiểm thử trực tiếp với XCloudPhone trong môi trường tạo bản vá.
