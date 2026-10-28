# ANDROID MULTI-DEVICE V1.6.1.8 — LOGOUT GHOST PROFILE (@null)

## Lỗi đã xử lý

- TikTok có thể hiển thị trang Profile cũ với tên/bio còn trong cache, trong khi username trở thành `@null` ngay sau khi xác nhận Log out.
- `@null` KHÔNG được xem là username xác thực hoặc bằng chứng đăng xuất thành công.
- Trước đây bộ nhận diện coi `null` là chuỗi username hợp lệ nên báo nhầm *tài khoản không thuộc kho*. Sau đó luồng logout chờ màn hình Log in hoặc Welcome back mà không có nhánh phục hồi khi Profile bị kẹt.

## Cơ chế V1.6.1.8

1. Từ chối chuỗi `null` / `undefined` trong bước xác minh TikTok handle.
2. Nhận diện riêng Profile bất thường có `@null` cùng các điều khiển sở hữu Profile.
3. Sau thao tác logout đã được nhấn và xác nhận, nếu Profile `@null` tồn tại: chờ có giới hạn, rồi đóng/mở lại TikTok **một lần** (giữ dữ liệu).
4. Đọc lại màn hình TikTok. Chỉ khi thấy **Welcome back + Add another account** hoặc màn hình login đã xác định mới tiếp tục luồng đăng nhập của tài khoản được phân công.
5. Nếu còn `@null`: dùng nút Home rồi mở vùng bình luận của video hiện tại và focus ô nhập **mà không viết hoặc gửi bình luận**. Nếu xuất hiện login gate, mới tiếp tục; nếu không, dừng riêng máy ở lỗi kỹ thuật có thể kiểm tra lại.
6. KHÔNG tự động logout thêm lần nữa, xóa dữ liệu, chọn tài khoản Google lưu sẵn, hoặc tự tạo bình luận công khai.
7. Một màn hình bảo mật/2FA/CAPTCHA hoặc tài khoản chưa xác minh danh tính làm dừng khôi phục.

## Selector bổ sung

- `homeTab`: tab Home.
- `commentEntry`: `com.ss.android.ugc.trill:id/el_`.
- `commentInput`: `com.ss.android.ugc.trill:id/ekm`.
- **Không có selector gửi bình luận** trong bản vá.

## Cài đặt

- Nguồn: V1.6.1.7.
- Bản vá ghi đè ba file:
  - `AndroidMultiDeviceV1/Core/TikTokStateManager.cs`
  - `AndroidMultiDeviceV1/Core/LoginWorkflow.cs`
  - `AndroidMultiDeviceV1/Config/selectors.json`
- Đóng tool, sao lưu thư mục mã nguồn và `%LOCALAPPDATA%\AndroidMultiDeviceV1` trước khi vá.
- Chạy `BUILD_RUN.bat`.
- Giữ một điện thoại/một tài khoản thử nghiệm, không thay đổi trạng thái tài khoản bằng tay khi worker đang xử lý.

## Hạn chế kiểm thử

- Đã kiểm tra JSON, sự hiện diện của nhánh xử lý, và tính toàn vẹn file ZIP.
- Chưa biên dịch được WinForms bằng .NET 8 hoặc kết nối tới XCloudPhone trong môi trường dựng bản vá.
- Nếu prompt chỉ xuất hiện khi thật sự **gửi** bình luận, bản vá sẽ dừng, KHÔNG tự đăng bình luận lên video khác.
