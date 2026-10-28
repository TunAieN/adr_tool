# Kiểm thử V2.1.11

## Kiểm tra ảnh mẫu có sẵn
Card 1: hiển thị 3.4K+, V2.1.10 OCR `34K+`; dấu chấm pixel phải được tìm thấy, giá trị tạm `3400`.
Card 2: hiển thị 7.7K+, V2.1.10 OCR `77K+`; dấu chấm pixel phải được tìm thấy, giá trị tạm `7700`.
Card 3: hiển thị 2.3K+; giữ giá trị tạm `2300`.
Card 4: hiển thị 4.7K+; giữ giá trị tạm `4700`.

## Kiểm thử hồi quy thủ công trên Windows
1. Test card có icon Like -> IconLikeIgnored, Viewer=null.
2. Test card chưa rõ icon -> IconUnknown, Viewer=null.
3. Test badge hiển thị 34K+ thật, không có dấu chấm -> không tự đoán 3.4K+.
4. Đọc 2 ảnh liên tiếp cùng giá trị -> OcrRepeatedCandidate, vẫn chưa được tự chọn phòng.
5. Hai ảnh ra giá trị khác nhau -> OcrFrameConflict; không tự chọn cao/thấp.
6. Tự Stop lúc chờ ảnh thứ hai -> hủy nhanh; không kẹt Appium session.
7. Kiểm tra các thao tác đăng nhập, hồ sơ, kho, Start/Stop nhiều máy giữ nguyên.
