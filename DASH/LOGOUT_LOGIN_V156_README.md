# V1.5.6 — Logout → Google Login theo UI thực tế

## Mục tiêu

Khi Phone 01 đang đăng nhập một username **khác** với username của tài khoản được phân công, chỉ đăng xuất tự động nếu bộ nhận diện xác minh được username hiện tại thuộc **kho tài khoản được phép của CHÍNH Phone 01**. Không đăng xuất người dùng chưa rõ danh tính, không bỏ qua CAPTCHA/2FA hoặc hạn chế tài khoản.

## Những thay đổi

- `profileMenu` ưu tiên `//android.widget.Button[@content-desc="Profile menu"]`.
- `settingsPrivacy` ưu tiên `//android.widget.Button[@content-desc="Settings and privacy"]`.
- `logout` ưu tiên `//android.view.View[@content-desc="Log out"]`.
- `logoutConfirm` ưu tiên `//android.widget.Button[@content-desc="Log out"]`.
- Bước **xác nhận lần hai là bắt buộc**; nếu không thấy thì dừng mà không đăng nhập tiếp.
- Chờ TikTok trở lại foreground rồi tìm màn hình Profile chưa đăng nhập sau logout (giới hạn chờ, không dùng delay mù).
- Từ Home, bấm Profile theo XPath cũ, sau đó bấm Log in `//android.widget.Button[@resource-id="com.ss.android.ugc.trill:id/z78"]`.
- Bấm Continue with Google bằng XPath đã cấu hình, sau đó giữ nguyên luồng Add another account → email → Next → password → Next → I agree → Don't allow (nếu có).
- Không xác nhận danh tính TikTok bằng `source.Contains(username)`; SessionReconciler phải kiểm tra handle chính xác.

## Cài bản vá vào dự án V1.5.1 Profile Foreground Fix

1. Đóng tool, sao lưu thư mục mã nguồn và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén **PATCH** vào thư mục dự án chứa `BUILD_RUN.bat`.
3. Ghi đè đúng hai file:
   - `AndroidMultiDeviceV1/Core/LoginWorkflow.cs`
   - `AndroidMultiDeviceV1/Config/selectors.json`
4. Chạy `BUILD_RUN.bat`; giữ Appium Server và ADB hoạt động. Trước khi Start, không để Appium Inspector dùng chung máy.
5. Test trên **1 máy** trước bằng hai tài khoản thử nghiệm đã xác nhận handle và thuộc kho thiết bị.

## Hành vi kiểm tra

- Đúng username hiện tại → không logout, giữ phiên.
- Username khác **được xác minh và được phép** → hai bước Logout → kiểm tra đã logout → Profile → Log in z78 → Google Login của tài khoản mục tiêu.
- Username khác nhưng không thuộc kho đã phê duyệt, hoặc không đọc được → `WAIT_REVIEW`.
- Chưa thấy nút xác nhận logout, chưa thấy màn hình login, mất kết nối hoặc UI đổi → dừng; không chuyển sang tài khoản khác.
- Security challenge/restricted → tạm dừng, không tự xoay tài khoản để vượt giới hạn.

## Bảo mật và kiểm thử

- Không có thay đổi schema `account-vault.json` hoặc kho nhiều điện thoại. Không xóa dữ liệu app Android.
- Giới hạn thời gian chờ mang tính kỹ thuật; chức năng cần kiểm thử trên TikTok/Google phiên bản thực tế.
- Không ghi username/mật khẩu trong log chẩn đoán. Tránh bật HTTP debug Appium vì body có thể chứa mật khẩu khi nhập.
- **Chưa thể build WinForms .NET trên Windows/XCloudPhone trong môi trường hiện tại**. Chỉ kiểm tra nguồn, JSON và ZIP. Nếu build lỗi, cung cấp log CMD (che mật khẩu).
