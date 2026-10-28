# V2.1.43 – Tự đối chiếu Username sau khi lưu Name

Mã nguồn dựa trên V2.1.42.

- Chỉ hoạt động sau khi tài khoản đã được xác minh ở Preflight và Name đã xác minh lưu thành công.
- Trên cùng Appium session, quay về Edit profile nếu cần, đọc username `mud[2]` hai lần (500 ms), đối chiếu Name trên trang.
- Nếu username không thay đổi: ghi `PROFILE_HANDLE_UNCHANGED`, giữ kho nguyên trạng.
- Nếu username thay đổi: kiểm tra ID tài khoản, thiết bị, trạng thái phiên, handle cũ và xung đột trong kho; cập nhật duy nhất ExpectedTikTokHandle trong vault mã hóa bằng ghi file nguyên tử; đồng bộ nhãn handle ở ProfileUpdateLedger, giữ nguyên email/password/status/assignment.
- Nếu không xác minh được username, Name hiển thị không khớp hoặc trùng account khác: dừng an toàn, không tự sửa kho, không xóa TikTok và không tự chuyển tài khoản.
- **Quan trọng:** Đây không phải chức năng sửa sai preflight cho tài khoản đã thay handle từ phiên trước. Nếu username thực tế đã khác kho TRƯỚC lần Start, Preflight vẫn dừng, người vận hành phải xác minh chủ sở hữu và sửa TikTok handle một lần bằng tính năng hiện có.
- Các selector của giao diện Profile cũ được giữ nguyên; nhánh đồng bộ handle mới chỉ đọc trên Edit profile giao diện `mud[2]`. Không tự đoán username khi giao diện cũ không cung cấp handle ở Edit profile.
- Vẫn cần `BUILD_RUN.bat` trên Windows để xác nhận biên dịch và kiểm thử thật trên Android.

- Giao diện cũ không có `mud`: giữ username đã xác minh từ Profile, không chặn cập nhật tên/bio/ảnh.
- Trường `PreviousVerifiedTikTokHandle` + thời gian cập nhật được lưu cùng vault mã hóa; nếu bị gián đoạn giữa việc lưu vault và lưu ledger, phiên sau có thể đồng bộ lại ledger theo cùng AccountId chỉ khi có bằng chứng đổi handle trong kho.
