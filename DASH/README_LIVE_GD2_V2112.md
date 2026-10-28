# ANDROID MULTI-DEVICE — LIVE SEARCH V2.1.12

Bản sửa từ V2.1.11, chỉ thay `AndroidMultiDeviceV1/Core/LiveVisualOcr.cs`.

## Vấn đề
OCR badge Viewer dạng số nguyên đôi khi bị thiếu một ký tự hoặc thêm số 0 ở vùng OCR thứ hai, khiến tất cả 4 card đều báo `OcrConflict` dù hai cách đọc khác đồng ý.

## Logic mới
- `99 / 9 / 99` → `99` (`IntegerMajorityCandidate`).
- `533 / 5330 / 533` → `533` (`IntegerMajorityCandidate`).
- `55 / (rỗng) / 5` → `55` (`PartialIntegerRecoveredCandidate`).
- `319 / 3190 / 319` → `319` (`IntegerMajorityCandidate`).
- Nếu không có đủ căn cứ: `OcrConflict`, không chọn số.
- Kiểm tra dấu chấm pixel chỉ chạy khi OCR có ký hiệu `K` hoặc `M`.
- `IconLikeIgnored`, `IconUnknown` tiếp tục loại khỏi thống kê Viewer.
- Tất cả số đọc bằng ảnh vẫn là `OcrCandidate` hoặc `OcrRepeatedCandidate`, không phải xác minh độc lập từ TikTok. Chức năng tìm LIVE vẫn **chỉ đọc**, không tự mở phòng hoặc gửi bình luận.

## Cài đặt
1. Đóng tool, sao lưu V2.1.11.
2. Với PATCH: giải nén đúng cây thư mục và ghi đè file `Core/LiveVisualOcr.cs`.
3. Chạy `BUILD_RUN.bat` trên Windows  với .NET 8 SDK, Appium và Tesseract đã cài.
4. Kiểm tra trên từ khóa `ghép đôi`; đối chiếu 4 badge thực với log.

## Giới hạn
Ảnh 352x624 rất nhỏ. Đồng thuận OCR của một ảnh hoặc hai ảnh chỉ cho ứng viên, không bảo đảm con số tuyệt đối. Cần kiểm thử Windows/XCloudPhone và tránh suy diễn trạng thái Verified. Chưa build được bằng .NET Windows trong môi trường phát triển hiện tại.
