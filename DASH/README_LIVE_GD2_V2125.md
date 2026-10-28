# V2.1.25 – FIND & OPEN LIVE (giai đoạn 2)

## Luồng hoạt động
- Sau khi lấy mẫu đúng 8 card (4+2+2), chọn card có Viewer OCR cao nhất phù hợp ngưỡng.
- Lưu vị trí màn hình, slot, hình chụp và vùng card trong bộ nhớ phiên Appium.
- Vuốt ngược qua tối đa số màn hình cần tìm + 2 lượt dự phòng; chụp lại sau mỗi lượt, so sánh ảnh preview giữa card đã lưu và card đang nhìn thấy.
- Chỉ chạm khi ứng viên vượt ngưỡng giống ảnh và đủ khác so với các ứng viên còn lại; tuyệt đối không sử dụng tọa độ ban đầu để nhấn.
- Sau chạm, kiểm tra đã rời lưới tìm kiếm hay chưa. Đây chỉ là kiểm chứng chuyển trang, **không khẳng định chắc chắn đã mở đúng phòng** nếu không có ID phòng được TikTok cung cấp.
- Ảnh LIVE động thay đổi nhiều có thể làm hệ thống không xác minh được; trong trường hợp đó thông báo TARGET_NOT_FOUND và KHÔNG chạm. Không spam click.

## Log và chẩn đoán
- `TARGET_FOUND`: chọn được ứng viên; đang tìm lại.
- `PHASE2 RECHECK`: điểm khác biệt ảnh của ứng viên tốt nhất và thứ nhì.
- `TARGET_VERIFIED`: khớp ảnh đủ điều kiện để nhấn.
- `TARGET_OPENED`: đã rời lưới kết quả (chưa xác nhận ID phòng).
- `TARGET_NOT_FOUND`/`TARGET_NOT_OPENED`: không đủ bằng chứng hoặc không chuyển trang.
- Tệp trong `live-scroll-*/phase2`: `reverse-XX.png`, `reverse-XX.xml`, `reverse-XX.json`, `after-tap.png`, `after-tap.xml`.

## Cảnh báo
Viewer OCR là ước lượng, không phải ID phòng. So khớp ảnh preview cũng không chứng thực tuyệt đối vì video thay đổi; khi không chắc chắn, hệ thống dừng an toàn. Không tự bình luận hoặc thao tác trong phòng.

## Kiểm thử
Chạy `BUILD_RUN.bat` trên Windows có .NET 8, Appium, Android đang online. Kiểm tra log PHASE2 và ảnh chẩn đoán. Chưa build hay thử trên Android trong môi trường tạo bản này.
