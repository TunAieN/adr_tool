# LIVE Search V2.1.6 — XML + Screenshot OCR (thử nghiệm)

## Phát hiện từ dữ liệu Phone 01 thực tế
- `live-search-last-ui.xml`: GridView `[0,102][352,583]`, một FrameLayout đệm cao 7 px và 4 card: `[7,109][179,448]`, `[179,109][351,448]`, `[7,448][179,583]`, `[179,448][351,583]`.
- Mọi card chỉ có ViewGroup và ImageView không có `text` / `content-desc`. XPath không thể trích xuất Viewer/tiêu đề chưa được xuất ra Accessibility Tree.
- `live-search-last-screen.png`: số viewer hiện rõ trên ảnh: 3.2K+, 3.9K+, 795, 2.5K+. Từ nguồn ảnh, không thể coi các số đó là dữ liệu XML.

## Bản sửa
1. Giữ xác định vị trí card từ cây GridView có thật; không dùng XPath card `[2]` cố định.
2. Nếu XML không cho biết tiêu đề/Viewer, lấy ảnh màn hình qua Appium và cắt từng vùng badge riêng.
3. Chạy OCR **Tesseract trên PC Windows** ở hai chế độ tiền xử lý. Nếu cả hai đọc cùng một số và có tín hiệu biểu tượng người thì đánh dấu `OcrConfirmed`. Nếu chỉ một lần đọc được, đánh dấu `OcrCandidate`; không tự xếp hạng số này.
4. Ở card hiển thị đủ chiều cao, thử OCR dòng tiêu đề. Chú thích `OCR (ước lượng)` vì tiếng Việt/văn bản nhỏ có thể lỗi.
5. `OcrConfirmed` được dùng như một ứng viên **gợi ý** trong Search; **không tự mở LIVE, không gửi bình luận**.
6. Độ dài kéo mặc định cho cấu hình *mới* là 5% (cũ 8% vẫn được giữ theo cấu hình đã lưu); tốc độ kéo 180 px/s. Chỉ cuộn khi dữ liệu card đã đọc xong.

## Điều kiện chạy OCR
Tesseract OCR **không đi kèm ZIP**. Trên máy Windows cài từ [UB Mannheim](https://github.com/UB-Mannheim/tesseract/wiki) theo hướng dẫn của [Tesseract OCR](https://tesseract-ocr.github.io/tessdoc/Installation.html); chọn ngôn ngữ English (`eng`). Cài đường dẫn mặc định `C:\Program Files\Tesseract-OCR\tesseract.exe`.

Thử trong CMD:
```bat
"C:\Program Files\Tesseract-OCR\tesseract.exe" --version
"C:\Program Files\Tesseract-OCR\tesseract.exe" --list-langs
```
Nếu đặt ở đường dẫn khác, trong CMD dùng:
```bat
set "TESSERACT_EXE=D:\Tools\Tesseract-OCR\tesseract.exe"
BUILD_RUN.bat
```

**Không cố copy font hoặc file ngôn ngữ từ hệ thống này về máy anh.** Cài engine chính thức/đáng tin cậy trên PC rồi khởi động tool.

## Cài đặt
- Ghi đè bản PATCH lên V2.1.5 (bản vá gồm 5 file: `Core/LiveSearchEngine.cs`, `Core/LiveVisualOcr.cs` **file mới**, `Core/LiveManagerSettings.cs`, `Core/AdbAndAppium.cs`, `LiveSearchDialog.cs`).
- Chạy `BUILD_RUN.bat`.
- Vào **Cấu hình LIVE → Nguồn LIVE**, nếu muốn vuốt 5% thì chỉnh lại từ 8% và lưu; cấu hình cũ không bị ghi đè.
- Chạy tìm `liên quân` Phone 01; quan sát `OCR card 1/4`, v.v. Đối chiếu Viewer trong cửa sổ với màn hình thật.
- Các số `OcrCandidate`/không đọc được phải kiểm tra thủ công, không được mặc định 0 hoặc chọn dựa trên số đoán.

## Giới hạn / kiểm thử
- OCR với ảnh badge nhỏ cỡ 352×624 **có thể nhầm** 3.2K, 3.9K, 795, 2.5K; đặc biệt nếu chữ bị phủ lên video động, bitrate thấp hoặc phụ thuộc vị trí giao diện. Đây là bản thử nghiệm OCR, **chưa xác nhận khả năng đọc chính xác cả bốn card trên XCloudPhone**.
- Vùng OCR hiện tham chiếu vị trí badge tương ứng với ảnh anh gửi. Nếu giao diện khác, phải điều chỉnh theo dữ liệu ảnh thực tế.
- **Chưa biên dịch `.NET 8` trên Windows hoặc kiểm thử Appium thực tế** trong môi trường tạo bản vá.
- Chỉ tìm, đọc, đánh giá dữ liệu LIVE. Không tự đăng bình luận.
