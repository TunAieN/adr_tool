# V2.1.45 – Khắc phục Google chooser & onboarding sau Google Login

Kế thừa V2.1.44, chỉ cập nhật 2 file mã chính:

- `AndroidMultiDeviceV1/Core/LoginWorkflow.cs`
- `AndroidMultiDeviceV1/Core/TikTokStateManager.cs`

## Trường hợp 1: Google Choose an account

- Giữ quy tắc cũ: khi xuất hiện nhãn `Use another account`/`Add another account`, nhấn đúng hàng có nhãn đó, KHÔNG chọn danh tính Google đã lưu theo vị trí.
- Với giao diện nhỏ `Choose an account for TikTok` + `Sign-in options` KHÔNG có `Use another account`, sau khi xác nhận là giao diện Google thực, thử Back **một lần** rồi bấm `Continue with Google` **một lần** để đưa vào luồng cũ.
- Nếu không mở được luồng an toàn thì giữ nguyên trạng thái `NeedsHuman` và log. Không nhấn bừa vào hàng tài khoản đã lưu.

## Trường hợp 2: TikTok onboarding **sau** Google Login

- Bổ sung `CompletePostGoogleOnboardingAsync` ngay sau OAuth/password/Google consent và kiểm tra quyền hệ thống. Dùng lại `HandleFirstLaunchPageAsync` cũ (Terms/Gender/Interests), không thay XPath đã chạy được.
- Mở rộng bộ phân loại trạng thái `TikTokUiState.Onboarding` để vòng đợi và Preflight KHÔNG lẫn trang `Tell us more about yourself` với lỗi điều hướng tới Profile. Chỉ khi Home/Profile hiện ổn định thì chuyển sang xác minh đúng username.
- Khi chưa hoàn tất onboarding, giữ nguyên dữ liệu/tài khoản, không reset lần nữa; không bấm Back 5 lần vào trang giới tính.

## Log mới

- `GOOGLE_CHOOSER_OVERLAY_DETECTED`, `GOOGLE_CHOOSER_REOPEN`
- `POST_GOOGLE_ONBOARDING_DETECTED`
- `Thiết lập TikTok: đã chọn ...`, `... Continue ...`, `... Skip ...`
- `POST_GOOGLE_ONBOARDING_COMPLETE` / `POST_GOOGLE_MAIN_UI_READY`
- `POST_GOOGLE_UI_PENDING` / `POST_GOOGLE_ONBOARDING_PENDING`
- `PREFLIGHT_ONBOARDING`, `PREFLIGHT_ONBOARDING_COMPLETE`

## Giữ nguyên

- V2.1.42 Back tối đa 5 lần cho trang phụ **không phải onboarding**.
- V2.1.43 cập nhật handle sau Name, V2.1.44 hồi phục tiến độ hồ sơ.
- Login Google với đúng account được gán, bảo vệ 2FA/CAPTCHA, và quản lý kho.

## Kiểm thử

1. Dừng Appium Inspector trên Phone 02 và giữ Appium server `4723`.
2. Đảm bảo ADB online cho `adb.xcloudphone.net:25153` (không phải `24187` đã hết hạn).
3. Giải nén, chạy `BUILD_RUN.bat`, mở kho hiện tại và TEST tài khoản gán Phone 02.
4. Sau khi `pm clear`, quan sát luồng popup Google (nếu xuất hiện), email/password/consent, trang Gender/Interests, Home/Profile và so khớp username.
5. Nếu vẫn dừng, gửi log từ `Google:` cho tới `POST_GOOGLE_*`, cùng screenshot/XML của màn hình đang hiển thị.

**Kiểm tra thực hiện:** `python TEST_POST_GOOGLE_ONBOARDING_V2145.py` (offline source contract). Môi trường tạo ZIP không có .NET SDK hoặc Android, nên chưa build hay kiểm thử thật.
