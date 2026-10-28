# V1.5.10 — Google account chooser: loại bỏ XPath theo vị trí

## Nguyên nhân đã xác nhận từ log & mã nguồn V1.5.9

`useAnotherAccount` đặt XPath `(RecyclerView/.../container)[2]` làm lựa chọn đầu tiên. Nếu danh sách chỉ có một tài khoản Google đã lưu thì hàng thứ 2 là **Add another account**. Khi có hai tài khoản đã lưu, hàng thứ 2 lại là **tài khoản Google thứ 2**, nên tool chọn nhầm và tự đăng nhập bằng tài khoản đó.

## Thay đổi

* Xóa selector theo thứ tự tài khoản `[2]`/`[3]`. Không đoán số lượng Google account đã lưu.
* `useAnotherAccountLabel`: xác minh có nhãn **Add another account**, **Use another account** hoặc bản dịch tiếng Việt.
* `useAnotherAccount`: click vào chính hàng RecyclerView có phần tử con chứa nhãn đó; có phương án dự phòng là ancestor clickable gần nhất / phần tử có nhãn.
* `LoginWorkflow`: chỉ click khi xác nhận đúng nhãn và đúng ngữ cảnh Google. Sau click, bắt buộc thấy form email Google trước khi coi bước thành công.
* Nếu không thấy nhãn/form email thì dừng an toàn, không chọn bất kỳ tài khoản đã lưu nào, không thử click hàng thứ 2/3.
* Vẫn giữ nguyên Google Login, Multi-Device, logout/Preflight, kho mã hóa, Excel, retry và toàn bộ cấu trúc V1.5.9.

## Cài đặt PATCH

1. Đóng Android Multi-Device Manager và sao lưu cả mã nguồn lẫn `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén `ANDROID_MULTI_DEVICE_V1_GOOGLE_CHOOSER_V1510_PATCH.zip` vào thư mục dự án V1.5.9, ghi đè `Core/LoginWorkflow.cs` và `Config/selectors.json`.
3. Chạy `BUILD_RUN.bat`. Giữ Appium Server và Remote ADB hoạt động. Không mở một Appium Inspector session khác cùng lúc trên máy thử.
4. Test trước với **một điện thoại và tài khoản được phép quản lý**. Quan sát trực tiếp màn hình chooser nếu lần đầu áp dụng.

## Nhật ký kỳ vọng

```text
TikTok: đã nhấn Continue with Google; chờ Google Choose an account / Email.
Google: đã mở biểu mẫu nhập email qua Add another account (không chọn tài khoản lưu sẵn).
Google: đã điền email của tài khoản được phân công.
```

Nếu vẫn bị kẹt, xuất **UI hierarchy của Google chooser** có nhãn Add another account và nhật ký đã che thông tin nhạy cảm. Không gửi mật khẩu hoặc mã xác minh.

## Kiểm thử được thực hiện

Cấu trúc JSON hợp lệ; kiểm tra cú pháp XPath và thử các selector với cây UI giả lập có 0, 1, 2, 3 và 5 tài khoản lưu sẵn, tất cả đều chọn hàng Add another account và không chọn hàng đã lưu. `LoginWorkflow` đã được đối chiếu với mã nguồn V1.5.9. **Chưa thể dotnet build hoặc kiểm thử trực tiếp trên Windows/XCloudPhone tại đây**, cần chạy thử trên máy của anh.
