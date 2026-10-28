# Android Multi-Device Manager V5.9.2 — TikTok Web / Chrome CDP riêng

## Nguồn và phạm vi

Đã chuyển module từ `AndroidWork(1).zip` sang source `AccV591.zip` (V5.9.1), không thay file Login, CAPTCHA, StateManager, DeviceOrchestrator, các lớp quản lý tài khoản và Phase 5.

**Cách bật**: Cấu hình LIVE -> Nguồn LIVE -> Nguồn tìm -> `TikTok Web — Chrome profile CDP riêng` -> Lưu. Mặc định vẫn là `TikTok Search trên điện thoại (Appium)` để không thay hành vi cũ.

- Chrome tự mở profile riêng `%LOCALAPPDATA%\AndroidMultiDeviceV1\chrome-live-cdp-profile` (có thể dùng lại profile CDP đã tồn tại).
- CDP lắng nghe tại `127.0.0.1`, port do Chrome chọn tự động. Không mở hoặc chuyển tab trong Chrome cá nhân.
- Web search `/search/live?q=...` theo keyword đã cấu hình. Lấy username từ link `/@username/live`.
- Viewer phải có SVG *người* sát số; SVG Like bị loại. Giới hạn Viewer lấy từ bộ lọc LIVE hiện có.
- Danh sách được sắp Viewer giảm dần, chuyển từng username sang Android để tìm chính xác tài khoản, nhấn **card 1** rồi xác minh username và Viewer thật trong phòng.
- Nếu nguồn Web/Android không xác minh được, không thay bằng Viewer chưa tin cậy. Sau khi từ chối phải về lưới LIVE có chứng cứ; nếu màn hình không chắc chắn thì phục hồi bằng supervisor chứ không nhấn mù.
- Hàng đợi CDP được bảo vệ bởi khóa tuần tự chung; các worker Android vẫn dùng ADB/Appium/systemPort riêng.
- CAPTCHA/đăng nhập trên Chrome profile riêng cần được người dùng hoàn tất hợp lệ; không vượt qua bảo mật.

## Log cần kiểm tra

`CHROME_CDP_PROFILE_CREATED`, `CHROME_CDP_ATTACH`, `CHROME_CDP_READY`, `CHROME_CDP_READ`, `CHROME_CDP_QUEUE_SAVED`, `CHROME_CDP_QUEUE_ITEM`, `TARGETED_FIRST_CARD_PICK`, `PHASE3 LIVE_HOST_MISMATCH`, `TARGETED_FIRST_CARD_RESULT`.

## Nâng cấp

- Bản PATCH dành riêng cho source `AccV591.zip`/V5.9.1; giải nén lên thư mục gốc chứa `DASH`, chấp nhận ghi đè.
- Sao lưu `settings.json`, `account-vault.json`, `live-manager-settings.json` trước khi thay mã nguồn. Không xóa Chrome profile CDP riêng nếu muốn tiếp tục phiên đăng nhập.
- `BUILD_RUN.bat` để biên dịch trên Windows .NET 8; bản ZIP này chỉ được kiểm tra bằng kiểm tra source/hồi quy trong môi trường hiện tại, **chưa build hay chạy end-to-end trên Windows/XCloudPhone**.

## Lưu ý

Cùng một máy tính dùng **một Chrome CDP profile riêng** và một hàng đợi truy cập tuần tự. Dữ liệu đọc từ Web không thay bằng chứng xác minh trên Android. Selector SVG TikTok có thể thay đổi theo phiên bản website; nếu `viewerSvgAccepted=0`, cần kiểm tra lại DOM/SVG, không tự chuyển sang số Like.
