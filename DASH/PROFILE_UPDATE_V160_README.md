# Android Multi-Device V1.6.0 — Cấu hình cập nhật hồ sơ

## Cài đặt

Dùng bản **V1.5.11 Contacts Permission Fix** làm gốc. Đóng chương trình, sao lưu mã nguồn và thư mục dữ liệu `%LOCALAPPDATA%\AndroidMultiDeviceV1`.

- Với gói PATCH: giải nén chép đè các file trong ZIP vào thư mục dự án V1.5.11.
- Chạy `BUILD_RUN.bat` trên Windows có .NET SDK 8. Không xóa `account-vault.json`.
- Trong thanh KHO TÀI KHOẢN chọn **Cấu hình hồ sơ**.
- Bật **Tự cập nhật** rồi bật độc lập **Tên hiển thị**, **Tiểu sử**, **Ảnh đại diện**.
- Danh sách tên/bio: mỗi dòng là một lựa chọn. Ảnh: chọn folder trên **máy tính Windows**.

## Đã triển khai

1. WinForms dialog 3 tab cho cấu hình toàn bộ thiết bị, không đổi bố cục Manager.
2. Chỉ chạy Profile Update SAU khi `SessionReconciler` xác minh username TikTok tương ứng với tài khoản được gán.
3. Đổi tên: Profile > Edit > tên > xóa > nhập > Save > Confirm nếu có > đọc lại ô tên trên màn hình edit để xác minh.
4. Đổi bio: Edit > Bio > nhập > Save > mở lại Bio để xác minh. Tối đa 80 ký tự trong bản này.
5. Danh sách tên/bio có random hoặc chọn ổn định dựa trên account ID, lưu theo từng tài khoản để không đổi lặp khi chạy lại.
6. Chống trùng ảnh **trên toàn bộ hệ thống**: SHA-256 file + 64-bit difference perceptual hash để phát hiện cả ảnh gần giống đã nén lại; photo assignment được đặt chỗ có lưu bền vững, chống trùng cả khi worker song song.
7. Lọc ảnh đầu vào: JPG/JPEG/PNG/BMP, chỉ nhận ảnh >= 250x250 pixels ở CẢ HAI CHIỀU, xoay EXIF, crop giữa thành 720x720 JPEG 90%; không giả vờ nâng ảnh bé để vượt giới hạn.
8. Đẩy ảnh đã chọn bằng ADB đến `/sdcard/Pictures/TikTokProfileAssets/<accountId>.jpg` trên đúng điện thoại. Thử thông báo Media Scanner.
9. Lưu trạng thái và kết quả các trường trong `%LOCALAPPDATA%\AndroidMultiDeviceV1\profile-update-records.json`; mỗi bước đã được xác minh không được thao tác lặp vô cớ.

## RẤT QUAN TRỌNG — ảnh avatar chưa tự bấm Gallery

V1.6.0 **chưa** có khả năng khẳng định thumbnail trong thư viện TikTok tương ứng với ảnh vừa chuyển lên điện thoại. XPath có chỉ số `(l37)[1]` và `(l37)[2]` chỉ là vị trí, có thể trỏ sang ảnh khác khi thư viện hoặc thứ tự thay đổi. Bản này **cố ý DỪNG AN TOÀN** sau khi chuẩn bị/chuyển ảnh, không thực hiện Next/Confirm/Save & post bừa.

Để hoàn thiện phần ảnh, cần bổ sung thuật toán so khớp thumbnail trong screenshot Appium với file tham chiếu (hoặc selector có tên file/MediaStore ID), kiểm tra chắc chắn đã chọn đúng ảnh và xác minh avatar cuối. Nếu TikTok yêu cầu `Save & post`, chú ý đây có thể là thao tác đăng ảnh công khai, phải kiểm tra nội dung và sự cho phép của chủ tài khoản.

**Không** được coi XPath `//android.widget.Button[@content-desc="Profile photo"]/android.widget.ImageView[2]` là bằng chứng nội dung avatar đã được thay đúng; nó chỉ chứng minh phần tử tồn tại.

## Những thứ cần thử trên Phone 01

- Bật Tên, nhập 1 tên, không bật Bio/Ảnh: xác minh có thể vào Edit, Save, đọc lại tên.
- Bật Bio, nhập 1 bio: xác minh đổi và đọc lại bio. Các selector nhận từ phiên TikTok hiện tại của người dùng, có thể khác trên phiên bản/ngôn ngữ khác.
- Bật Avatar với 2 ảnh đều >=250x250: kiểm tra tool chuẩn bị file 720x720 đúng, đẩy vào máy, lưu dành riêng một ảnh cho tài khoản, rồi DỪNG với lý do thiếu xác minh thumbnail (không click nhầm).
- Test mất kết nối ADB giữa lần chạy: xem bản ghi từng bước, bảo đảm bước đã xác minh không bị làm lại.

## Giới hạn kiểm thử

Đã kiểm tra cấu trúc ZIP/JSON và đường gọi từ `DeviceOrchestrator` sau khi xác thực username. Môi trường tạo mã nguồn không có .NET SDK Windows hoặc XCloudPhone nên **chưa build hay test Appium/TikTok thật**. Các XPath cần xác nhận trực tiếp bằng Inspector. Nên thử bằng tài khoản được phép quản lý và dữ liệu thử nghiệm trước.
