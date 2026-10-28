# Android Multi-Device V1 — Google Account Chooser Login Patch

This revision is based on **ANDROID_MULTI_DEVICE_V1_DEVICE_GRID_FIX** and retains its WinForms layout, device registry and encrypted account vault.

## What's changed

- `Continue with Google` primary XPath added using the actual TikTok ComposeView hierarchy.
- Detect **Choose an account** before interacting with Google identity screens.
- Never click saved Google accounts in the chooser; click **Add another account**.
- Wait for Google's email and password screens, focus the field hit target, prefer a focused `EditText`, and send only the account assigned to that phone.
- Use the supplied **Next** selectors, **I agree** and the exact Android **Don't allow** permission button where present.
- Validate Google package / trusted Google URL before typing. Other prompts (CAPTCHA, 2FA, account security checks) require a human.
- Wait by **UI state**, not by a fixed list of blind delays. Each stage has a timeout and a non-secret log line.
- Retain human verification of TikTok identity if no expected TikTok handle is set. A success response from Google alone is *not* proof of the correct TikTok account.
- Update `SetTextAnyAsync` to try an alternative locator when a TextView cannot receive text, instead of treating it as an editable field.

## Apply the patch

1. Close V1 Manager. Keep a backup of the current project and vault data.
2. Copy these files into your existing project at the same relative paths:
   - `AndroidMultiDeviceV1/Core/LoginWorkflow.cs`
   - `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`
   - `AndroidMultiDeviceV1/Config/selectors.json`
3. Run `BUILD_RUN.bat` in the **existing project folder**.
4. Confirm ADB and Appium server are running, choose Phone 01 and an assigned testing account, and start it.
5. If Google requires 2FA/CAPTCHA, complete it on the device; the worker automatically observes the UI for up to 3 minutes. The Manager does not have a login-confirm button.

## Security and limitations

- This patch is intended for accounts that the operator is authorized to access, not for mass spam, lockout circumvention, or account evasion.
- Passwords travel to the connected cloud Android device and through the local Appium server. **Appium HTTP request logs may contain typed passwords.** Prefer running Appium with reduced logging (`appium --log-level warn`) on production machines and protect the server and device.
- Never import real credentials into untrusted devices. The source TXT may contain passwords in plaintext; store/delete it appropriately after import.
- The user's Google email/password XPaths point to `TextView` nodes; depending on the Android/Google version these may only be focus targets. If no editable control is exposed, the manager stops for manual inspection rather than typing blindly.
- No Android hardware or Windows .NET toolchain is available in this environment, so compilation and end-to-end Android login have **not** been confirmed here.
- UI structures, selectors, Google consent and Android permissions can vary between Google Play Services versions, locales, and TikTok versions.

## New: Unassign an account from a phone

- In **KHO TÀI KHOẢN**, select one or more rows and click **Gỡ khỏi máy**.
- Confirm the list of accounts and the previously assigned phones.
- The account is **not deleted** from the encrypted vault. Its `DeviceSerial` is cleared and the table shows **Chưa gán**. It can be assigned again later.
- Passwords, expected TikTok handles, last result and security state remain unchanged.
- The tool refuses to unassign an `ACTIVE` / `RESERVED` account or an account assigned to a device with a running worker. Stop the device and wait for the worker to end first.
- `NEEDS_REVIEW` / `RESTRICTED` statuses are preserved (not treated as login-ready).
- Unassigning does **not** remotely log the TikTok app out of the phone.

### Apply this incremental patch

Close the manager, back up the project and account vault, then overwrite:

- `AndroidMultiDeviceV1/MainForm.cs`
- `AndroidMultiDeviceV1/Core/Storage.cs`

Run `BUILD_RUN.bat`. No change to the existing `account-vault.json` storage format or schema.

> Note: this source patch has not been built on Windows in the current environment.

## V1.1 — TikTok State Manager (phương án B, cập nhật)

Bản này phát triển từ `ANDROID_MULTI_DEVICE_V1_UNASSIGN_FULL`, giữ nguyên
`AccountVault`, `settings.json`, cơ chế nhiều thiết bị, phân/gỡ kho tài khoản và
luồng đăng nhập Google hiện tại.

**Trước khi Start:**

1. Mở kho và kết nối điện thoại. Điện thoại phải Online trên ADB.
2. Với **mỗi** tài khoản định dùng, chọn tài khoản trong bảng và bấm
   `TikTok handle` để nhập handle **đúng như trên Profile**, không phải email Google.
3. Kiểm tra XPath tại `Config/selectors.json`: `profileHandle` phải trỏ tới
   handle của CHỦ tài khoản trên trang Profile, và `profileLoggedInMarker` phải
   trỏ tới nút `Edit profile`/`Sửa hồ sơ`. Các selector mặc định chỉ là
   phương án thử ban đầu: nếu ứng dụng không cung cấp định danh ổn định, máy sẽ
   dừng `WAIT_REVIEW` thay vì đoán.
4. Bấm `Start máy`/`Start tất cả`: tool **tự động kiểm tra TikTok** trước khi login.
   Nút `Chẩn đoán UI` chỉ dành cho chẩn đoán tùy chọn khi thiết bị rảnh.
5. Không mở session Appium Inspector trùng với worker của cùng điện thoại.
   Sau khi người dùng hoàn tất 2FA/CAPTCHA (nếu có) trên điện thoại, tool tự đọc
   lại Profile trong tối đa 3 phút, không phải bấm nút kiểm tra trong Manager.

**Chính sách tự động đăng xuất khác tài khoản**:

- Chỉ đăng xuất nếu `TikTokStateDetector` xác minh đúng một handle trên trang
  Profile của chủ tài khoản và handle đó ánh xạ duy nhất tới tài khoản đã gán
  trên CHÍNH điện thoại, có trạng thái AVAILABLE hoặc COMPLETED.
- Nếu đã đúng handle mục tiêu: giữ nguyên login, không đăng nhập lại.
- Nếu đang logged out: thực hiện Google Login của bản V1.
- Chỉ sau khi xác minh logout thành công mới cho phép login tài khoản mục tiêu.
- Nếu Google/2FA/CAPTCHA, handle không rõ, UI không xác định: dừng máy để
  kiểm tra, không đoán/không đăng xuất. Khi có cảnh báo khóa tài khoản: dừng
  và đánh dấu RESTRICTED, không tự chuyển sang tài khoản khác.
- Nếu UI TikTok không nhận diện được, được thử `am force-stop` rồi mở lại
  **tối đa một lần** (không xóa dữ liệu ứng dụng, không `pm clear`).
- Sau khi phiên hoàn tất, kiểm tra lại đúng tài khoản và xác minh logout.
- File `session-checkpoints.json` chỉ giữ **ID, serial, trạng thái và thời điểm**;
  không chứa mật khẩu và không phải bằng chứng đăng nhập khi khởi động lại.

**An toàn:** Không dùng tool để né hạn chế nền tảng. Các bước đăng nhập Google
trong bản cũ cần kiểm thử riêng; Google có thể yêu cầu xác minh/2FA.
Không ghi credentials vào log, hạn chế Appium HTTP logging và không gửi file
`account-vault.json` hoặc TXT chứa mật khẩu cho người khác.

### Cài bản vá lên dự án cũ

Sao lưu `E:\...\AndroidMultiDeviceV1` và
`%LOCALAPPDATA%\AndroidMultiDeviceV1` trước khi thay mã nguồn. Đóng V1,
chép đè các file từ ZIP patch đúng cấu trúc thư mục rồi chạy `BUILD_RUN.bat`.
Không chép đè `account-vault.json` hoặc `settings.json`.

### Trạng thái kiểm thử

Các file và JSON đã được kiểm tra cấu trúc trong môi trường tạo gói, nhưng
môi trường này không có Windows .NET SDK/Appium/XCloudPhone để kiểm tra build
và chạy trực tiếp. Hãy chạy `BUILD_RUN.bat` trên Windows và gửi log nếu có lỗi.


## V1.2 — TỰ ĐỘNG preflight khi bấm Start (không cần bấm nút kiểm tra)

- `Start máy` hoặc `Start tất cả` sẽ tự tạo Appium session cho từng máy, tự kiểm tra TikTok Profile, so khớp handle và thực hiện nhánh an toàn tương ứng.
- Nút `Chẩn đoán UI` chỉ để chẩn đoán khi máy không có worker, không phải điều kiện chạy.
- Đã bỏ nút `Kiểm tra lại login`: sau khi Google trả về trạng thái chưa xác minh, worker tự đọc lại giao diện trong tối đa **3 phút**, mỗi lần chờ khoảng **5 giây**. Người dùng chỉ cần thao tác **trên Google/TikTok** nếu có 2FA/CAPTCHA; không cần bấm xác nhận trong Manager.
- Nếu thấy tài khoản bị hạn chế, tài khoản khác hoặc hết thời hạn mà chưa có bằng chứng đúng handle: dừng riêng máy, ghi trạng thái rõ ràng. Không xóa data TikTok và không vượt các bước bảo mật.
- Hệ thống không thể tự suy ra TikTok handle từ email Google. Vẫn cần cấu hình handle chuẩn và các UI selectors phải hoạt động.
- Còn cần chạy `BUILD_RUN.bat` và kiểm thử trên Windows/XCloudPhone thực tế.


## Cập nhật V1.4 – Import Username / TXT / Excel (thay thế hướng dẫn nhập handle thủ công của V1.1)

- Định dạng TXT mới (bắt buộc đủ 3 trường): `username|email|password`, mỗi dòng một tài khoản.
- Excel `.xlsx`: sheet đầu có tiêu đề `Username`, `Email`, `Password` (3 cột này có thể đổi thứ tự), hoặc không tiêu đề và điền lần lượt cột A/B/C. Cũng đọc được file một cột chứa `username|email|password` theo từng dòng.
- Tên TikTok được chuẩn hóa: có thể nhập `@username`, lưu và so sánh dưới dạng `username`.
- `username` nhập vào `ExpectedTikTokHandle` dùng cho State Manager; `email` và `password` dùng cho Google Login.
- Tài khoản đã lưu bằng bản V1.3 vẫn được giữ; nhập bản 3 trường cùng email sẽ bổ sung username nếu trước đó thiếu, không đổi mật khẩu hoặc trạng thái tài khoản. Nếu email có username khác hoặc username trùng với email khác, dòng đó được báo không hợp lệ/xung đột.
- Công thức Excel không được thực thi; cột Password cần đặt định dạng **Text**, nhất là mật khẩu dạng số hoặc bắt đầu bằng số 0.
- File `.xls` cũ: mở Excel, Save As `.xlsx` rồi nhập. Không đặt file Excel chứa mật khẩu ở thư mục chia sẻ công cộng; xóa bản nguồn không cần thiết sau khi nhập.
- Chỉ có thể xác nhận tool hoạt động sau khi biên dịch và kiểm thử trên Windows với các phiên TikTok/Google được phép sử dụng.


## V1.5 — Xóa vĩnh viễn tài khoản khỏi kho mã hóa

- Trong **KHO TÀI KHOẢN**, chọn một hoặc nhiều dòng rồi bấm **Xóa khỏi kho**.
- Khác với **Gỡ khỏi máy**, thao tác này xóa bản ghi trong kho: username TikTok,
  email Google, mật khẩu, phân công và trạng thái đi kèm; không xóa tài khoản thật trên mạng.
- Hộp thoại hiển thị các bản ghi sẽ xóa; nút mặc định là **No** để tránh nhấn nhầm.
- Chặn nếu bất kỳ tài khoản được chọn đang `RESERVED` hoặc `ACTIVE`, hoặc điện thoại
  được gán có worker/phiên Appium chẩn đoán đang chạy. Chọn nhiều dòng có tính nguyên tử:
  một dòng bị chặn thì không xóa dòng nào.
- Start và Xóa cùng dùng một khóa điều phối để tránh chạy đua giữa kiểm tra và lưu.
- Thao tác xóa không đăng xuất TikTok và không can thiệp vào những điện thoại không liên quan.
- File vault được ghi mã hóa trước khi cập nhật danh sách tài khoản trong bộ nhớ;
  tham chiếu checkpoint được dọn theo khả năng tốt nhất.
- Không có công cụ Undo; sao lưu trước khi áp dụng nếu cần. File nguồn TXT/Excel và
  các bản sao lưu độc lập vẫn cần được quản lý riêng.
- Bản V1.4 nhập `username|email|password` qua TXT/Excel vẫn giữ nguyên.

**Cài bản vá:** đóng tool, sao lưu dữ liệu; ghi đè 4 file được liệt kê trong
`DELETE_ACCOUNT_V15_README.txt`, sau đó chạy `BUILD_RUN.bat`.

**Chưa xác minh trên Windows:** dự án cần được biên dịch và kiểm tra thao tác xóa thật
trong môi trường .NET 8/WinForms của người dùng.
