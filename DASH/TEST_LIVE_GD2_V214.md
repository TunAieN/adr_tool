# Quick test V2.1.4

1. Dùng Phone 01 đang Online, Appium server chạy tại `http://127.0.0.1:4723`.
2. Trong Cấu hình LIVE, chọn `SearchSwipePercent=24`, `SearchAfterSwipeWaitMilliseconds=3600`, `SearchPerCardWaitMilliseconds=650`.
3. Từ khóa `ghép đôi`, Start TÌM LIVE. Xác nhận 3 lần đọc cấu trúc UI trước khi có log đọc card.
4. Sau log `đọc card 1/N` ... `đọc card N/N` mới xuất hiện log `vuốt NGẮN ...`.
5. Kiểm tra khi `0` card: dừng để chẩn đoán; không tự vuốt vô hạn.
6. Khi Viewer không có trong XML, số Viewer là `?`, không được chọn phòng chỉ vì số đoán.
7. Nhấn Stop: tác vụ dừng, session được giải phóng, không điều khiển worker khác.
8. Đóng rồi mở lại Cấu hình LIVE: thông số nhịp cuộn được giữ nguyên.
