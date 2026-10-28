# V2.1.70 – Khôi phục chuyển pha gốc sau thử nghiệm idle 1s

## Vấn đề đã sửa

Ở V2.1.68/V2.1.69, checkbox thử idle 1s tạo một nhánh kết thúc sớm `PHASE5_SKIPPED=true`. Khi người dùng chọn thử idle 1s, tìm LIVE và xác minh phòng thành công nhưng không đi tiếp sang khối nghiệp vụ PHASE5 đã có. Đây là thay đổi không đúng yêu cầu giữ nguyên luồng của chủ dự án.

## Thay đổi tối thiểu

- Chỉ chỉnh `AndroidMultiDeviceV1/LiveSearchDialog.cs` để xóa đường rẽ chặn và sửa nhãn checkbox cho đúng hành vi; giữ nguyên các lõi `LiveCommentAutoSender.cs`, `LiveCommentOneShot.cs`, `LiveRoomFlowGuard.cs`, `LiveSearchEngine.cs`.
- Không thay đổi mã nguồn, logic hay hành vi của bộ gửi bình luận đã có, và không thêm chức năng gửi mới.
- Khi chọn idle 1s: thử nghiệm dùng idle 1000ms **chỉ trong PHASE1–3**; sau khi room đạt các kiểm tra và khôi phục idle về giá trị gốc với kết quả đọc lại đúng, chạy tiếp vào **đúng khối chuyển pha cũ**.
- Khi không chọn idle 1s: tiến trình tìm LIVE → xác minh phòng → PHASE5 vẫn giữ nguyên như trước.
- Nếu room chưa được xác nhận, XML sai, hay khôi phục setting thất bại: không chuyển pha; xử lý lỗi và đóng session.
- `LiveCommentUiProbe.cs` còn nguyên tệp của phiên trước nhưng nhánh chẩn đoán tự động của checkbox không còn gọi nó; không sửa/không xóa mã của chủ dự án.

## Log mới

- `SAFE_IDLE_VALIDATED` xác minh đầy đủ phòng qua hai lượt kiểm tra.
- `SAFE_IDLE_RESTORED` xác minh đã đưa idle về giá trị ban đầu.
- `SAFE_IDLE_FLOW_HANDOFF` báo chuyển tiếp về đường nghiệp vụ có sẵn.
- `ROOM_FLOW_WATCHDOG_AUTO_START` và `PHASE5 AUTO_START` là log của đường nghiệp vụ cũ.

## Kiểm thử

1. Giải nén vào thư mục mới, chạy `BUILD_CHECK_ONLY_V2161.bat` trên Windows.
2. Nếu build thành công, mở `Tìm LIVE` và chọn thử idle 1s, không cần dùng nút benchmark.
3. Xác nhận: `SAFE_IDLE_APPLIED` → `PHASE3 LIVE_ACCEPTED` → `SAFE_IDLE_VALIDATED` → `SAFE_IDLE_RESTORED` → `SAFE_IDLE_FLOW_HANDOFF` → `ROOM_FLOW_WATCHDOG_AUTO_START` → `PHASE5 AUTO_START`.
4. Không có `PHASE5_SKIPPED` hoặc đường kiểm tra ô nhập tách riêng từ checkbox này.

**Lưu ý:** Gói ZIP chỉ được rà soát mã và kiểm tra toàn vẹn; môi trường đóng gói hiện không có .NET SDK, không xác nhận build Windows hay hoạt động thật trên XCloudPhone. `SEND_REQUESTED` vẫn là trạng thái chưa xác minh đăng thành công.
