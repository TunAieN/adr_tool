# Test cases V2.1.1

1. `GridView` trực tiếp, 2 card: cả 2 xuất hiện trong bảng, không vuốt ngay trước hai lượt UI ổn định.
2. `GridView` chứa `FrameLayout` lồng 3 tầng: vẫn tìm card bằng badge LIVE.
3. RecyclerView không có `GridView` nhưng có 2 badge LIVE dưới vùng tabs: vẫn đọc ứng viên.
4. Tab `LIVE` ở đầu màn hình: không bị nhận nhầm thành một card.
5. Không có card trong XML dù screenshot cho thấy có: chờ lại cùng trang, dừng với chẩn đoán sau khi thất bại; không cuộn nhiều lần và không nhảy từ khóa.
6. Có 8 card qua nhiều trang, chỉ 2 card có Viewer xác minh >= ngưỡng: chọn ứng viên cao Viewer nhất **trong nhóm** và chỉ báo cáo, không mở phòng.
7. 8 card không đủ điều kiện: tiếp tục quét trong cùng từ khóa.
8. Hết giới hạn cuộn hoặc thời gian từ khóa: đổi từ khóa; hết tổng thời gian: dừng.
9. Click Stop trong lúc chờ UI: hủy ngay theo CancellationToken.
10. Popup quyền thông báo: chọn đúng Don't allow, không nhấn popup khác.
