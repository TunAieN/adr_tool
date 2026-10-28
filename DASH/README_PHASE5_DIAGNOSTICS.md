# PHASE5 – bản sửa chẩn đoán `COMMENT_OPEN_NOT_FOUND`

Phiên bản này được chỉnh trực tiếp từ file `ANDROID_MULTI_DEVICE_LIVE_GD2_V2130_SINGLE_COMMENT_REVIEW (2).zip` do người dùng cung cấp.

## Đã sửa

- Nếu XPath `//android.widget.TextView[@resource-id="com.ss.android.ugc.trill:id/h42"]` không tìm thấy, **dừng ngay lần lỗi đầu tiên**, không thử đi thử lại trên giao diện không xác định.
- Lưu chẩn đoán **chỉ đọc**, bao gồm `page-source.xml`, `screen.png` và `diagnostic-summary.txt`, vào `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\phase5-comment-missing-<timestamp>\`.
- Trong log in `ForegroundPackage`, sự tồn tại các resource-id `h42`, `h7r`, `vxc`, `tv_online_audience_num`, và liệu cây UI có `EditText` hay không.
- Không thêm selector thay thế, không nhấn bừa, không tăng tần suất gửi, không tự vượt trạng thái hạn chế.
- Cửa sổ phân biệt số lần **nhấn nút gửi** và trạng thái phát hành đã được máy chủ chấp nhận (chưa xác minh).

## Kết luận từ log người dùng (2026-10-23 15:05–15:08)

Giai đoạn 3 có `LIVE_ACCEPTED` với Viewer=41.600; Giai đoạn 5 bắt đầu lúc 15:07:50 rồi báo `COMMENT_OPEN_NOT_FOUND` nhiều lần và bị hủy lúc 15:08:29. Không có `COMMENT_DRAFT_ENTERED`, không có nhấn nút gửi. **Số bình luận gửi = 0**.

`LiveSearchDialog.cs` khởi tạo một `AppiumSession` mới cho Giai đoạn 5. `noReset=true` nhưng code không xác minh lại màn hình LIVE ngay sau khi tạo session; đây là một giả thuyết cần kiểm chứng qua snapshot, không phải nguyên nhân đã xác định.

## Cách kiểm tra

1. Giải nén vào thư mục mới; chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK và Appium.
2. Trên phiên bị lỗi, mở thư mục diagnostics ở log `PHASE5 DIAGNOSTIC_SAVED`.
3. Gửi `diagnostic-summary.txt`, `page-source.xml` và `screen.png` sau khi ẩn thông tin nhạy cảm nếu muốn phân tích tiếp.

**Giới hạn kiểm thử:** chỉ phân tích mã và cấu trúc ZIP trong môi trường Linux hiện có; chưa chạy .NET/Windows và chưa thử thiết bị Android thực tế. Không xác nhận đã gửi bình luận thành công.
