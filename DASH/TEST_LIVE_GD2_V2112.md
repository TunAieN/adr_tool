# Kiểm thử V2.1.12

1. `dotnet build AndroidMultiDeviceV1/AndroidMultiDeviceV1.csproj -c Release` trên Windows.
2. Trên TikTok Search > LIVE, chụp ảnh tại đúng thời điểm OCR chạy, so log với màn hình.
3. Màn hình mẫu `ghép đôi`: Viewer 99, 533, 55, 319, đều icon Person.
4. Kỳ vọng OCR thô: `99/9/99 -> 99`, `533/5330/533 -> 533`, `55/""/5 -> 55`, `319/3190/319 -> 319`.
5. `dotPixels=n/a` cho badge số nguyên; `DotPixelsRecoveredCandidate` chỉ cho K/M có bằng chứng pixel.
6. Kiểm thử hồi quy K/M: `3.4K+`, `7.7K+`, `1.4K+`, `4.7K+` không bị hiểu thành 34K, 77K, 14K, 47K; nếu thiếu dấu chấm và không có bằng chứng pixel thì trả Ambiguous, không chọn số.
7. Nhãn Like và Unknown không cho phép dùng con số làm Viewer.
8. Stop hủy tìm kiếm bình thường; không gửi nội dung.
