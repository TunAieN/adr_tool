# ANDROID MULTI-DEVICE — LIVE SEARCH V2.1.10

Bản vá đọc Viewer từ ảnh. Cài trên dự án V2.1.9 đang hoạt động.

## Thay đổi

1. Khi các phương án OCR cùng ảnh bất đồng do mất dấu thập phân: `14K+` / `1.4K+`, ghi **1.400 (OcrCandidate)**, quy tắc `DecimalDotRecoveredCandidate`. Không coi là Verified.
2. Khi mất chữ số `1` đầu: `71` / `171`, ghi **171 (OcrCandidate)**, quy tắc `LeadingOneRecoveredCandidate`, nếu đúng mẫu thiếu ký tự đầu. Không coi là Verified.
3. Với kết quả giống nhau: vẫn chỉ là OcrCandidate; các ảnh tạo ra từ cùng screenshot không phải bằng chứng độc lập.
4. Giữ phân biệt biểu tượng Person/Like từ V2.1.9: Like và Unknown không được tính vào Viewer.
5. Khi bộ quét tạm dừng vì thiếu xác minh, hiển thị các card OCR tạm tính trong bảng để người dùng đối chiếu thay vì dừng trước khi đẩy kết quả lên giao diện.
6. Giữ nguyên logic đăng nhập Google, kho tài khoản, Multi-Device, cấu hình và mức kéo ngắn hiện hành. KHÔNG thêm chức năng gửi bình luận hay mở phòng tự động.

## Cài

- Đóng chương trình, sao lưu dự án V2.1.9.
- Giải nén PATCH ngay vào thư mục gốc dự án (nơi có BUILD_RUN.bat), ghi đè theo đường dẫn.
- Chạy BUILD_RUN.bat.
- Nếu dùng FULL, giải nén sang thư mục mới để tránh thay đổi dữ liệu ứng dụng gốc.

## Kiểm thử với ảnh chẩn đoán người dùng

| Card | Badge | OCR trước sửa | Kết quả dự kiến |
|---|---|---|---|
| 1 | Person | 980 / 980 | 980 OcrCandidate |
| 2 | Person | 71 / m / 171 | 171 OcrCandidate |
| 3 | Person | 14K+ / 14K+ / 1.4K+ | 1.400 OcrCandidate |
| 4 | Person | 664 / 664 / 664 | 664 OcrCandidate |

Không đủ bằng chứng: giữ trạng thái OcrConflict, không chọn số đại.

## Giới hạn

- Chưa biên dịch trên Windows, chưa kiểm thử Appium/XCloudPhone. Cần chạy BUILD_RUN.bat và thử Phone 01.
- OCR một ảnh không đủ làm bằng chứng Viewer chính xác để tự chọn phòng; kết quả là ước lượng dựa trên hình ảnh.
- Bộ đọc chưa tự cuộn thêm khi các card chưa được xác minh. Bản vá này ưu tiên dữ liệu đúng và hiển thị được trên bảng.
