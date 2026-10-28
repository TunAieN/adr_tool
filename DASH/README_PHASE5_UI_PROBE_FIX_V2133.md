# PHASE5 V2.1.33 — FIX NHẬN DIỆN TYPE... VÀ KIỂM TRA UI (KHÔNG GỬI)

Nguồn: ZIP `ANDROID_MULTI_DEVICE_PHASE5_COMMENT_OPEN_DIAGNOSTICS.zip`.

## Đã triển khai
- Sửa kiểm thử ô bình luận: phân tích `page-source.xml` bằng XML, ưu tiên `h42`; dự phòng `text="Type..."` / `Type…`.
- Kiểm tra phần tử **cha có `clickable=true`** thay vì coi `TextView clickable=false` là không tồn tại.
- Với mẫu thực tế: h42 `[10,542][159,573]`; cha ilz `[10,535][190,580]`, `clickable=true`.
- Nút **Test mở ô bình luận** trong cửa sổ Tìm LIVE sẽ chỉ nhấn mở **một lần** sau khi xác nhận trạng thái LIVE, sau đó lưu ảnh/XML trước và sau, kiểm tra `h7r`, `EditText`, `vxc`.
- Phân biệt lỗi `COMMENT_OPEN_NO_VISIBLE_TYPE_LABEL`, `COMMENT_OPEN_NO_CLICKABLE_ANCESTOR`, `INPUT_UI_VISIBLE` và `INPUT_UI_NOT_VISIBLE`.
- Không nhập chữ hoặc nhấn nút gửi. Không thực thi `LiveCommentAutoSender` trong giao diện này, không tự gửi bình luận hàng loạt.
- `CommentDrafts`, các thiết lập và Giai đoạn 1–3 giữ nguyên.

## Cách test
1. Giải nén rồi chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK.
2. Nhấn **Tìm LIVE** để hoàn tất Phase 1–3; nếu `LIVE_ACCEPTED`, nhấn **Test mở ô bình luận**.
3. Mở đường dẫn ở dòng `PHASE5 UI_PROBE_DIAGNOSTIC_SAVED`, xem `diagnostic-summary.txt`, `before.xml`, `after.xml` và ảnh chụp tương ứng.
4. Kết quả mong đợi khi mở thành công: `PHASE5 UI_PROBE_RESULT: INPUT_UI_VISIBLE; sent=0`.

## Hạn chế
Không thể kiểm thử Appium/Android trên môi trường này; .NET 8 SDK không có sẵn. Nút kiểm thử chỉ xác nhận trạng thái giao diện, không xác minh khả năng gửi bình luận. Bản đã chỉnh sửa mã nguồn và kiểm tra ZIP, không khẳng định build chạy thành công trên Windows.
