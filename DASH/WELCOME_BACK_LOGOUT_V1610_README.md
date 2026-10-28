# V1.6.1.0 - Welcome back logout recognition fix

## Vấn đề
V1.6.0.9 nhấn xác nhận Log out thành công, nhưng không chấp nhận màn hình
"Welcome back" với các tài khoản đã lưu và nút "Add another account".
Do đó hết 90 giây/35 giây chờ và tài khoản bị gắn NEEDS_REVIEW.

## Bản sửa
- Đọc XML TikTok: chỉ coi Welcome back là LoggedOut khi *đồng thời* có
  nhãn Welcome back và Add another account trong hierarchy TikTok.
- LogoutAsync nhận diện trạng thái trên trước khi tìm nút Log in hoặc click Profile.
- TikTokStateDetector nhận diện sớm màn hình này trong Preflight và lần kiểm tra lại sau logout.
- Google Login chỉ chọn Add another account trên đúng màn hình TikTok;
  ưu tiên selector khớp nhãn, không tự chọn thẻ tài khoản lưu sẵn.
- Giữ nguyên các chặn bảo mật/giới hạn; không thay dữ liệu kho.

## Cài đặt
1. Đóng Manager và sao lưu mã nguồn/dữ liệu.
2. Giải nén PATCH vào thư mục dự án V1.6.0.9 theo đúng cấu trúc,
   ghi đè 3 file Core/TikTokStateManager.cs, Core/LoginWorkflow.cs và Config/selectors.json.
3. Chạy BUILD_RUN.bat.
4. Thử 1 máy và một tài khoản được phép quản lý, theo dõi log.

## Kết quả dự kiến
Logout: đã xác minh trạng thái Welcome back / Add another account; phiên trước đã đăng xuất.
Đã xác minh đăng xuất thành công; bắt đầu đăng nhập tài khoản được gán.
TikTok: Welcome back -> đã chọn Add another account ...

## Giới hạn
Chưa kiểm thử trên XCloudPhone hoặc biên dịch Windows trong môi trường tạo bản vá.
Nếu Google hoặc TikTok đang yêu cầu xác minh/đang hạn chế tài khoản, tool vẫn dừng an toàn.
