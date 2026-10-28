ACCOUNT LIFECYCLE V5 — PATCH TRÊN ACCOUNTV4
===========================================

1) SAO LƯU trước khi cập nhật:
   %LOCALAPPDATA%\AndroidMultiDeviceV1\settings.json
   %LOCALAPPDATA%\AndroidMultiDeviceV1\account-vault.json
   Và sao lưu cả thư mục source AccountV4.

2) DỪNG TOÀN BỘ worker rồi thoát ứng dụng đang chạy. Giải nén
   PatchV5.zip lên đúng thư mục có DASH\AndroidMultiDeviceV1\
   hoặc dùng AccountV5.zip FULL. Chạy DASH\BUILD_RUN.bat.

3) Mỗi thiết bị có riêng cấu hình Vòng đời TK:
   THIẾT BỊ > Chọn máy > Vòng đời TK.
   Khi bấm Lưu có hai lựa chọn:
   - Từ phiên tiếp theo (an toàn mặc định)
   - Ngay cho phiên ĐÃ XÁC MINH. Deadline tính từ giờ xác minh,
     không tính từ giờ bấm Lưu. Giảm giới hạn xuống dưới thời gian
     đã chạy sẽ kết thúc phiên tại điểm huỷ an toàn gần nhất.

4) Start LIVE khi kho máy CÓ tài khoản:
   - Appium đọc Profile chính chủ, xác minh username KHỚP DUY NHẤT
     với tài khoản của ĐÚNG máy đó.
   - Tài khoản phải còn đủ điều kiện sử dụng; đã từng chạy sẽ không
     được chọn lại nếu chính sách "Không dùng lại" đang bật.
   - Đăng ký phiên + countdown sau khi xác minh, rồi chạy LIVE.
   - Hết giờ: huỷ LIVE, giải phóng Appium; pm clear TikTok/GMS đúng
     serial; chỉ khi clear thành công mới handoff vào Start tài khoản
     gốc để Google Login/tìm LIVE với tài khoản chưa dùng tiếp theo.
   - Nếu không xác minh được username, dừng an toàn, KHÔNG pm clear.
   - Nếu máy không có tài khoản nào được gán, giữ chế độ LIVE độc lập
     như V4 và hiển thị "CHƯA KÍCH HOẠT"; KHÔNG có vòng đời tự động.

5) Start tài khoản cũng dùng bộ đếm mới, để thay giới hạn NGAY giữa
   phiên đã xác minh. Phiên TEST một tài khoản giữ chính sách không
   chuyển sang tài khoản khác. Chế độ chọn máy và vault vẫn độc lập.

6) Nếu máy đã chạy từ phiên bản V4 trước khi cập nhật, phải Stop,
   thoát ứng dụng, build bản V5, Start LIVE lại. Không thể gắn một
   bộ đếm mới vào process/worker V4 đã chạy.

7) Sau khi Start LIVE, xem CHI TIẾT máy:
   Bộ đếm vòng đời / Trạng thái xác minh / Bắt đầu / Hạn / Còn lại.
   Đừng nhầm "giới hạn đã lưu" với "đang áp dụng".

8) Mốc log V5 để đối chiếu:
   LIVE_OWNER_VERIFIED
   LIVE_ACCOUNT_SESSION_STARTED
   ACCOUNT_LIFECYCLE_APPLIED_NOW
   LIVE_ACCOUNT_SESSION_LIMIT_REACHED
   LIVE_ACCOUNT_ROTATION_CLEAR_OK
   LIVE_ACCOUNT_ROTATION_HANDOFF

9) Kiểm tra source không thay thế build Windows:
   python DASH\TEST_ACCOUNT_LIFECYCLE_V5.py
   python DASH\TEST_ACCOUNT_LIFECYCLE_V4.py
   python DASH\TEST_ACCOUNT_LIFECYCLE_V3.py
   Sau đó chạy Windows .NET 8 BUILD_RUN.bat. Test thực tế một điện
   thoại với tài khoản kiểm thử và 1-2 phút trước khi dùng nhiều máy.

An toàn: Không có tính năng vượt CAPTCHA hoặc checkpoint.
Xóa dữ liệu TikTok/GMS là thao tác phá huỷ dữ liệu cục bộ và đăng xuất;
chỉ dùng khi đã xác minh tài khoản và phiên đã hết hạn thực sự.
