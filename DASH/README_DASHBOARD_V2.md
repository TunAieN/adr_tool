# Android Multi-Device Manager — Dashboard V2

Nâng cấp từ gói DeviceV1 / FAST. Chỉ sửa lớp trình bày quản lý thiết bị và 3 trường lưu lựa chọn hiển thị trong `Settings`.

## Cài đặt

- **Bản FULL (`DeviceV2.zip`):** giải nén, vào thư mục `DASH`, chạy `BUILD_RUN.bat` (yêu cầu Windows + .NET 8 SDK).
- **Bản PATCH (`PatchV2.zip`):** sao lưu thư mục source V1 hiện tại. Giải nén bản vá đè vào **thư mục gốc chứa `AndroidMultiDeviceV1`** và chạy `BUILD_RUN.bat` như cũ.
- Kho tài khoản, các tệp vault, settings cũ trong `%LOCALAPPDATA%` không được đụng chạm bởi việc giải nén bản vá; các lựa chọn hiển thị mới được ghi cùng `settings.json` khi người dùng chủ động thay đổi bố cục.

## Những điểm mới

1. Bảng thiết bị V2 có chữ lớn và chỉ hiện các cột thường dùng; cột Model, Port, ADB thấy lúc có thể bật từ nút **Cột hiển thị**.
2. Kéo mép cột để đổi độ rộng. Khi nhả chuột, bố cục cột được lưu vào `settings.json`. Có thể **Đặt lại cột mặc định**.
3. Phần dưới có hai vùng riêng: **THÔNG TIN THIẾT BỊ** và **TRẠNG THÁI VÀ NHẬT KÝ GẦN NHẤT**. Hiện đầy đủ email/runtime khi dữ liệu có sẵn, Stage/Detail, lỗi mới nhất, ADB, CloudPhone, hạn thuê và ghi chú. Nếu worker chưa cung cấp tài khoản hiện tại, hiển thị **Chưa xác định**, không suy đoán từ kho.
4. Có thể kéo đường phân cách giữa bảng và chi tiết; khoảng cách được ghi nhớ.
5. Không máy nào được tick mặc định; **tick** là lựa chọn cho thao tác hàng loạt và **chọn hàng (màu xanh)** chỉ là xem chi tiết. Start/Stop đã tick có bước xác nhận.
6. Giữ toàn bộ nút Start, Stop, Start LIVE, Kho tài khoản, Nhật ký máy, Sửa thông tin, lọc và tìm kiếm.

## Đảm bảo tương thích

- Không sửa `DeviceId`, `Serial`, `SystemPort`, chính sách phân công kho tài khoản, mã hóa vault hoặc `Storage`.
- Không sửa LoginWorkflow, TikTokStateManager, Appium, LiveSearchEngine, LiveVisualOcr hay Phase 5.
- Không có kết nối/API CloudPhone mới; ánh xạ tên phiên và hạn thuê vẫn do người dùng nhập và kiểm chứng thủ công.
- Thêm các thuộc tính tương thích ngược `DeviceDashboardColumnWidths`, `DeviceDashboardColumnVisibility`, `DeviceDashboardSplitterDistance` trong `Core/Models.cs`; settings.json cũ vẫn đọc bình thường.

## Kiểm tra

- `python TEST_DASHBOARD_V2.py`: kiểm tra tĩnh điều khiển V2 và bảo toàn kết nối MainForm.
- `python TEST_DASHBOARD_V1.py`, `python TEST_FAST_PREFLIGHT.py`, test Profile/Gallery: PASS trong môi trường đóng gói.
- `TEST_POST_GOOGLE_ONBOARDING_V2145.py` hiện có một assertion kỳ vọng dòng chú thích không tồn tại ngay trong `LoginWorkflow.cs` của **DeviceV1.zip**. File LoginWorkflow không được sửa trong V2; đây là lỗi kiểm thử kế thừa, không phải hồi quy do V2.
- Chưa chạy được `dotnet build` WinForms / thao tác GUI trên Windows trong môi trường đóng gói. Cần chạy `BUILD_RUN.bat` tại PC và thử chọn cột, tick, Start/Stop trước khi sử dụng rộng rãi.
