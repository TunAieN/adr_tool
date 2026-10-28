# V2.1.48 – LIVE Search: Back/Home recovery

## Vấn đề

Nếu TikTok đang nằm trong một trang con (Edit profile, Gallery, LIVE room hoặc màn hình phụ khác), nút Search `k_g` có thể không xuất hiện. Ở V2.1.47, LIVE Search bỏ ngay từ khóa khi không tìm thấy `k_g`; nhiều từ khóa bị thử mà không có khôi phục giao diện.

## Sửa trong `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs`

- Ưu tiên dùng ô Search `hvp` nếu đã sẵn sàng.
- Nếu không thấy `k_g/hvp`, đọc lại XML, xác minh foreground là TikTok trước mọi tương tác.
- Nếu có **tab Home ở thanh dưới** (nhãn Home / Trang chủ trong vùng dưới trái), chạm đúng bounds động, đọc lại UI và tìm `k_g`.
- Nếu ở trang phụ không có Home, ưu tiên nút XPath `//android.widget.Button[@content-desc='Back to previous screen']`, dự phòng Android Back. **Tối đa 5 lần Back**, đọc lại UI sau mỗi lần, dừng ngay khi có Search hoặc Home.
- Dừng an toàn tại Google, màn hình đăng nhập, onboarding, CAPTCHA/xác minh bảo mật hoặc nếu foreground không thuộc TikTok.
- Không Back khỏi Home khi không thấy nút Search: lưu ảnh XML và lý do lỗi, tránh thoát ứng dụng.
- Nếu không tìm được Search sau khôi phục, dừng cả lượt tìm và không lặp lại 5 Back trên mỗi từ khóa.
- Không sửa luồng quét 8 card / OCR / chọn LIVE / các chức năng khác.

## Log cần xem

```text
LIVE_SEARCH_ENTRY_MISSING
LIVE_SEARCH_RECOVERY_BACK_REQUESTED: 1/5; method=XPath|AndroidBack
LIVE_SEARCH_HOME_TAP_REQUESTED
LIVE_SEARCH_RECOVERY_READY: k_g found ...
```

Nếu lỗi:

```text
LIVE_SEARCH_RECOVERY_STOP: ...
LIVE_SEARCH_NAV_DIAGNOSTIC_SAVED: ...
LIVE_SEARCH_BATCH_STOP: ...
```

Chẩn đoán lưu tại `%LOCALAPPDATA%\\AndroidMultiDeviceV1\\diagnostics\\live-search-navigation-YYYYMMDD-HHMMSS` (`screen.png`, `screen.xml`, `reason.txt`). Hãy che dữ liệu riêng tư trước khi chia sẻ.

## Cách chạy

Giải nén sang thư mục riêng, chạy `BUILD_RUN.bat`, mở kho và kết nối Remote ADB của Phone 02. Có thể để TikTok đang ở Edit profile để thử Search recovery.

## Giới hạn kiểm tra

Kiểm tra tĩnh offline và toàn vẹn ZIP đã hoàn thành. Chưa thể build .NET Windows hoặc xác minh Appium trên Cloud Phone trong môi trường này.
