# V2.1.66 — Appium waitForIdleTimeout benchmark (additive)

## Mục đích

Các lệnh `SOURCE_XML` và `FIND_ELEMENT` tại V2.1.65 mất 10–13 giây/lượt. Bản này bổ sung kiểm thử an toàn đối chiếu `waitForIdleTimeout` đang áp dụng (thông thường 10000ms) với 1000ms. **Không tự động áp dụng 1000ms cho các luồng thông thường.**

## Chạy thử

1. Sao lưu thư mục V2.1.65, giải nén bản này vào một thư mục mới.
2. Kiểm tra ADB, Appium UiAutomator2 hoạt động. Chạy `BUILD_CHECK_ONLY_V2161.bat`, rồi `BUILD_RUN.bat`.
3. Chọn điện thoại rồi mở cửa sổ **Tìm LIVE**.
4. Bấm **Đo tốc độ Appium** trong hàng tiêu đề **Nhật ký tìm LIVE**; không bấm **Tìm LIVE** trong khi đo.
5. Xem `APP_IDLE_BASELINE_SAMPLE`, `APP_IDLE_CANDIDATE_SAMPLE`, `APP_IDLE_TEST_COMPARE` và `APP_IDLE_TEST_RESTORED`.

Bản thử chạy trong Appium session **riêng**. Phép đo chỉ đọc XML và tìm một FrameLayout bất kỳ, không chạm màn hình, không vuốt, không nhập chữ, không gửi bình luận, không đổi tài khoản.

Sau thử, tool khôi phục setting trước đó rồi đóng session. Nếu không có `APP_IDLE_TEST_RESTORED: ... verified=true` thì session chẩn đoán vẫn được đóng, không tiếp tục thao tác. Chế độ tìm LIVE thường không chịu ảnh hưởng.

## Đọc kết quả

- `baseline` là thời gian lệnh khi ở setting ban đầu (thường 10000ms).
- `candidate` là thời gian khi ở 1000ms.
- `sourceReductionPct` là mức giảm thời gian đọc XML (%); không phải mức tăng tốc toàn tool.
- `structurallyValid=true` chỉ xác minh cấu trúc XML và XPath cơ bản, không chứng minh tất cả màn hình nhận đúng.

Mọi hành vi của `LiveSearchEngine`, `LiveCommentOneShot`, `LiveCommentAutoSender`, `LiveRoomFlowGuard`, kho tài khoản được giữ nguyên.

## Bản có thể chậm nếu...

Nếu 1000ms vẫn cho `SOURCE_XML` trên 10 giây, nguyên nhân có thể nằm ở remote UiAutomator2, kết nối, tải thiết bị hoặc tầng WebDriver, không chỉ idle timeout.

## Chưa kiểm thử

Chưa có .NET SDK Windows hoặc XCloudPhone trong môi trường build; cần thực thi trên máy người dùng. Không cam kết tốc độ trước khi có log benchmark mới.
