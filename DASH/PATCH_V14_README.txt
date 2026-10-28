V1.4 — Username/Email/Password & Excel import

BAT: không đổi. Kho tài khoản: không xóa.

Chép các file từ bản PATCH vào đúng đường dẫn project:
  AndroidMultiDeviceV1\Core\Storage.cs
  AndroidMultiDeviceV1\Core\ExcelAccountImporter.cs (file mới)
  AndroidMultiDeviceV1\MainForm.cs

Đóng tool trước, sao lưu mã nguồn và %LOCALAPPDATA%\AndroidMultiDeviceV1.
Chạy BUILD_RUN.bat. Mở kho -> Nhập TXT / Excel -> chọn file.
TXT: username|email|password
Excel: Username | Email | Password trong ba cot (sheet dau); hoac mot cot username|email|password.
Sau khi import, gán account vào Phone 01 rồi nhấn Start máy.
Username xác minh danh tính; không suy ra username từ email Google.
Không gửi file có mật khẩu thật cho người khác.
