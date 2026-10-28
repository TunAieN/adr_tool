# DEVICE DASHBOARD V1 — FAST base (27/10/2026)

## Cài đặt

- FULL: giải nén gói DeviceV1.zip, chạy `BUILD_RUN.bat` ở thư mục gốc `DASH`.
- PATCH: sao lưu source FAST trước; giải nén PatchV1.zip vào **thư mục gốc source FAST chứa AndroidMultiDeviceV1**, chấp nhận ghi đè file cùng tên. Chạy lại `BUILD_RUN.bat`.
- Dự án là WinForms .NET 8 trên Windows. Không sử dụng công cụ dịch vụ bên ngoài để lấy thông tin thuê.

## Cách dùng

1. Vào **THIẾT BỊ** để xem dashboard toàn chiều rộng, có thanh tìm kiếm, lọc máy, tóm tắt tình trạng ADB và worker.
2. Chọn một máy → nhấn **Sửa thông tin** (hoặc nhấp đúp dòng) để đặt alias, tên phiên CloudPhone, thời điểm hết hạn thuê, ghi chú.
3. Mapping **Phone ↔ CloudPhone là thủ công**, cần đối chiếu đúng endpoint ADB/phiên thuê trên website. Không suy ra ánh xạ từ vị trí danh sách.
4. Hạn thuê là dữ liệu tự nhập, không tự đồng bộ/gia hạn. Lọc **Sắp hết hạn** áp dụng cho thiết bị có hạn đã nhập và còn <=24 giờ, bao gồm đã hết hạn.
5. Tick các máy rồi chọn **Start đã tick**: vẫn dùng luồng `StartSelectedDevicesAsync` cũ (tài khoản tick từ kho xử lý theo quy tắc cũ); Stop đã tick có bước xác nhận.
6. Nút **Kho tài khoản** và **Nhật ký máy** chuyển về panel cũ, chọn đúng thiết bị; điều khiển/worker cũ không thay đổi.

## Bảo toàn dữ liệu & phạm vi

- `DeviceId` và `Serial` không đổi khi chỉnh alias/tên CloudPhone.
- Kho tài khoản vẫn được mã hóa trong `account-vault.json` và phân theo ADB Serial như trước.
- Metadata quản lý lưu trong `settings.json` hiện tại. Các trường mới (nullable/empty) tương thích cấu hình cũ.
- Không tác động tới `LoginWorkflow`, `TikTokStateManager`, `LiveSearchEngine`, OCR, hay Phase 5.
- `LastSeenAt` chỉ được cập nhật khi Refresh ADB xác nhận ONLINE; không phải heartbeat nền tự động và không thay thế kiểm tra kết nối thực tế.
- Không thay cấu hình maxParallel, không tạo Appium session khi vẽ dashboard; refresh giao diện mỗi 5 giây từ các trạng thái sẵn có.

## Xác minh

- Đã chạy kiểm thử FAST idle/regression và test profile/gallery tĩnh. Gói được kiểm tra cấu trúc.
- Không có .NET 8 Windows SDK/Appium trong môi trường đóng gói. Chạy build và kiểm thử worker cùng lúc trên PC trước khi đưa vào sử dụng chính thức.
