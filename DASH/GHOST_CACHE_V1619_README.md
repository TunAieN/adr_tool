# ANDROID MULTI-DEVICE V1.6.1.9 — GHOST PROFILE CACHE-ONLY RECOVERY

Bản vá đặt lên nguồn V1.6.1.8, giữ nguyên Multi-Device, kho tài khoản, checkbox, test đúng tài khoản, Google Login, cập nhật hồ sơ.

## Lỗi

Sau khi công cụ xác nhận nút Log out, Profile có thể hiện `@null`, rồi khi mở lại TikTok hiện handle cũ. Cả hai trường hợp KHÔNG chứng minh đã thoát phiên. Tool không được đăng nhập tài khoản kế tiếp nếu phiên cũ còn hiệu lực.

## Chỉnh sửa

- Chỉ trong nhánh logout **đã xác minh tài khoản hiện tại là tài khoản thuộc kho được phép**, nếu giao diện `@null` tồn tại sau logout: đóng/mở TikTok một lần và xác minh lại.
- Nếu giao diện vẫn là `@null` HOẶC hiển thị lại đúng tài khoản cũ, kiểm tra hỗ trợ xóa cache-only bằng `ro.build.version.sdk >= 33` và `pm help` phải thể hiện rõ cú pháp `clear ... --cache-only`.
- Nếu cả hai điều kiện đúng, chạy duy nhất `adb -s <serial> shell pm clear --cache-only com.ss.android.ugc.trill` rồi mở lại TikTok.
- Đọc lại trạng thái. Nếu Welcome back/Login thật thì chuyển sang đăng nhập tài khoản được yêu cầu; nếu vẫn là tài khoản cũ hoặc trạng thái không rõ thì DỪNG với `TechnicalFailure`.
- Không xóa toàn bộ dữ liệu ứng dụng (`pm clear com.ss.android.ugc.trill`), không xóa Google cache và không lặp lại logout vô hạn.
- KHÔNG tự động gửi bình luận `helo` lên video bất kỳ. Điều này có thể đăng nội dung công khai nếu phiên vẫn còn hiệu lực. Nếu cần thử hành vi này chỉ dùng một video thử nghiệm do người vận hành kiểm soát, với xác nhận riêng.

## Hai file thay đổi

- `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`
- `AndroidMultiDeviceV1/Core/TikTokStateManager.cs`

## Cài đặt

1. Đóng tool, sao lưu dự án V1.6.1.8 và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén bản vá giữ nguyên cấu trúc thư mục, ghi đè 2 file.
3. Chạy `BUILD_RUN.bat`. Test đúng một tài khoản và một điện thoại trước.

## Kiểm tra thủ công xem máy có hỗ trợ cache-only hay không (KHÔNG xóa gì)

`"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe" -s adb.xcloudphone.net:24187 shell pm help`

Tìm `--cache-only` trong phần trợ giúp của lệnh `clear`. Cổng ADB phải trùng cổng hiện tại trên XCloudPhone.

## Giới hạn

- Chỉ kiểm tra logic tĩnh và ZIP; không có .NET SDK Windows/XCloudPhone kết nối để chạy smoke test thật.
- `--cache-only` xóa tệp cache của TikTok, không có nghĩa sẽ giải quyết được mọi lỗi phiên phía máy chủ. Nếu nó không xử lý được, dừng để xác minh thủ công, không lặp vô hạn.
