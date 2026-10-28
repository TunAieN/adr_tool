# V1.6.2.4 — Direct Google Login After Fresh TikTok Launch

## Lỗi đã quan sát
ADB `pm clear TikTok = Success`, thiết lập ban đầu xong, điện thoại hiện `Log in to TikTok` với `Continue with Google` nhưng Detector tiếp tục đọc username Profile, dẫn tới `Unknown` sau gần một phút.

## Thay đổi
- Nhận diện chắc chắn màn hình phương thức đăng nhập bằng giao diện TikTok + nhãn `Continue with Google` + `Log in to TikTok` hoặc `Use phone / email / username`.
- Kiểm tra màn hình mới ở các điểm: ngay khi bắt đầu nhận diện, sau khi đợi UI, khi điều hướng Profile và trong vòng chờ đọc username.
- Chuyển thành `LoggedOut` để Reconciler gọi LoginWorkflow (không gọi lại `pm clear`).
- LoginWorkflow chấp nhận màn hình phương thức đăng nhập xuất hiện trực tiếp.
- Ưu tiên selector Google theo nhãn, chuyển XPath Compose vị trí thành dự phòng cuối cùng.
- Không chọn tài khoản Google đã lưu theo chỉ số và không bỏ qua xác minh danh tính/cảnh báo.

## Áp dụng
Đóng tool. Sao lưu bản V1.6.2.3 và kho dữ liệu. Chép đè các file trong PATCH vào gốc V1.6.2.3, rồi chạy BUILD_RUN.bat.

Các file sửa: Core/TikTokStateManager.cs, Core/LoginWorkflow.cs, Config/selectors.json.

## Nhật ký mong đợi
`Preflight UI: Đã thấy Log in to TikTok / Continue with Google` → `Chuyển tài khoản: sau xóa dữ liệu TikTok, giao diện=LoggedOut` → `TikTok: đã nhấn Continue with Google` → các bước Google đã có.

## Giới hạn
Chưa build WinForms .NET 8 hay chạy Appium/XCloudPhone trong môi trường này. XPath và các nhãn có thể thay đổi theo phiên bản TikTok. Luồng chưa xác minh được giao diện sẽ dừng thay vì tự xóa lại dữ liệu.
