# LIVE Search V2.1.1 – quét chậm, nhóm 8 card

Cập nhật từ bản V2.1.0. Đây là phân hệ **chỉ đọc**, không tham gia LIVE và không gửi bình luận.

## Sửa lỗi từ log thực tế

- Không vuốt khi chưa đọc được card: chờ giao diện ổn định và thử đọc lại **cùng màn hình**. Hai lần đọc thất bại sau các lượt chờ sẽ dừng việc tìm nguồn với lý do selector/XML, không lặp vuốt liên tục.
- Bộ đọc card dùng dấu hiệu badge LIVE + vị trí hình học, có thể tìm card nằm lồng sâu trong `GridView` hoặc danh sách Compose/RecyclerView. Không giới hạn `FrameLayout` con trực tiếp.
- Dùng cử chỉ cuộn ngắn hơn (0,52 chiều dài vùng nội dung). Sau một lần cuộn, chờ tối thiểu 1,8 giây rồi đọc UI định kỳ khoảng 1,1 giây cho đến khi nhận được hai ảnh trạng thái liên tiếp phù hợp.
- Lưu card đã xem theo fingerprint, gom tối đa khoảng 8 card mới trong một lượt đánh giá (kể cả trải qua nhiều màn hình cuộn). Xếp hạng số Viewer của cả nhóm, không lựa LIVE đầu tiên.
- Khi không có card đạt điều kiện, chuyển sang nhóm sau trong cùng từ khóa. Hết giới hạn cuộn hoặc thời gian từ khóa mới chuyển từ khóa khác.
- Viewer chỉ được coi là **đã xác minh** khi XML cung cấp nhãn/bằng chứng cho biểu tượng người. Số cạnh ImageView không có metadata chỉ là ứng viên cần kiểm tra và không được tự động chọn làm phòng tương tác.
- Khi phát hiện ứng viên đứng đầu, chỉ báo trong cửa sổ, **không mở LIVE** ở giai đoạn này.

## Cài đặt

Với dự án V2.1.0, đóng tool rồi sao lưu mã nguồn. Chép ba file trong bản PATCH vào đường dẫn tương ứng: `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs` và `AndroidMultiDeviceV1/LiveSearchDialog.cs` và `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`.
Chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK. Nếu phát hiện lỗi build, gửi nguyên văn thông báo.

## Kiểm thử mong đợi

```
Từ khóa: ghép đôi — mở TikTok Search.
Đã vào kết quả LIVE cho “ghép đôi”.
ghép đôi: lượt 1, thấy 2 card, thêm 2 card mới, nhóm đang có 2/8.
ghép đôi: đã lưu card đã kiểm tra; chuẩn bị cuộn đúng 1 lần rồi chờ UI ổn định.
ghép đôi: lượt 2, thấy 2 card, thêm 2 card mới, nhóm đang có 4/8.
...
ghép đôi: đã đánh giá nhóm 8 card; 2 đạt ngưỡng Viewer xác minh.
ghép đôi: ĐỀ XUẤT LIVE có Viewer cao nhất trong nhóm: ..., 2.450 Viewer. Chưa mở phòng.
```

Nếu mã mới vẫn không nhận được card, cần chụp **Appium Inspector Page Source/XML ngay lúc có 2 card trên màn hình**. Ảnh màn hình không cho biết chính xác tên node và metadata của badge Viewer.

## Giới hạn kiểm thử

Chưa có phiên Appium/XCloudPhone và chưa có .NET SDK trong môi trường tạo bản vá, nên chưa build hoặc kiểm thử trên thiết bị thật. XPath bố cục có thể thay đổi giữa các bản TikTok. Logic nhận dạng theo tọa độ là fallback, kết quả chưa có Creator/ID chính xác cần kiểm tra lại trước khi dùng.
