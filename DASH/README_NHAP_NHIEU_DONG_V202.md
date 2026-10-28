# V2.0.2 — Nhập nhiều dòng trong Thư viện nội dung

Áp dụng trên **Android Multi-Device LIVE GĐ1 V2.0.1** (giao diện gọn).

## Đã sửa

- Bật `AcceptsReturn` cho ô nội dung và ô từ khóa: **Enter xuống dòng**, không kích hoạt nút Lưu cấu hình.
- Ô thư viện hỗ trợ nhập/dán nhiều dòng trực tiếp bằng Ctrl+V, hỗ trợ cuộn ngang/dọc để dễ kiểm tra mỗi nội dung một dòng.
- Thêm nút **Thêm nhiều dòng**: mở cửa sổ dán danh sách nhiều dòng; bấm **Thêm vào danh sách** để **nối vào cuối**, không ghi đè dữ liệu đang nhập.
- Tự đếm dòng, bỏ qua dòng trống; giữ nguyên thứ tự các dòng và không tự loại các nội dung trùng nhau.
- Hỗ trợ cả xuống dòng Windows CRLF, Unix LF, và CR đơn.
- Giữ giới hạn cấu hình hiện tại: tối đa 10.000 dòng, mỗi dòng tối đa 500 ký tự.
- Lưu trong `live-manager-settings.json` như V2.0.1; không thay đổi kho tài khoản hoặc tự gửi bình luận.

## Cài đặt

1. Đóng tool; sao lưu mã nguồn và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Dùng `FULL.zip` giải nén vào thư mục **mới**, hoặc giải nén `PATCH.zip` ghi đè **V2.0.1** (đúng đường dẫn `AndroidMultiDeviceV1/LiveSettingsDialog.cs`).
3. Chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK.
4. Mở `CẤU HÌNH LIVE` → `Thư viện nội dung`.
5. Gõ nội dung 1, Enter, nội dung 2; hoặc bấm `Thêm nhiều dòng` để dán 10–100 dòng một lần, bấm `Thêm vào danh sách`.
6. Bấm `Lưu cấu hình` ở cửa sổ chính, mở lại và kiểm tra bộ đếm / nội dung.

## Kiểm thử đề xuất

- Enter trong ô nội dung tạo dòng mới, không tự lưu / đóng cửa sổ.
- Dán `A\nB\nC` tạo 3 dòng.
- `Thêm nhiều dòng` nối với các dòng cũ thay vì ghi đè.
- `Hủy` khi nhập hàng loạt không thêm dữ liệu.
- `Lưu` rồi mở lại giữ nguyên các dòng.
- Hơn 10.000 dòng hoặc một dòng trên 500 ký tự bị cảnh báo và không thêm hàng loạt.
- `Nhập TXT`, `Xuất TXT` hoạt động như trước.

## Tình trạng xác minh

Đã đối chiếu thay đổi mã nguồn với V2.0.1 và kiểm tra ZIP. Môi trường đóng gói hiện tại không có .NET SDK Windows, do đó **chưa thể biên dịch/chạy WinForms**; cần kiểm thử bằng `BUILD_RUN.bat` trên máy thực tế.
