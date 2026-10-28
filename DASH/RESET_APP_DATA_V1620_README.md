# ANDROID MULTI-DEVICE V1.6.2.0 — TikTok App Data Reset

## Mục đích

Thay **Settings → Log out** bằng xóa toàn bộ dữ liệu ứng dụng TikTok trên *đúng điện thoại* trong trường hợp chuyển từ một tài khoản TikTok **đã xác minh thuộc kho được phép quản lý của máy** sang tài khoản được gán hoặc chọn để kiểm thử. Không đăng bình luận để kích hoạt đăng nhập.

## QUAN TRỌNG — Tác động của `pm clear`

`adb -s <serial> shell pm clear com.ss.android.ugc.trill` xóa **toàn bộ dữ liệu cục bộ của TikTok**: phiên đăng nhập, cache, thiết lập, bản nháp hoặc thông tin chưa đồng bộ. Thao tác này có thể không thể khôi phục. Không xóa tài khoản TikTok trên máy chủ, không xóa tài khoản Google trong Google Play Services, không xóa kho tài khoản/lịch sử cập nhật hồ sơ lưu trên Windows. App TikTok vẫn được cài đặt; giao diện có thể hỏi lại quyền/đồng ý khi khởi động mới.

### Quy trình
1. `Start máy` / `Chạy TK tick` vẫn kiểm tra Profile và username hiện tại.
2. Nếu đang đăng nhập đúng tài khoản đích: **giữ nguyên**, không xóa dữ liệu.
3. Nếu đang đăng nhập tài khoản khác có chính xác 1 bản ghi hợp lệ trong kho của máy: sau khi xác nhận không có màn hình bảo mật, chạy `am force-stop` và `pm clear` **chỉ TikTok** cho serial đó.
4. Chỉ coi lệnh xóa thành công nếu ADB trả về đúng `Success`. Sau đó mở TikTok bằng `monkey -p` và chờ giao diện có thể thao tác.
5. Đọc lại trạng thái TikTok: chỉ khi `LoggedOut` mới chạy Google Login. Nếu trạng thái không rõ, **dừng lỗi kỹ thuật**, không xóa hoặc nhập lại mù quáng.
6. Khi một phiên hết thời gian, dùng cùng cơ chế dọn dữ liệu **chỉ khi** xác minh được username của tài khoản đang chạy.

Không xóa TikTok khi đang thấy CAPTCHA/2FA/challenge, ở màn hình Google, username ngoài kho, hoặc `Unknown/@null` chưa được xác minh chủ sở hữu. Không có chức năng bình luận bất kỳ.

## Cài đặt bản vá

Giải nén bản vá vào thư mục dự án V1.6.1.9, ghi đè:
- `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`
- `AndroidMultiDeviceV1/Core/TikTokStateManager.cs`
- `AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs`

Sao lưu thư mục dự án và `%LOCALAPPDATA%\AndroidMultiDeviceV1` trước khi cập nhật, sau đó chạy `BUILD_RUN.bat` trên Windows với .NET 8 SDK, ADB và Appium đã cài. **Không xóa account-vault.json hay profile-update-records.json.**

## Nhật ký mong đợi

```
Preflight: OtherApprovedAccount
Preflight: đã xác minh tài khoản cũ được phép quản lý; BẮT ĐẦU RESET DỮ LIỆU TIKTOK ...
Chuyển tài khoản: ADB xác nhận pm clear TikTok = Success
Chuyển tài khoản: đã mở TikTok sạch; chờ giao diện ...
Chuyển tài khoản: sau xóa dữ liệu TikTok, giao diện=LoggedOut
Chuyển tài khoản: xác nhận TikTok sạch và chưa đăng nhập; bắt đầu Google Login ...
```

## Giới hạn kiểm thử

Đã kiểm tra cấu trúc mã, đường gọi `pm clear` có giới hạn và ZIP; chưa build trên Windows / kiểm thử XCloudPhone trong môi trường này. Sau `pm clear`, lần đầu TikTok có thể cần thiết lập lại quyền riêng tư hoặc hiển thị onboarding. Tool sẽ dừng thay vì đoán khi chưa nhìn thấy `LoggedOut`.
