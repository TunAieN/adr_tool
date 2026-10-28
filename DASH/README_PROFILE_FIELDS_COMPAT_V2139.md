# V2.1.39 — Tương thích Edit Profile mới và cũ (Name / Bio / Avatar)

## Xuất phát từ log thực tế
- `PROFILE_UPDATE_EDIT_REUSED` và `PROFILE_UPDATE_HANDLE_RECHECK` đã đúng trên máy mới.
- Lỗi mới xảy ra tại bước mở màn hình sửa Name; Name editor không được xác nhận trong V2.1.38.
- User cung cấp các ID mới: ô sửa tên `lfn`, ô sửa Bio `hw7`, hàng Bio `tvContent`, ảnh `Camera`, `Upload photo`, cấp quyền `permission_allow_all_button`.

## Bản sửa
1. **Giữ nhánh UI cũ**: vẫn có `profileEditButton`, `mtq[1]`, `hvc`, Save, Confirm, Gallery, photo post verification.
2. **Tên trên UI mới**:
   - Tìm hàng Name bằng nhãn `Name` và vùng cha có thể nhấn, hoặc `mud[1]`.
   - XPath `(//android.widget.TextView[@resource-id="com.ss.android.ugc.trill:id/mud"])[2]` được giữ **ở CUỐI danh sách dự phòng** đúng theo thông tin người kiểm thử, nhưng **ảnh Edit profile trước đó cho thấy mục này thuộc hàng Username**. Sau mọi lần click, yêu cầu có đúng trang Name + ô `lfn` (hoặc trang Name cũ + `hvc`) mới cho phép nhập/Save. Nếu là Username, quay lại, không đổi username.
   - Nút xoá `Close` và nút `Save` dùng selector đã có.
3. **Bio trên UI mới**:
   - Mở qua `tvContent` hoặc vùng cha `Bio`; chỉ chấp nhận trang Bio hợp lệ.
   - Nhập/đọc lại `hw7`, dự phòng `hvc` trên UI cũ; dùng nút `Save` cũ.
4. **Ảnh**:
   - Giữ nguyên `Camera` → `Upload photo` → kiểm tra quyền truy cập ảnh → `permission_allow_all_button` → Gallery matcher ảnh gốc → lưu/xác minh.
   - Vẫn yêu cầu `AllowPhotoSaveAndPost` được bật chủ động trong cấu hình ảnh; không tự bật tùy chọn này.
5. **Kiểm soát sai tài khoản**: recheck `mud[2]` ở Edit profile trước khi sửa; các thao tác Save có nhật ký `SAVE_PENDING`, tránh lặp sau lỗi.

## Log bổ sung
- `PROFILE_NAME_EDITOR_READY`
- `PROFILE_NAME_INPUT_VERIFIED`
- `PROFILE_BIO_EDITOR_READY`
- `PROFILE_BIO_INPUT_VERIFIED`
- `PROFILE_NAME_NAVIGATION_STOP` khi mở sai màn hình

## Kiểm thử Windows
- Giải nén ZIP vào thư mục MỚI, chạy `BUILD_RUN.bat`.
- Kết nối đúng Cloud Phone đang thuê, xác nhận `adb devices -l` có trạng thái `device`, đóng Appium Inspector trước khi chạy.
- Mở kho tài khoản cũ, chỉ định Phone 02 với đúng tài khoản, chạy Test.
- Kiểm tra log xác minh username khớp, Name Save/verify, Bio Save/verify, avatar picker/permission/verify.
- Nếu Name không mở sau khi đã thử các locator, gửi screenshot và XML **trang Edit profile** và **trang sau khi nhấn hàng Name**; không thay đổi Username bằng thao tác thử đoán.

Chưa build/kiểm thử trên Windows/.NET hoặc Cloud Phone trong môi trường tạo gói này; chỉ có kiểm tra mã nguồn, JSON và XML giả lập.
