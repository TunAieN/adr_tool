# ANDROID MULTI-DEVICE V1.6.0.3 — SMART UI READY WAIT

## Khắc phục

Log cũ:
- `Không xác minh được Profile; khởi động lại TikTok một lần`
- `Preflight: Unknown - Không click được tab Profile (kiểm tra XPath profile)`

Nguyên nhân: Appium có thể trả về `currentPackage=com.ss.android.ugc.trill` trong khi ứng dụng chưa tải xong giao diện. V1.6.0.2 xác nhận package quá sớm rồi click Profile.

## Thay đổi

1. Bổ sung `TikTokForegroundProbe.SourceHasReadyTikTokUi()` và `WaitForReadyUiAsync()`.
   - Đợi các phần tử TikTok Profile/Login thực tế xuất hiện trong UI hierarchy, **hai lần kiểm tra liên tiếp**, tối đa 90 giây khi vừa mở/restart/logout.
   - KHÔNG coi foreground package đơn thuần là UI đã tải xong.
2. `TikTokStateDetector.DetectAsync()`:
   - Đợi UI TikTok sẵn sàng tối đa 75 giây ngay cả khi ứng dụng đã ở foreground.
   - Đọc `Profile` trong các phần tử đã hiển thị rồi mới nhấn; có giới hạn 45 giây để tìm và nhấn tab Profile.
   - Sau nhấn, đợi tối đa 40 giây để đọc username trên trang Profile.
3. `AppiumSession.ClickVisibleAnyAsync()` dùng API WebDriver /displayed trước khi click. Tránh nhấn nút đã có trong XML nhưng đang ẩn.
4. `DeviceOrchestrator`: chờ UI sẵn sàng khi mở TikTok để chẩn đoán và chạy worker. Mỗi máy có luồng chờ độc lập và hỗ trợ Stop (CancellationToken).
5. `LoginWorkflow.LogoutAsync`: sau logout cũng chờ UI TikTok sẵn sàng trước khi điều hướng Profile.

## Cài đặt

- Dựa trên FULL V1.6.0.2 / bản patch scroll logout gần nhất.
- Thoát chương trình và sao lưu dự án, dữ liệu `%LOCALAPPDATA%\\AndroidMultiDeviceV1`.
- Giải nén PATCH vào thư mục dự án, ghi đè 4 file C# được nêu trong ZIP.
- Chạy `BUILD_RUN.bat`.
- Appium Server phải đang chạy, Remote ADB phải ONLINE.
- Thử duy nhất Phone 01 trước.

## Kết quả mong đợi

- Khi mở lại TikTok, log cho biết đang chờ UI tối đa 90 giây.
- Khi giao diện đã có nút Profile hoặc login, tool mới click/đối chiếu username.
- Nếu vẫn không hiện UI đúng thời hạn: ghi lỗi kỹ thuật có thể thử lại; KHÔNG kết luận tài khoản bị ban.
- Không thay đổi vault, Excel, cài đặt Profile Update, logout, hoặc danh sách selector.

## Giới hạn

Các thời gian 75/90/45/40 giây là **giới hạn tối đa**, không phải lệnh ngủ cố định; khi UI xuất hiện, tool tiếp tục sớm hơn.
Chưa thể biên dịch trực tiếp trong môi trường không có Windows/.NET SDK, và chưa kiểm thử trên thiết bị XCloudPhone thật.
