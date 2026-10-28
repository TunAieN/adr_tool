AUTO PREFLIGHT HOTFIX V1.3 -- TỰ PHỤC HỒI PHIÊN GIÁN ĐOẠN

NGUYÊN NHÂN CŨ:
Start() chỉ lấy AVAILABLE, nhưng khi tool dừng giữa phiên, account bị đổi thành NEEDS_REVIEW.
Vì vậy Start trả về false TRƯỚC khi Appium được tạo, dù thiết bị ONLINE.

GIẢI PHÁP V1.3:
- Giữ nguyên Device Worker, SessionReconciler, AccountVault, Multi-Device.
- Tài khoản phiên gián đoạn có trạng thái INTERRUPTED (mới).
- Tự nhận biết 2 mẫu LastResult gián đoạn V1.2 đã lưu ở NEEDS_REVIEW.
- Start ưu tiên kiểm tra phiên gián đoạn TRƯỚC AVAILABLE; chỉ khi TikTok UI
  chứng minh được đúng handle mới cho ACTIVE và tính giờ.
- Nếu đã logout, chạy quy trình login bình thường; nếu không xác định danh tính,
  không có handle hoặc yêu cầu bảo mật thì dừng WAIT_REVIEW/WAIT_VERIFY.
- Tài khoản NEEDS_REVIEW vì lỗi khác, RESTRICTED/DISABLED không được mở lại.
- Thông báo lỗi Start hiện rõ lý do (thay vì message chung).
- Lưu dữ liệu vault hiện có, không thay đổi định dạng hoặc mật khẩu mã hóa.

CÀI ĐẶT:
1. ĐÓNG tool, sao lưu code và %%LOCALAPPDATA%%\AndroidMultiDeviceV1.
2. Giải nén patch vào thư mục gốc chứa BUILD_RUN.bat.
3. BUILD_RUN.bat. Không xóa account-vault.json.
4. Mở kho, quét ADB, kiểm tra TikTok handle, chọn Phone 01 -> Start máy.
5. Đọc log PREFLIGHT -> nhận diện đúng handle/login.

CẢNH BÁO:
Chỉ xử lý gián đoạn, không tự vượt 2FA/CAPTCHA/hạn chế nền tảng.
Code đã kiểm tra cấu trúc, CHƯA biên dịch thử trên Windows hay kiểm thử thiết bị thật.
