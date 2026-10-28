# V1.6.0.8 — FIX POST-PREFLIGHT OTHER APPROVED ACCOUNT

Cơ sở: V1.6.0.7 ACCOUNT CHECKBOX.

Lỗi: detector phân loại OtherApprovedAccount bằng kho đã gán, nhưng ReconcileAsync chỉ coi một tập nhỏ trạng thái là đủ điều kiện; NEEDS_REVIEW do lỗi UI/hồ sơ cũ bị loại mà không có log giải thích.

Sửa:
- Đối chiếu chủ sở hữu đã xác minh theo chính xác username và thiết bị.
- Cho phép nhánh NEEDS_REVIEW chỉ khi nguyên nhân là lỗi UI/hồ sơ kỹ thuật thuộc danh sách được kiểm soát; không áp dụng cho CAPTCHA, xác minh bảo mật, banned, restricted.
- Thêm log quyết định trước khi đăng xuất, kể cả khi từ chối.
- Log kết quả ReconcileAsync tổng hợp để không trông như treo.
- Không coi DISABLED là approved trong detector.

Cách cài: Đóng tool, sao lưu thư mục dự án và %LOCALAPPDATA%\AndroidMultiDeviceV1.
Giải nén bản vá vào thư mục mã nguồn V1.6.0.7, ghi đè hai file trong Core, rồi chạy BUILD_RUN.bat.

Hạn chế: chưa biên dịch/kiểm thử Windows và Appium thực tế trong môi trường này. Nếu còn dừng hãy gửi log từ `Preflight: OtherApprovedAccount` và các dòng `Preflight: ...` tiếp theo; không gửi mật khẩu.
