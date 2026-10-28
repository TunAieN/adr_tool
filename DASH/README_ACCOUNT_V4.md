# Account Lifecycle V4 — cấu hình RIÊNG cho từng máy

## Nâng cấp từ AccountV3 / DeviceV2 FAST

1. **Dừng tất cả worker** trước khi cập nhật (không thay file khi ứng dụng đang chạy).
2. Sao lưu `%LOCALAPPDATA%\AndroidMultiDeviceV1\settings.json` và `account-vault.json` trên Windows. Đây là dữ liệu thật của bạn; không được thay bằng file rỗng hay file từ ZIP.
3. Dùng `PatchV4.zip` nếu dự án đang là AccountV3, hoặc `AccountV4.zip` để có source đầy đủ. ZIP giữ nguyên cấu trúc `DASH/AndroidMultiDeviceV1`.
4. Giải nén, dán đè đúng thư mục tương ứng, chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK.
5. Lần mở app đầu tiên, `Storage.LoadSettings` sao chép **giá trị vòng đời V3 cũ** vào chính sách riêng của mỗi DeviceId đã có. Những máy mới về sau có mặc định 360 phút / tự chuyển / không dùng lại / tự dọn sau 24 giờ.
6. Vào **THIẾT BỊ → chọn Phone → Vòng đời TK**, hoặc **TÀI KHOẢN → Vòng đời TK**, hoặc **CẤU HÌNH → Vòng đời tài khoản**. Chọn đúng máy ở hộp thoại; chỉ máy đó được cập nhật.

## Chính sách tách biệt

- Từng DeviceRecord lưu `AccountLifecycle` trong `settings.json`, bao gồm: `SessionLimitMinutes`, `AutoRotateAccounts`, `NeverReuseUsedAccounts`, `AutoPurgeUsedAccounts`, `PurgeUsedAfterHours`.
- Kho và lịch sử tài khoản vẫn nằm trong `account-vault.json` được mã hóa, phân theo ADB serial, không chuyển account giữa các máy.
- Lúc chọn tài khoản, worker chụp snapshot cấu hình của máy. Thay đổi chính sách giữa phiên **không reset đồng hồ phiên đang chạy**. Chính sách mới áp dụng từ lần giữ tài khoản kế tiếp.
- Giới hạn thời gian chỉ áp dụng với Start tài khoản; Start LIVE trực tiếp giữ cơ chế riêng cũ.
- Chế độ TEST một account không tự chuyển sang account khác khi hết hạn.
- `pm clear` TikTok và Google Play Services khi hết phiên vẫn dùng logic nguyên bản AccountV3.
- Tùy chọn xóa credential khỏi vault chỉ áp dụng cho máy đã bật; `CredentialPurgeDueAtUtc` ghi khi kết thúc phiên được giữ nguyên khi sửa cấu hình sau đó. Khi đã dọn vẫn giữ fingerprint chống nhập lại. Máy đã gỡ khỏi danh sách sẽ không tự xóa credential đang nằm trong vault.
- Chính sách `NeverReuse` phải được kiểm tra riêng cho từng serial khi xem trước, giữ chỗ, Reset và chọn tiếp account.

## Ví dụ

| Máy | Giới hạn | Tự chuyển | Không dùng lại | Tự dọn kho | Xóa sau |
|---|---:|---|---|---|---:|
| Phone 02 | 360 phút | Có | Có | Có | 24 giờ |
| Phone 03 | 180 phút | Có | Có | Có | 48 giờ |
| Phone 04 | 720 phút | Không | Có | Không | — |

## Kiểm tra

`python TEST_ACCOUNT_LIFECYCLE_V4.py` và các `TEST_*.py` là kiểm tra mã nguồn, không thay thế được `dotnet build` hoặc thử nghiệm trên thiết bị.

Cần test trên Windows: (1) đặt Phone 02 2 phút; Phone 03 6 phút, (2) xác minh `ACCOUNT_SESSION_STARTED` đúng hạn riêng, (3) khi Phone 02 hết hạn chỉ Phone 02 pm clear/đổi account, (4) Phone 03 không bị ảnh hưởng, (5) restart app và kiểm tra cấu hình vẫn giữ nguyên. Tránh thử đồng thời trên tất cả thiết bị khi chưa có kết quả 1-2 máy.
