# PHOTO COMMIT RECONCILIATION — V1.6.1.5

Đặt trên V1.6.1.4. Chỉ thay đổi AndroidMultiDeviceV1/Core/ProfileUpdates.cs.

## Nguyên nhân
Trong bản trước, trạng thái SAVE_PENDING được lưu TRƯỚC khi click Save & post. Nếu WebDriver không xác nhận click trong 15 giây, code trả lỗi mà KHÔNG kiểm tra avatar thực tế. Việc đổi avatar trên điện thoại vẫn có thể đã thành công.

## Bản sửa
- Sau thao tác Save & post (có hoặc không có phản hồi click), chỉ ĐỌC avatar trên Profile. Không tự click Save & post lần thứ hai.
- Xác nhận hai lần liên tiếp với similarity >= 0.88 mới ghi PhotoVerified=true và PhotoChangeState=VERIFIED. Nếu không thể xác minh, tiếp tục giữ SAVE_PENDING và không thử lại thao tác đăng.
- Khi khởi động lại với SAVE_PENDING, đọc avatar trước và không quay lại Gallery/Save & post.
- Không thay đổi lựa chọn ảnh, kho tài khoản, quản lý nhiều máy, hồ sơ tên/bio.

## Cài đặt
1. Đóng tool, sao lưu dữ liệu %LOCALAPPDATA%\AndroidMultiDeviceV1 (đặc biệt profile-update-records.json).
2. Giải nén bản vá vào thư mục dự án V1.6.1.4, ghi đè AndroidMultiDeviceV1/Core/ProfileUpdates.cs.
3. Chạy BUILD_RUN.bat.
4. Khi thử trên Phone 01, đọc log `Ảnh: ... SAVE_PENDING` và `điểm avatar ...`; không bấm Đặt lại/Xóa lịch sử để ép đăng lại ảnh.

## Giới hạn
Bản vá CHƯA được biên dịch bằng .NET trên Windows hay thử với XCloudPhone. Nếu Profile avatar selector không hoạt động, xác minh sẽ giữ SAVE_PENDING; cần đọc XML phần avatar để sửa selector. Điểm ảnh từ camera overlay Edit profile không phải bằng chứng lưu thành công; phải kiểm tra trên Profile.
