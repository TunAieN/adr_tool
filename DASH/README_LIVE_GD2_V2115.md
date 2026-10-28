# LIVE Search V2.1.15 — Chống đếm trùng card sau cuộn

Nền tảng: FULL V2.1.14. Chỉ sửa AndroidMultiDeviceV1/Core/LiveSearchEngine.cs.

## Lỗi cụ thể trong log 01:26
Ở màn hình đầu, tool đọc Viewer 81, 52, 78, 104. Sau kéo, các card 78 và 104 còn ở trên màn hình nhưng bị cấp ID 5 và 6 vì hàng dưới đã di chuyển gần một chiều cao card. Điều kiện bám vị trí cũ quá chặt, khiến nhóm có 6/8 thay vì 4/8.

## V2.1.15 xử lý
- Mỗi lượt chụp một ảnh riêng của GridView để so sánh phần thumbnail từng card với ảnh trước khi kéo.
- Ghép bằng hình ảnh + cùng cột, không ghép dựa trên vị trí Y gần nhất; cho phép một card từ hàng dưới lên hàng trên.
- Card còn chưa chắc chắn được OCR lại. Nếu cùng cột, Viewer gần nhau, và có thêm chứng cứ trùng hình hoặc cả cặp trong một hàng khớp với nhau, dùng lại ID cũ.
- Trường hợp không đủ bằng chứng một card mới, bỏ qua và ghi log, không cộng vào nhóm 8 chỉ vì thay vị trí.
- Đếm riêng 'card CHƯA ĐỐI CHIẾU' (trước OCR) và 'card mới xác định' (sau đối chiếu).
- Nếu ba lần kéo liên tiếp không có card mới đủ căn cứ, dừng từ khóa thay vì đếm card cũ liên tục.

## Nâng cấp
1. Sao lưu thư mục V2.1.14 và kho dữ liệu cấu hình hiện hành.
2. Giải nén PATCH vào gốc mã nguồn V2.1.14, ghi đè file Core/LiveSearchEngine.cs.
3. Chạy BUILD_RUN.bat trên Windows, dùng .NET 8 SDK.
4. Trong Cấu hình LIVE giữ độ kéo 30% và chờ 4500ms, kiểm thử một thiết bị.

## Kỳ vọng thử nghiệm
- Lượt 1: 4 card mới xác định (ví dụ 81, 52, 78, 104), nhóm 4/8.
- Lượt 2 nếu hiện lại 78 và 104: tool phải ghi ID cũ và không tăng bộ đếm.
- Sau đó khi có 100 và 421 ở hàng mới: cộng đúng 2 card, nhóm 6/8.
- Đọc tiếp hai LIVE thực sự khác rồi mới đạt 8/8.

## Giới hạn
- Preview LIVE là hình động nên có thể không đủ bằng chứng để phân biệt hai phòng; trường hợp không rõ được bỏ qua thay vì đếm sai.
- Giá trị Viewer đọc OCR là ước lượng, có thể thay đổi theo thời gian; không thể dùng riêng Viewer làm danh tính phòng LIVE.
- Không mở phòng, không đăng bình luận, không chỉnh đăng nhập, kho tài khoản hoặc Multi-Device.
- Kiểm tra ảnh mẫu và mã nguồn tĩnh đã thực hiện; CHƯA build WinForms hay thử Appium trên Windows/XCloudPhone tại đây.
