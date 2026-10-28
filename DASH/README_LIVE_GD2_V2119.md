# V2.1.19 – Live Search: nhận diện sau kéo dài và PENDING

- Bỏ giới hạn cứng 430px trong đường đối chiếu OCR và tính độ dịch chuyển; giới hạn mới dựa trên chiều cao vùng card, tối thiểu 430px.
- Không dừng sau 4 lượt không xác minh được NEW nếu màn hình vẫn trả card; tiếp tục tới giới hạn MaxSearchScrolls hoặc timeout đã cài.
- Có thể xác nhận NEW bằng cặp **tiêu đề + chủ phòng**, nếu cả hai có nội dung cụ thể, khác mọi card đã lưu và không trùng card khác trong ảnh, sau khi màn hình có thay đổi. Đây là nhánh dự phòng khi kéo dài không còn card cũ trong khung hình.
- Card còn mơ hồ tiếp tục ở PENDING; số Viewer không dùng làm ID, không tự tính PENDING thành NEW.
- Độ kéo tự chọn 1–75%, timeout 240/600 giây được giữ nguyên.

## Giới hạn / kiểm thử
- Chưa kiểm thử thiết bị và chưa build bằng .NET SDK trong môi trường hiện tại.
- Trường hợp TikTok không trả được tiêu đề và chủ phòng, đồng thời không có card cũ để đối chiếu, công cụ vẫn giữ PENDING (không thể chứng minh duy nhất từ số Viewer/ảnh video).
- Để xác minh, chạy BUILD_RUN.bat, thử kéo 30%, 45%, 65%, so NEW/SEEN/PENDING và theo dõi log có thông báo nhận diện tiêu đề+chủ phòng hay không.
