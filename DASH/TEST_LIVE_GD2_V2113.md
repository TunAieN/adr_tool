# Bộ test thủ công — V2.1.13

1. **Nhận 4 card ban đầu:** log 4 khung / 4 card mới; OCR chỉ đọc Viewer có icon người, số Like được bỏ qua.
2. **Kéo ngắn:** sau khi đọc xong card, đúng 1 dragGesture rồi chờ cấu hình; trên máy 352x624 và kéo 5% là khoảng 31px, không vuốt đột ngột qua nhiều hàng.
3. **Kéo chưa lộ card mới:** log `0 card mới`, không OCR lại 4 card và không cộng thêm vào nhóm.
4. **Card mới vào màn hình:** log `2 card mới` hoặc số thực tế; nhóm tăng tương ứng; các card cũ giữ ID, không đếm lặp.
5. **Nhóm 8 card:** log gợi ý Viewer OCR lớn nhất trong nhóm đạt ngưỡng, tuyệt đối không mở phòng.
6. **Nhóm không có card đủ điều kiện:** tool tiếp tục kéo trong giới hạn, hoặc chuyển từ khóa khi hết giới hạn.
7. **Không thể cuộn/hết danh sách:** 2 lần không thay đổi tọa độ card -> dừng từ khóa an toàn.
8. **Stop khi đang chờ/kéo/OCR:** hủy token, kết thúc session, không tạo tác vụ khác.
9. **Timeout 65 giây:** vẫn nhận/log các card đã đọc, có thể trả ứng viên từ nhóm chưa đủ 8 với nhãn tham khảo; để quét nhiều hơn, tăng timeout trong Cấu hình LIVE.
10. **Độc lập:** kiểm tra không thay đổi logic đăng nhập, kho tài khoản, hồ sơ hoặc phương thức Google.

Nếu lỗi build: gửi toàn bộ stdout BUILD_RUN.bat. Nếu lỗi nhận ID sau kéo: gửi XML+ảnh cùng màn hình trước và sau lần kéo.
