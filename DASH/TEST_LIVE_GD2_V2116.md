# Kịch bản kiểm thử V2.1.16 trên Phone 01

1. Start LIVE Search, kiểm tra 4 card đầu được cộng vào nhóm độc lập với việc một số Viewer OCR lỗi. Không phải chờ 4/4 số Viewer hợp lệ.
2. Kéo 30%: nếu hai card từ hàng dưới lên hàng trên, chúng phải là `SEEN` (không tăng) **khi đối chiếu đủ bằng chứng**.
3. Nếu badge bị cắt hay thumbnail động mạnh, card không rõ phải là `PENDING`; được giữ để xem lại sau lần kéo tiếp theo, không coi là `NEW` chỉ vì khác tọa độ.
4. Khi xuất hiện phòng mới từ dải dưới, có mốc dịch chuyển của card cũ và hình ảnh khác rõ ràng, số `NEW nhóm` được tăng 1 dù Viewer là UNKNOWN.
5. Khi nhóm đủ 8 card duy nhất, một card `IconLikeIgnored` hoặc `OcrConflict` không được ngăn xếp hạng các card đã đọc Viewer hợp lệ.
6. Nếu hết thời gian trước khi đủ 8, hiển thị rõ số card thực sự xác minh được; không suy ra đã quét đủ 8.
7. Nhấn Stop giữa chừng; tác vụ hủy an toàn và không phát bình luận hoặc mở LIVE.

**Log nên kiểm tra:** `màn hình N: NEW=X, SEEN=Y, PENDING=Z`, `NEW nhóm #n (track ID k, ô m/4)`, `PENDING → NEW`, `đánh giá ... Viewer không đọc được=...`.

**Chẩn đoán khi PENDING lặp:** gửi XML, ảnh và log ngay sau khi kéo 2 lần; kiểm tra thông tin riêng tư trước.
