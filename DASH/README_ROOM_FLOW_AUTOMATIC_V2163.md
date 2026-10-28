# V2.1.63 – Room watchdog tự động trong luồng Tìm LIVE

## Mục đích
Ngay sau PHASE3 `LIVE_ACCEPTED`, hệ thống khởi tạo lớp giám sát phòng dùng **cùng Appium session**, không cần nhấn chế độ Theo dõi / Duyệt LIVE. Bản trước chỉ theo dõi khi nhấn nút riêng nên PHASE5 không nhận sự kiện thay đổi phòng.

## Phạm vi thêm
- Preflight xác minh LIVE hiện tại, Viewer và username; bắt đầu đồng hồ phòng từ 0.
- Trước mỗi lần thực hiện hành động đã có, đọc trạng thái UI: mute/chặn có thông báo rõ, mất phòng, lỗi màn hình hoặc Viewer rời ngưỡng ở hai lần đọc -> tạm dừng hành động trong phòng chưa xác minh.
- Mỗi 60 giây xác minh lại username qua bảng hồ sơ và Viewer. Nếu khác chủ phòng, đọc xác nhận hai lần, reset đồng hồ theo phòng **mới** rồi tạm dừng nhánh hành động của phòng trước. Không chuyển việc đăng bình luận tự động sang phòng mới.
- Phần duyệt LIVE độc lập và luật 10 phòng không đạt giữ nguyên; không chạy đồng thời để tránh tranh chấp Appium.
- Không thay đổi `LiveCommentOneShot.cs`, các XPath comment, bộ OCR, Search, PHASE2, PHASE3, cấu hình và kho tài khoản.
- `LiveCommentAutoSender.cs` chỉ được thêm tham số callback tùy chọn cuối cùng (mặc định `null`) và điểm gọi trước vòng hành động; logic cũ không thay đổi khi không gắn guard.

## Log
ROOM_FLOW_WATCHDOG_AUTO_START, ROOM_FLOW_TIMER_RESET, ROOM_FLOW_PERIODIC_CHECK_START, ROOM_FLOW_PERIODIC_CHECK_RESULT, ROOM_FLOW_AUTO_CHANGE_CONFIRMED, ROOM_FLOW_RESTRICTION_CONFIRMED, PHASE5 ROOM_GUARD_STOP.

## Giới hạn
- Không bảo đảm nhận được toast tồn tại rất ngắn nếu thông báo không lộ ra trong XML; cần ảnh chẩn đoán ngay sau thông báo để đánh giá bổ sung.
- Chưa theo dõi đồng thời liên tục độc lập bởi vì dùng Appium session chung tuần tự; trạng thái được kiểm tra giữa các hành động và mỗi 60 giây xác minh danh tính.
- Không triển khai tự động tiếp tục bình luận hàng loạt sau khi bị mute/chặn hoặc sau khi chuyển phòng.
- Chưa build .NET/Windows hay kiểm thử XCloudPhone trên môi trường này.
