# V2.1.30 — Gửi một bình luận có xác nhận

- Giữ logic tìm kiếm / PHASE2 / PHASE3 từ V2.1.29.
- Sau khi nhận `LIVE_ACCEPTED`, nút **Gửi 1 bình luận** bật trong cửa sổ Tìm LIVE.
- Văn bản lấy mặc định từ dòng đầu tiên trong `CommentDrafts`, có thể sửa trong ô trước khi xác nhận. Không thay đổi `live-manager-settings.json`.
- Cần người dùng bấm nút và chấp nhận hộp thoại xác nhận. Mỗi lượt tìm kiếm chỉ có tối đa một yêu cầu gửi; không có vòng lặp tự gửi hay luân chuyển phòng.
- XPath h42 mở ô bình luận; h7r TextView được thử sau các EditText thực tế; vxc gửi.
- `COMMENT_SEND_REQUESTED` chỉ xác nhận hành động click đã gửi tới UI, không xác nhận bình luận hiển thị trên TikTok. Không thử lại tự động.
- Chưa build hoặc thử trên Android vì không có dotnet/Appium/device trong môi trường này.
