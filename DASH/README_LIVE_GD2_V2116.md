# V2.1.16 — LIVE Card State Manager / Pending Recovery

Nguồn: dự án đầy đủ V2.1.15. Thay đổi duy nhất trong chương trình:
`AndroidMultiDeviceV1/Core/LiveSearchEngine.cs`.

## Lỗi gốc từ log 01:45

- Card vẫn đang chạy LIVE có thể bị cắt đầu badge khi kéo 30%; không đọc được icon/Viewer không chứng minh được card là cũ.
- Trạng thái card chưa rõ (`PENDING`) trước đây không được bảo toàn qua các lượt đọc, có nguy cơ bị cấp lại ID hoặc bị loại hoàn toàn.
- `card MỚI #8` là ID tracking, trong khi số card duy nhất đã chấp nhận mới chỉ là 5/8; log rất dễ gây hiểu lầm.
- OCR có thể đọc sai số nguyên như `7332` hoặc `2877`; các kết quả đó chỉ mang tính tham khảo.

## Thay đổi

1. `NEW` (card duy nhất đủ bằng chứng): cộng 1 vào nhóm; **Viewer được phép UNKNOWN**.
2. `SEEN` (khớp hình/đối chiếu từ viewport cũ): không cộng thêm.
3. `PENDING` (thiếu bằng chứng mới/cũ): giữ ID và lịch sử khi còn hiển thị; đọc lại ở viewport sau. Không cộng trước khi có bằng chứng.
4. Card chưa OCR được: không dừng toàn bộ từ khóa chỉ vì một card lỗi. Nếu xác nhận là card mới, Viewer được gắn trạng thái chưa biết, và bị loại riêng khỏi xếp hạng.
5. Card mới có thể được xác minh khi thật sự **xuất hiện từ dải dưới sau khi danh sách cuộn**, khác ảnh thumbnail cũ và có ít nhất một card khác làm mốc đo chuyển động thực tế. Không suy luận từ quãng kéo cấu hình đơn thuần.
6. Một PENDING từng xuất hiện từ dưới có thể trở thành NEW khi đã theo dõi qua các viewport, badge rõ hơn và có mốc chuyển động độc lập. Không lấy riêng số Viewer làm danh tính phòng.
7. Log phân biệt `NEW`, `SEEN`, `PENDING` và hiển thị vị trí trong nhóm là `NEW nhóm #...`, khác `track ID`.
8. Khi so sánh nhóm, thống kê các số Viewer đọc lỗi, badge Like và các Viewer ngoài ngưỡng. **Một card lỗi không ngăn so sánh các card còn lại.**
9. Tối đa 4 lượt không tìm được card NEW có bằng chứng sẽ đổi từ khóa; không cuộn mãi và không tự bịa số lượng để đủ 8.

## Cài đặt

- Sao lưu V2.1.15 cùng thư mục cấu hình/kho tài khoản của bạn.
- FULL: giải nén ra thư mục mới, chạy `BUILD_RUN.bat`.
- PATCH: giải nén đè vào gốc mã nguồn V2.1.15 để thay đúng `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs`, sau đó chạy `BUILD_RUN.bat`.
- Thử **một điện thoại** với `30%` độ kéo, `4500ms` chờ sau kéo, timeout mỗi từ khóa 180-240s.

## Lưu ý về độ tin cậy

- Video LIVE là ảnh động, thumbnail và Viewer có thể biến đổi nhanh; không có ID phòng thật từ XML. Do đó chưa thể đảm bảo phát hiện trùng tuyệt đối; trường hợp không chắc chắn là PENDING, không tính vào 8.
- Chỉ tìm kiếm, đọc, lọc và **đề xuất** phòng. Không tự mở phòng, không gửi bình luận, không chỉnh kho tài khoản.
- Ảnh màn hình và XML chẩn đoán lưu **trên PC**, hãy kiểm tra riêng tư trước khi chia sẻ.
- Tại môi trường tạo bản này **không có Windows/.NET SDK**, nên chưa build WinForms và chưa kiểm thử Appium với điện thoại thực. Cần chạy `BUILD_RUN.bat` trên Windows.
