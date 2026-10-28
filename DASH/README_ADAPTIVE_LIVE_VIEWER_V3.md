# V2170 — ADAPTIVE LIVE VIEWER OCR V3
## Mục tiêu
Sửa nguyên nhân Photo 03/04 bị `IconUnknown` khi Person icon rộng 16px,
trong khi Phone 02 đọc được Person 15px. Không hạ ngưỡng OCR cố định.

## Source thay đổi
`AndroidMultiDeviceV1/Core/LiveVisualOcr.cs` (duy nhất)
- Badge Locator trước đó (bản V2170_LIVE_BADGE_OCR_FIX) vẫn được giữ.
- Căn chỉnh động vùng Person/Like quanh badge đã phát hiện theo kích thước icon
  thực tế trên screenshot, tối đa 10 phép thử có giới hạn trên mỗi card.
- Chỉ xác nhận Person nếu cùng vị trí căn chỉnh vượt ngưỡng Person>=0.72,
  Person-Like>=0.12 VÀ có hình dạng đầu-vai tách biệt.
- Nhận diện Like độc lập; khi Like và Person đều có bằng chứng -> UNKNOWN,
  không ép đọc số Like thành Viewer.
- Bổ sung `iconAlign` / `iconRect` trong log để tìm nguyên nhân trên máy mới.
- Không đổi module tài khoản, đăng nhập, tương tác bình luận, lịch sử, cấu hình
  nhiều thiết bị, vòng quét 4+2+2, tìm lại card hay Phase 2/3.
- Không đổi phần suy luận OCR dấu chấm / K / M trước đây (bao gồm xử lý xung
  đột hai ảnh, không suy diễn số khi thiếu bằng chứng).

## Kiểm thử ngoại tuyến
Kịch bản: 3 ZIP chẩn đoán live-scroll-20261026-175016-1,
175018-1, 175101-1 với 8 mẫu Person mỗi thiết bị, tổng 24 mẫu.
- PASS: 24/24 Person được nhận diện sau căn chỉnh.
- PASS: 0/120 ảnh Like *mô phỏng* bị coi là Person.
- Chú ý: các Like ở bộ này được vẽ dựa trên hai template Like hiện có,
  KHÔNG phải mẫu Like thật lấy từ TikTok; chưa đủ chứng minh zero false positive.
- Các phép thử chạy offline bằng Python dùng quy tắc hình học tương đương C#;
  chưa chạy source thực tế trên .NET/Windows/Appium/Tesseract.
Dữ liệu chi tiết ở `QA_adaptive_icon_replay.json`.

## Cài đặt
1. Sao lưu source/bản build đang chạy.
2. FULL SOURCE: giải nén nguyên ZIP vào thư mục mới, chạy `BUILD_RUN.bat`
   bằng Windows .NET 8. PATCH ONLY: sao lưu và thay
   `AndroidMultiDeviceV1/Core/LiveVisualOcr.cs` của phiên bản V2170 badge fix,
   rồi xóa cache build nếu cần và build lại.
3. Kiểm thử từng Phone 02, 03, 04 với một từ khóa. Theo dõi
   `icon=Person`, `iconAlign=adaptive-*`, `PHASE2 TARGET_FOUND`, `TARGET_OPENED`.
4. Kiểm tra ở Phone 02 hoạt động không hồi quy; sau đó thêm Phone 03 và 04.
5. Nếu máy mới có layout không hỗ trợ, không ép mở card: gửi ZIP `live-scroll-*`
   có viewport-*.png/.json/.xml và FAILED-icon-unknown để bổ sung mẫu.
6. Trước khi tự động chạy hàng loạt, thu thập badge Like thật và thử nghiệm
   thủ công trên nhiều kích thước màn hình, phiên bản TikTok khác nhau.

## Giới hạn / an toàn
Đây là bản triển khai mã nguồn và phép thử offline, chưa phải xác nhận
build/chạy thực tế. Mặc định UNKNOWN khi icon không đủ bằng chứng.
Không thể đảm bảo 100% thiết bị/phát hành TikTok tương lai mà không có QA.
