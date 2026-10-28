# V2.1.67 – Benchmark Appium ABBA (chỉ chẩn đoán)

## Mục tiêu

Bản V2.1.66 thử 2 lượt cho mỗi giá trị `waitForIdleTimeout`, nên lần đầu tải/chờ session có thể làm sai kết quả trung bình. Bản V2.1.67 thay bằng **20 mẫu cho 10.000 ms và 20 mẫu cho 1.000 ms**, cùng trên một Appium session thử nghiệm độc lập, theo thứ tự **A → B → B → A** lặp 10 khối. Trước khi lấy mẫu chính thức, chạy **2 lượt warmup mỗi cấu hình** (không tính vào thống kê). Kết thúc khôi phục **giá trị gốc** đã đọc khi bắt đầu.

## Cách dùng

1. Mở thư mục phiên bản riêng, build bằng `BUILD_CHECK_ONLY_V2161.bat`.
2. Mở ứng dụng, chọn điện thoại, mở cửa sổ **Tìm LIVE**.
3. Cho màn hình điện thoại hiển thị trạng thái ổn định (ví dụ trang chủ hoặc một phòng LIVE đang xem), không điều khiển máy thủ công khi đang đo.
4. Nhấn **Đo Appium 20×2**. Phép đo không thực hiện tap, swipe, gõ chữ, gửi bình luận, đăng nhập/đăng xuất.
5. Xem `APP_IDLE_WARMUP`, `APP_IDLE_SAMPLE`, `APP_IDLE_TEST_COMPARE`, `APP_IDLE_TEST_RESTORED`, `APP_IDLE_TEST_REPORT_SAVED`.

Bản ghi JSON mới ở `C:\Users\<Windows user>\AppData\Local\AndroidMultiDeviceV1\diagnostics\app-idle-abba-YYYYMMDD-HHMMSS.json` (thư mục gốc lấy từ `Storage.Folder`). Nếu hủy hoặc gặp lỗi, tool vẫn cố khôi phục giá trị cũ và ghi báo cáo dở dang với trạng thái `completed=false`; session thử được đóng bởi caller.

## Đọc báo cáo

- `sourceMedianMs`, `sourceP95Ms`: thời gian đọc XML trung vị và phân vị 95 (nearest-rank).
- `findMedianMs`, `findP95Ms`: thời gian tìm phần tử trung vị và phân vị 95.
- `nodesMin`, `nodesMax`: số node trong XML tại thời điểm đo.
- `structurallyValid`: kiểm tra root hierarchy, tìm FrameLayout, số node tối thiểu, sự thay đổi số node trung vị không vượt 20%, và những selector quan trọng thường xuyên có mặt trong baseline không biến mất khi thử candidate.
- **Giới hạn:** Những kiểm tra cấu trúc này chỉ chứng minh tính nhất quán của màn hình được lấy mẫu, **không chứng minh** mọi màn hình TikTok sẽ hoạt động đúng ở giá trị timeout mới.
- `sourceReductionMedianPct`: thay đổi của median, không phải tốc độ của toàn tool.
- `keepChange=false`: phép thử **không tự đổi** timeout trong phiên chạy thường.

Báo cáo `perf-appium-*.json` cũ cũng được bổ sung các trường `medianMs`, `p95Ms`, `latencySampleCount` và `percentileWindowIsTruncated`. Phân vị được tính trên tối đa 8.192 phép đo gần nhất mỗi loại lệnh. Các trường cũ (`count`, `averageMs`, `maxMs`, `totalMs`, `errors`, `slow`) được giữ nguyên.

## Giới hạn và an toàn

- Các lệnh trên một session được thực thi **tuần tự**.
- Không ghi XML đầy đủ, nội dung video, tên chủ phòng, nội dung tin nhắn, tài khoản, mật khẩu hoặc serial thiết bị vào báo cáo benchmark.
- Không áp dụng 1.000 ms cho mọi thiết bị hoặc mọi màn hình. Cần kiểm thử nhiều giao diện và đánh giá độ ổn định thực tế.
- Bộ `LiveRoomFlowGuard` đã tái sử dụng **cùng một snapshot xác minh** trong một checkpoint từ bản V2.1.64. Bản này **không cache XML giữa các lần kiểm tra độc lập**, vì làm vậy có thể bỏ lỡ việc TikTok tự đổi phòng hoặc thông báo hạn chế.
- Không sửa `LiveCommentOneShot.cs`, `LiveCommentAutoSender.cs`, `LiveSearchEngine.cs`, `LiveRoomFlowGuard.cs` hoặc kho tài khoản. Không tăng tần suất gửi bình luận.
- Chưa build trực tiếp với .NET 8 Windows hoặc chạy trên XCloudPhone trong môi trường tạo bản này. Hãy build và thử nút benchmark trên máy thật.
