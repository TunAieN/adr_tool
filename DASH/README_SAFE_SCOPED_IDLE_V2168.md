# V2.1.68 – Thử idle 1.000 ms có giới hạn và tự phục hồi

Mục tiêu: kiểm tra giảm độ trễ truy xuất UI Appium trong quá trình tìm kiếm và xác minh LIVE, không tác động tới logic gửi bình luận.

## Cách dùng

1. Giải nén vào thư mục mới. Chạy `BUILD_CHECK_ONLY_V2161.bat` trên Windows trước.
2. Mở cửa sổ `Tìm LIVE`. Tích `Thử idle 1s (chỉ tìm, không gửi)` nếu muốn thực hiện phép thử. **Mặc định không tích**.
3. Nhấn `Tìm LIVE` để thử. Tab tìm kiếm/grid và PHASE3 vẫn dùng logic kiểm tra hiện có. **Khi tick phép thử, PHASE5 không chạy và không gửi bình luận**.
4. Quan sát: `SAFE_IDLE_BASELINE`, `SAFE_IDLE_APPLIED`, `SAFE_IDLE_STATE_VERIFIED`, `SAFE_IDLE_ROOM_CHECK`, `SAFE_IDLE_VALIDATED`, `SAFE_IDLE_RESTORED`.

## Quy tắc an toàn

- Chỉ bật 1.000 ms trong cùng session đang chạy PHASE1–3. Không cập nhật cấu hình của máy, Appium server, tài khoản hay session khác.
- Trước khi bật: đọc setting, chỉ chấp nhận giá trị trước đó là 10.000 ms; chụp snapshot XML; bật và đọc lại setting; so sánh root, số node (±25%), tối thiểu 5 resource ID ổn định nếu màn hình có đủ.
- Trong tìm kiếm: ghi nhận bằng chứng tab LIVE có GridView và PHASE3 chấp nhận host/viewer. Sau khi PHASE3 chấp nhận, đọc 2 snapshot LIVE liên tiếp để kiểm tra LIVE, Viewer, nút chủ phòng và không có dấu hiệu hạn chế trong XML.
- **Bắt buộc khôi phục 10.000 ms và xác minh lại trước khi kết thúc thử nghiệm**, kể cả khi dừng do Cancel. Nếu không khôi phục hoặc xác minh UI không đạt, dừng luồng thay vì tiếp tục.
- **Chế độ thử nghiệm KHÔNG gọi watchdog hoặc PHASE5**; chỉ tìm và xác minh phòng. Khi không tích tùy chọn, luồng cũ (bao gồm PHASE5) không bị thay đổi.
- Không tăng tần suất gửi, không thay đổi mã `LiveCommentAutoSender.cs`, `LiveCommentOneShot.cs`, `LiveRoomFlowGuard.cs`.
- Không đảm bảo toast tắt tiếng thoáng qua luôn xuất hiện trong XML. Không suy đoán được gửi thành công chỉ từ `SEND_REQUESTED`.
- Chế độ độc lập `Theo dõi / Duyệt LIVE` và nút benchmark `Đo Appium 20×2` không bị thay đổi.

## Hạn chế

Đây là một thử nghiệm opt-in, chưa kiểm thử .NET build/UiAutomator2 trên Windows/XCloudPhone tại đây. Những trạng thái màn hình được kiểm tra không bảo đảm bao phủ mọi biến thể TikTok. Khi kiểm tra không đạt, nên tắt tùy chọn và chạy chế độ gốc.
