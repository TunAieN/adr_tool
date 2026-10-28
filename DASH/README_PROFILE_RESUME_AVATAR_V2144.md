# V2.1.44 — Resume Avatar after manual handle correction

## Lỗi từ log
Preflight đã xác minh đúng username của tài khoản được chọn nhưng `ProfileUpdateWorkflow.ExecuteAsync` dừng trước `PrepareEditProfileAsync` do `profile-update-records.json` vẫn giữ nhãn username cũ trong cùng AccountId. V2.1.43 chỉ chấp nhận nhãn cũ nếu vault chứa `PreviousVerifiedTikTokHandle`, còn khi người dùng sửa handle bằng UI thì trường này bị xóa theo thiết kế.

## Cách sửa
- Nếu vault có bằng chứng rename đã xác minh: sửa **nhãn** ledger cũ, giữ nguyên trạng thái của từng bước.
- Nếu handle được sửa thủ công: chỉ nối lịch sử khi tài khoản cùng GUID và thiết bị đang hoạt động, handle không trùng với account khác, lịch sử Name/Bio đều `Verified`, trên cùng trang `Edit profile` đọc username ổn định hai lần và Name/Bio hiện tại khớp chính xác với dữ liệu đã lưu.
- Không thay đổi handle trong vault, không nhập lại Name hoặc Bio đã `Verified`.
- Khi `PhotoVerified=false`, tiếp tục luồng Gallery cũ, dùng lại ảnh dự trữ nếu có.
- Nếu thiếu chứng cứ hoặc sai lệch: dừng và giữ nguyên dữ liệu, không tự chuyển/bỏ qua tài khoản khác.

## Log bổ sung
`PROFILE_HISTORY_HANDLE_MISMATCH`, `PROFILE_HISTORY_SCREEN_CHECK`, `PROFILE_HISTORY_REBOUND`, `PROFILE_BIO_ALREADY_VERIFIED`, `PROFILE_PHOTO_RESUME`.

## Giới hạn
Đã kiểm tra cấu trúc mã nguồn offline. Chưa chạy `dotnet build` trên Windows / Cloud Phone. Người dùng cần chạy `BUILD_RUN.bat` và gửi log nếu Gallery/các bước khác thất bại.
