# V1.6.0.7 — Chọn tài khoản bằng checkbox

Bản này được xây dựng từ **ANDROID_MULTI_DEVICE_V1_PROFILE_NAME_STATE_V1606_FULL.zip** và chỉ thay đổi `AndroidMultiDeviceV1/MainForm.cs`.

## Thay đổi

- Thêm cột **Chọn** (checkbox) nằm đầu bảng **KHO TÀI KHOẢN**.
- Checkbox **Tất cả** ở đầu cột: chọn/bỏ chọn tất cả tài khoản đang hiển thị; có trạng thái chọn một phần.
- Thêm nút **Chọn tất cả**, **Bỏ chọn** trên thanh công cụ.
- Chuyển các thao tác **Gán vào máy / Gỡ khỏi máy / Xóa khỏi kho / Đặt lại** sang sử dụng các tài khoản được tick.
- **TikTok handle** yêu cầu tick đúng một tài khoản.
- Việc tô xanh dòng chỉ nhằm xem thông tin; không tự động trở thành lựa chọn thực thi.
- Duy trì lựa chọn theo Account ID khi bảng tự làm mới (5 giây); tự bỏ chọn những tài khoản đã bị xóa khỏi kho.
- Giữ nguyên cơ chế khóa worker và xác nhận trước khi gỡ/xóa của V1.6.0.6. Nếu trong nhóm được tick có tài khoản đang hoạt động, thao tác xóa bị chặn cho cả nhóm như trước.

## Cài bản vá

1. **Đóng tool** và sao lưu thư mục mã nguồn đang sử dụng.
2. Giải nén `ANDROID_MULTI_DEVICE_V1_CHECKBOX_V1607_PATCH.zip` vào **thư mục gốc dự án V1.6.0.6**, ghi đè `AndroidMultiDeviceV1/MainForm.cs`.
3. Chạy `BUILD_RUN.bat`.
4. Trong bảng **KHO TÀI KHOẢN**, tick vào cột đầu tiên rồi thử các nút Gán/Gỡ/Đặt lại theo trường hợp phù hợp.

## Lưu ý

- Bản vá chỉ thay đổi phần giao diện/chọn tài khoản; không thay đổi file kho mã hóa và không yêu cầu nhập lại dữ liệu.
- Trạng thái checkbox là tạm thời trong phiên mở ứng dụng, không lưu vào kho mã hóa.
- Không có .NET SDK Windows trong môi trường tạo bản vá, nên mã nguồn chưa được biên dịch và kiểm thử trực tiếp trên máy Windows của anh. Nếu có lỗi biên dịch, gửi nguyên văn log.
- Trạng thái `ADB: UNKNOWN`, `Online: 0` trong ảnh người dùng gửi là vấn đề kết nối thiết bị độc lập với tính năng checkbox. Cần kết nối/quét lại ADB trước khi chạy điện thoại.
