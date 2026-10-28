# V2.1.42 — Preflight điều hướng từ trang con (tối đa 5 Back)

Kế thừa toàn bộ V2.1.41, bao gồm selector Gallery `l3z`/`y3s`, Name/Bio/Avatar, xác minh username giao diện mới/cũ.

## Vấn đề xác nhận từ log

Máy có thể được để nguyên tại **Edit profile**, Gallery, hoặc màn hình con TikTok. Bản cũ chỉ tìm nút Profile ở thanh dưới; nếu không có sẽ chờ hết khoảng điều hướng và báo `PROFILE_NAVIGATION_NOT_CONFIRMED`, với `tapRequests=0`. Người dùng đề nghị quay lại tối đa năm lần rồi kiểm tra Profile sau mỗi lần.

## Thay đổi

- Nhận biết UI TikTok đã tải nhưng đang ở màn hình con; **không mặc định phải là Home/Profile**.
- Khi đang ở màn hình con không có Profile/tab Profile và không có Home/For You, thử quay lại **tối đa 5 lần**. Nếu trang hiện tại hiển thị nút `//android.widget.Button[@content-desc="Back to previous screen"]`, ưu tiên nút đó; nếu không, sử dụng Appium Android Back.
- **Đọc lại XML sau mỗi lần**; ngay khi thấy Profile chủ (`t_k`/`tlh`) hoặc tab Profile thì **ngừng Back**, quay về luồng xác minh username và cập nhật hồ sơ hiện có.
- Không Back trong khi Google Authentication, CAPTCHA, màn hình hạn chế tài khoản hoặc đăng nhập đang được nhận diện. Không Back từ Home/For You feed nếu tab Profile sẵn có hoặc khi app không còn nằm foreground.
- Sau tối đa 5 lần mà chưa tới được giao diện điều hướng, dừng an toàn; không đoán tọa độ và không xem đây là lỗi/khóa tài khoản.
- Phân biệt `PROFILE_RECOVERY_BACK_REQUESTED`, `PROFILE_RECOVERY_STOP`, `PROFILE_RECOVERY_GIVE_UP`, số `backRequests` trong lỗi cuối.

## Kiểm thử trên Windows

1. Giải nén toàn bộ ZIP vào thư mục mới, chạy `BUILD_RUN.bat`.
2. Chắc chắn Phone 02 dùng ADB mới, tắt Appium Inspector trước khi chạy cùng thiết bị.
3. Mở TikTok tới **Edit profile**, giữ nguyên màn hình, sau đó chọn TEST tài khoản trong tool.
4. Dự kiến log: `PROFILE_RECOVERY_BACK_REQUESTED: 1/5`, tiếp đó `PROFILE_PAGE_CONFIRMED` hoặc `PROFILE_RECOVERY_STOP` rồi `PROFILE_TAP_REQUESTED`, cuối cùng `Preflight: ExpectedAccount` nếu username khớp.
5. Thử thêm khi đang ở **Recents** hoặc màn hình con khác; Back tối đa 5 lần, không được vượt quá giới hạn.
6. Thử khi đang ở Home: không Back, nhấn Profile trực tiếp.
7. Thử khi đã ở Profile của chính mình: không Back, xác minh trực tiếp.

**Phạm vi kiểm tra:** cú pháp cấu hình JSON và quy tắc điều hướng được kiểm tra offline; không có .NET SDK/Appium/Android trong môi trường xây dựng để xác nhận build hoặc phiên chạy thật.
