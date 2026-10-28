# V2.1.22 – Overlap tracking + per-scroll captures

- Nhận diện cuộn lưới 2 cột x 2 hàng: khi có bằng chứng hai card hàng dưới của màn trước trùng hai card hàng trên của màn sau, đối chiếu hai card còn lại như ứng viên mới. Không dựa vào số Viewer để xác nhận danh tính.
- Khi không đủ bằng chứng, giữ PENDING thay vì coi mặc định card là phòng khác. Không khẳng định cứ kéo là xuất hiện đúng hai phòng.
- Lưu mỗi viewport: `viewport-01.png`, `.xml`, `.json` (thứ tự slot, track ID, NEW/SEEN/PENDING, Viewer và tên phòng). Ảnh được chụp ngay khi đọc viewport; ảnh N là trước kéo lần N, ảnh N+1 là sau kéo đó.
- Nơi lưu: `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\live-scroll-YYYYMMDD-HHMMSS-...` trên Windows. Ảnh có thể chứa dữ liệu riêng tư; xem lại trước khi gửi.
- Khi đã đạt cấu hình 8 card duy nhất được xác nhận sẽ dừng ngay; không chờ timeout. Timeout chỉ dùng khi chưa đủ.
- Đã bảo toàn cấu hình kéo 1–75% và dữ liệu khác của bản gốc.

## Cách kiểm thử
Chạy `BUILD_RUN.bat` trên Windows có .NET SDK, Appium, Android. Sau lượt thử, nếu còn lỗi hãy gửi log V2.1.22 cùng thư mục `live-scroll-*` tương ứng (có thể nén ZIP). Việc lưu ảnh thực tế yêu cầu quyền ghi thư mục AppData; nếu thất bại log sẽ báo lỗi.

**Trạng thái:** Chưa build hay chạy thử với Android trong môi trường đóng gói hiện tại (không có .NET SDK/Appium). Cần kiểm thử thực tế trước khi kết luận giải quyết lỗi nhận diện.
