# ANDROID MULTI-DEVICE — LIVE SEARCH V2.1.11

## Phạm vi
Bản sửa trên V2.1.10, tập trung vào OCR dấu thập phân và hai ảnh đọc độc lập. **KHÔNG tự mở phòng LIVE hoặc gửi bình luận.** Không sửa logic đăng nhập, hồ sơ hoặc Multi-Device.

## Các thay đổi
- Giữ nguyên kiểm tra icon `Person` / `Like` / `Unknown` trước khi đọc Viewer.
- Với OCR đồng thuận `34K+` hoặc `77K+`, dùng kiểm tra **điểm sáng cô lập gần cuối chữ số thứ nhất** để phát hiện dấu chấm tí hon. Nếu tìm thấy, đưa ra *ứng viên* `3.4K+` hoặc `7.7K+` (`DotPixelsRecoveredCandidate`). **Không chia 10 theo quy tắc cứng.**
- Nếu OCR hai chữ số + K/M nhưng không tìm được bằng chứng dấu chấm, trả về `TwoDigitScaleAmbiguous` và không dùng con số để lọc LIVE; có thể là `34K` thật.
- Sau ảnh OCR đầu, chụp thêm ảnh thứ hai (chờ 1.2 giây, giữ nguyên màn hình) và đối chiếu từng card; kết quả trùng hiển thị `OcrRepeatedCandidate`. Mâu thuẫn rõ ràng thành `OcrFrameConflict`.
- Trạng thái `OcrRepeatedCandidate` là dữ liệu *tham khảo*, **không** được nâng thành `Verified` hoặc `OcrConfirmed`, không tự lựa chọn phòng dựa vào hai lượt OCR.

## Cài đặt
1. Sao lưu mã nguồn hiện tại và dừng tool.
2. Giải nén PATCH V2.1.11 đè vào thư mục V2.1.10, hoặc dùng FULL vào một thư mục mới.
3. Chạy `BUILD_RUN.bat` trên Windows với .NET 8 SDK, Appium Server và Tesseract đã cài.
4. Mở Phone 01 -> TÌM LIVE -> từ khóa `live`.

## Log kiểm tra
- `dotPixels=True; quy tắc=DotPixelsRecoveredCandidate`
- `OCR card X/4: hai ảnh cùng đọc 3,400 Viewer (CHƯA XÁC MINH)`
- `TwoDigitScaleAmbiguous`: không đoán thành 3.4K hoặc 34K khi chưa có bằng chứng.
- Các số Viewer là lượng xem **hiển thị làm tròn** trên chip TikTok.

## Giới hạn
Màn hình Android mẫu chỉ 352x624; số và dấu chấm rất nhỏ. OCR có thể mất dấu chấm khi đổi màu nền hoặc bố cục TikTok. Cần chạy thực tế trên XCloudPhone, đối chiếu ảnh và log. Bản này chưa được build hoặc chạy Windows trong môi trường đóng gói Linux.
