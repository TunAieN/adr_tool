# V2.1.27 – PHASE 3 LIVE VALIDATION

Bản vá từ V2.1.26.

- Sau khi PHASE2 mở LIVE, lấy Viewer từ `tv_online_audience_num` trên XML Appium; phân tích số nguyên và đơn vị K/M.
- Viewer ngoài ngưỡng hoặc không đọc được: đóng phòng bằng XPath `@2131842704` hoặc Android Back; xác nhận lưới trước khi kết luận đã thoát.
- Viewer đạt: nhấn `k13`, đọc tên `q16`, username `user_name`, Back một lần và xác minh Viewer còn hiện trong LIVE.
- Không xác minh được username: không tự kết luận không trùng; rời phòng an toàn nếu đã về LIVE.
- Tạm kiểm tra trùng username trong cùng tiến trình ứng dụng (bản ghi hết hạn sau 10 phút). Đây KHÔNG PHẢI ID phiên LIVE và không phải kiểm tra trùng chính xác giữa nhiều máy tính hay qua khởi động lại. Cần cải tiến quản lý release/heartbeat trước khi dùng lâu dài.
- Lưu `phase3/live-entry.*`, `host-profile.*`, `live-return.*`, `after-exit.*`, `live-room.json` hoặc `result.json` trên PC.
- Giữ nguyên quét 4+2+2, OCR search card, đối chiếu ảnh, vuốt ngược và tap của V2.1.26.

## Giới hạn phiên bản
- Nếu phòng bị loại, quay về lưới nhưng chưa tự chọn ứng viên khác. Đây là bước tiếp theo cần xử lý bằng state machine nhằm tránh click nhầm khi danh sách đã đổi.
- Nếu đã mở màn hình khác nhưng Appium không thể xác minh quay lại LIVE, dừng với `PHASE3_RECHECK_REQUIRED`.
- Chưa thực thi build hay kiểm thử trên Android trong môi trường tạo gói này. Hãy chạy `BUILD_RUN.bat` trên Windows có .NET 8, Appium, thiết bị kết nối.
- Trước khi chia sẻ diagnostics, kiểm tra dữ liệu cá nhân trong ảnh/XML/JSON.
