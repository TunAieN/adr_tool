# V2.1.69 — thử idle 1s và kiểm tra ô bình luận (không tự gửi)

Bản này tiếp tục dựa trên V2.1.68. Sau khi tìm LIVE dưới `waitForIdleTimeout=1000ms`, mã **khôi phục setting ban đầu (thường là 10000ms), xác minh kết quả** rồi kiểm tra tình trạng phòng một lần nữa trước khi gọi trình thăm dò giao diện `LiveCommentUiProbe.RunAsync` đã có trong dự án.

- Thử nghiệm làm rõ liệu khung bình luận có xuất hiện và sẵn sàng sau luồng tìm LIVE dùng idle 1 giây hay không.
- Nếu tình trạng phòng bị hạn chế, tắt tiếng, không phải LIVE hoặc không đọc được Viewer, dừng không mở khung bình luận.
- Nếu hợp lệ, mở khung nhập đúng **một lần** theo locator UI đã xác minh, kiểm tra `EditText`, `vzb`/`vxc` và lưu before/after XML + ảnh trong `diagnostics/phase5-open-ui-probe-*`.
- **Không nhập văn bản, không nhấn Send, không gọi vòng tự động PHASE5.** Nếu muốn xác minh một bình luận thực sự được đăng, người dùng có thể tự nhập và gửi thủ công trong một LIVE được phép kiểm thử.
- Không thay đổi mã `LiveCommentOneShot.cs`, `LiveCommentAutoSender.cs`, `LiveCommentUiProbe.cs`, `LiveSearchEngine.cs`, `LiveRoomFlowGuard.cs` hoặc module tài khoản.
- Không tích ô thử nghiệm: luồng V2.1.68 hiện có giữ nguyên, bao gồm những tính năng người dùng tự phát triển; phiên tự động không nằm trong phép thử này.

## Chạy thử

1. Build bằng `BUILD_CHECK_ONLY_V2161.bat`, tiếp theo là `BUILD_RUN.bat` trên Windows.
2. Tích `Thử idle 1s + kiểm tra ô bình luận (không gửi)` và nhấn `Tìm LIVE`.
3. Xem `SAFE_IDLE_VALIDATED`, `SAFE_IDLE_RESTORED`, `SAFE_IDLE_COMMENT_UI_TEST_START`, `SAFE_IDLE_COMMENT_UI_TEST_RESULT`, `SAFE_IDLE_TRIAL_COMPLETE`.
4. Nếu `INPUT_UI_VISIBLE`, thử nhập thủ công bằng điện thoại trong phòng kiểm thử được phép và xem TikTok có đăng hay không. `INPUT_UI_VISIBLE` không chứng minh việc gửi đã thành công.

## Xác minh

Đã kiểm tra cấu trúc mã nguồn bằng diff và file ZIP. Môi trường tạo gói không có .NET SDK hay thiết bị XCloudPhone nên chưa xác nhận biên dịch hay chạy thực tế.
