ANDROID MULTI-DEVICE MANAGER — V5.5 LIVE OWNER PROFILE RECOVERY
Cơ sở: AccV54.zip (Account Lifecycle V5.4)

VÌ SAO SỬA:
- Lần Start LIVE thứ nhất có thể bị PROFILE_NAVIGATION_NOT_CONFIRMED sau 5 Back.
- Lần thứ hai xử lý popup CAPTCHA, hướng dẫn vuốt, menu Share thành công.
- Luồng Start tài khoản vốn có điều hướng phục hồi bằng một lần đóng/mở TikTok không xóa dữ liệu; Start LIVE chưa thực hiện khi detector trả về Unknown PROFILE_NAVIGATION_NOT_CONFIRMED.

SỬA:
- Ở DetectLiveOwnerWithV4PopupRecoveryAsync, nếu detector trả Unknown với PROFILE_NAVIGATION_NOT_CONFIRMED, trước hết quét lại popup mới xuất hiện (V4 handler); nếu đóng được thì tự nhận diện lại Profile.
- Nếu không có popup đang chặn, dùng TikTokStateRecovery.RecoverNavigationAsync(...,forceRestartFirst:true) hiện có để mở lại TikTok MỘT lần, không xóa dữ liệu.
- Nếu giao diện đã sẵn sàng, tự chạy lại V4 popup handler và xác minh Profile trong CHÍNH worker Start LIVE.
- Giữ nguyên fail-safe: màn hình Google/biện pháp bảo mật chưa giải quyết, sai username, lỗi account vẫn dừng; không tự pm clear và không tự coi tài khoản hợp lệ.
- Nếu UI vẫn không nhận diện được sau lần phục hồi duy nhất, ghi lỗi cũ và dừng; không lặp vô hạn.

LOG MỚI:
LIVE_OWNER_PROFILE_LATE_CAPTCHA_DISMISSED
LIVE_OWNER_PROFILE_LATE_POLICY_DISMISSED
LIVE_OWNER_PROFILE_RECOVERY_START
LIVE_OWNER_PROFILE_RECOVERY_RESULT: ready=True/False
LIVE_OWNER_PROFILE_RECOVERY_EXHAUSTED
LIVE_OWNER_PROFILE_RECOVERY_ERROR
LIVE_OWNER_PROFILE_RECOVERY_BLOCKED

KHÔNG THAY ĐỔI:
- LIVE Search, OCR Viewer, PHASE 1-5, LivePopupHandler CAPTCHA gốc
- FAST Preflight, Google Login, pm clear phục vụ account switch/hết phiên
- Bộ đếm vòng đời, account vault, ADB/Appium concurrency và systemPort

HƯỚNG DẪN CÀI PATCH:
1. Dừng worker, tắt tool, sao lưu settings.json và account-vault.json cùng source V5.4.
2. Giải nén P55.zip tại thư mục dự án chứa DASH và ghi đè đúng đường dẫn.
3. Chạy DASH/BUILD_RUN.bat trên Windows .NET 8.
4. Thử Phone 07 từ màn hình phụ/hướng dẫn; xác nhận log RECOVERY_RESULT và LIVE_OWNER_VERIFIED.

ĐÃ KIỂM TRA: static contract tests và các hồi quy liên quan.
CHƯA KIỂM TRA: build C# .NET 8 / chạy Appium trên Windows (SDK không có trong runtime tạo bản vá).
