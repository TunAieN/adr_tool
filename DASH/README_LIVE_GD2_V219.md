# Android Multi-Device LIVE Search — V2.1.9: Viewer Icon Gate

## Phạm vi

Bản này sửa duy nhất Core/LiveVisualOcr.cs trên nền V2.1.8.

* Badge LIVE có thể hiển thị biểu tượng **người (viewer)** hoặc **ngón tay cái (like)**.
* Một vùng có điểm ảnh trắng không chứng minh đó là biểu tượng người. V2.1.8 ghi `iconPixels=True` kể cả khi biểu tượng là Like.
* V2.1.9 nhận diện **hình dạng icon trước OCR**. Nếu Like, ghi `IconLikeIgnored` và không đưa con số cạnh icon vào Viewer. Nếu không nhận diện chắc chắn, ghi `IconUnknown`, không coi là Viewer.
* Với icon người, OCR vẫn trả `OcrCandidate` hoặc `OcrConflict` từ một ảnh; không tự xác minh độc lập.
* Bản sửa không click LIVE, không tham gia phòng và không gửi bình luận.

## Dữ liệu kiểm tra ngoại tuyến

Ảnh thử `ghép đôi`: card 1 = Like `11K+` => bỏ qua; card 2 = Người `373` => có thể đọc Viewer; card 3 = Người `1.4K+` => có thể đọc Viewer, OCR có thể bị xung đột; card 4 = Like `91` => bỏ qua.

Đã đối chiếu mẫu 4 icon trong ảnh này và 4 icon người trong ảnh thử trước bằng phép so khớp mẫu. Kết quả ngoại tuyến đúng 8/8. Đây **không phải** kết quả chạy trực tiếp Appium trên Windows; kiểu badge khác có thể thành `IconUnknown`.

## Cài đặt

1. Sao lưu thư mục code và đóng ứng dụng.
2. Giải nén PATCH vào **bản mã nguồn V2.1.8**, ghi đè `AndroidMultiDeviceV1/Core/LiveVisualOcr.cs`.
3. Giữ nguyên Tesseract OCR đã cài. Chạy `BUILD_RUN.bat`.
4. Tìm `ghép đôi` trên Phone 01. Trong log card 1 & 4 phải được loại bằng `icon Like — BỎ QUA số Like`.
5. Đối chiếu card 2/3 trên screenshot và log. Nếu icon biến thể chưa hỗ trợ, dừng ở `IconUnknown`, không ép thành Viewer.

Không xóa account-vault.json hay dữ liệu TikTok.
