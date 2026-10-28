# V2.1.34 – Test nhập và gửi một bình luận

## Sửa đổi

- Tiếp nối V2.1.33, dựa trên log thực tế `INPUT_UI_VISIBLE; h7r=True; visibleEditText=True; vxc=True`.
- Sau Giai đoạn 1–3 được `LIVE_ACCEPTED`, nút **Test nhập và gửi 1 câu** được bật.
- Mỗi lượt tìm LIVE chỉ có **một lần** người dùng chủ động kích hoạt nút; không tự gửi theo lịch, không lặp, không chuyển phòng, không phát qua nhiều thiết bị.
- Tool tự chọn đúng một câu từ `CommentDrafts` trong cấu hình LIVE, `Sequential` lấy dòng đầu, `Random`/`Shuffle` chọn ngẫu nhiên một dòng. Ghi rõ câu đã chọn vào nhật ký.
- Phân tích XML để mở thanh `Type...`: ưu tiên `h42`; nếu không có, tìm chữ `Type...`; nhấn đúng vùng được xác định từ XML với cha clickable `ilz`.
- Sau đó tìm `EditText` đang focus rồi dự phòng qua `h7r`, gửi thao tác nhập text qua Appium, **đọc lại XML xác nhận văn bản khớp 100%** trước khi gửi.
- Chỉ khi nút `vxc` hiện ra và có tọa độ hợp lệ trong XML vừa đọc, tool tap **một lần** tại tâm nút gửi. Không tự retry nếu lệnh tap lỗi hoặc timeout.
- Lưu `before.xml`, `opened.xml`, `entered.xml`, `after.xml` (nếu đọc được) và `diagnostic-summary.txt` ở `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\phase5-one-shot-*`.
- Nhật ký đánh dấu `sendClicks=1` chỉ khi Appium xác nhận lệnh tap đã hoàn thành; đây **không phải xác nhận máy chủ đã tiếp nhận bình luận** (`delivery=UNVERIFIED`).
- Giai đoạn 1–3 không thay đổi.

## Cách thử

1. Giải nén toàn bộ ZIP vào thư mục mới rồi chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK.
2. Vào Cấu hình LIVE, kiểm tra thư viện bình luận (`CommentDrafts`) đã có nội dung mong muốn.
3. Nhấn `Tìm LIVE` để đạt `LIVE_ACCEPTED`.
4. Nhấn `Test nhập và gửi 1 câu` một lần. Lúc này thao tác gửi được yêu cầu ngay, **không hiện thêm hộp thoại xác nhận**.
5. Kiểm tra log `DRAFT_SELECTED`, `COMMENT_TEXT_VERIFY`, `COMMENT_SEND_REQUESTED` và `COMPLETE`.

## Phạm vi kiểm thử

Bản này gửi **tối đa một yêu cầu gửi** theo thao tác kích hoạt của người dùng. Không có động cơ lặp/gửi theo chu kỳ hoặc tự đăng qua các phòng khác. Không tự vượt qua giới hạn của ứng dụng.

## Trạng thái

Đã thay đổi mã nguồn và kiểm tra cấu trúc ZIP; **chưa build Windows hoặc kiểm thử thực tế trên Android** trong môi trường tạo bản vá.
