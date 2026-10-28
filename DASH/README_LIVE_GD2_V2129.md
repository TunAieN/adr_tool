# V2.1.29 – PHASE3 FAST TARGETED UI

- Giai đoạn 1 (4+2+2) và giai đoạn 2 không đổi.
- Nhấn k13 tại tâm bounds lấy từ chính XML LIVE vừa đọc, không dùng tọa độ cố định. Nếu không có bounds hợp lệ, fallback XPath cũ.
- Sau Back kiểm tra Viewer qua tìm phần tử trực tiếp, tránh lấy toàn XML khi không cần. Nếu hai lần không thấy Viewer, kiểm tra XML toàn phần để chẩn đoán.
- Lưu live-return.png và live-return-check.json khi đường nhanh thành công; chỉ lưu live-return.xml khi cần fallback.
- Không nhấn lại k13 khi tap đã gửi thành công; không Back thêm khi chưa biết màn hình.
- Cần build .NET 8 và thử trên thiết bị thật. Tốc độ 5–10 giây là mục tiêu, không phải bảo đảm.
