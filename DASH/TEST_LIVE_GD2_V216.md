# Kiểm thử V2.1.6 — chỉ đọc

1. Chạy `tesseract --version` và `tesseract --list-langs` (phải có `eng`).
2. Mở `BUILD_RUN.bat`; Phone 01 ADB online, Appium 127.0.0.1:4723.
3. Chọn từ khóa `liên quân`, tab LIVE. Chờ UI ít nhất 3 lần đọc.
4. Màn hình ví dụ 352x624 phải nhận **4 vùng card** tương ứng với hai hàng 2 cột.
5. Quan sát log `OCR card 1/4` đến `4/4`; đối chiếu từng số với ảnh. Nếu sai, `OcrCandidate` phải được xem là chưa xác minh.
6. Nếu OCR không được cài, log rõ `máy chưa tìm thấy Tesseract OCR` và dừng ở chẩn đoán; không tiếp tục cuộn vô hạn.
7. Trên giao diện Settings kiểm tra `SearchSwipePercent=5`; sau khi đọc xong nhóm, chỉ một cú kéo ngắn; đảm bảo không bỏ sót card.
8. Stop phải hủy OCR/đọc UI và không làm thay đổi trạng thái TikTok hay tài khoản.
