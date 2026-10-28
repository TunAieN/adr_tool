# V2.1.60 – TikTok Search không có tab LIVE: tự mở lại tối đa 4 lượt

## Trường hợp thực tế

Sau khi tìm một từ khóa, TikTok đôi khi chỉ hiện Top / Videos / Photos / Users ... nhưng không hiện category tab LIVE. Ảnh chụp có thể vẫn có nhãn LIVE trên video hoặc người dùng; đó KHÔNG phải tab LIVE.

## Luồng đã sửa

1. Tìm từ khóa như trước, kiểm tra category LIVE trong thanh Search; nếu tab ở ngoài màn hình thì vuốt ngang trong `HorizontalScrollView q1m`, tối đa 3 lần như V2.1.52.
2. Nếu không thấy LIVE: chờ thêm một nhịp và đọc XML mới. Nếu tab vừa xuất hiện, nhấn ngay mà không đóng ứng dụng.
3. Nếu vẫn không thấy LIVE, chỉ khi foreground đúng `com.ss.android.ugc.trill` và không có Login/OAuth/CAPTCHA/Onboarding: dùng ADB `am force-stop` TikTok **không xóa dữ liệu**, sau đó `monkey -p com.ss.android.ugc.trill 1` để mở lại **đúng điện thoại**.
4. Chờ TikTok sẵn sàng và thực hiện lại chuỗi Search → nhập từ khóa → Submit → tìm/vuốt tab LIVE.
5. Tối đa **4 lượt tìm kiếm cho mỗi từ khóa**, tính cả lượt đầu; tương ứng tối đa **3 lần khởi động lại ứng dụng**. Khi tab LIVE được xác nhận `selected=true` + `GridView`, DỪNG retry và bắt đầu đọc card.
6. Nếu hết 4 lượt hoặc có màn hình không an toàn, dừng, lưu ảnh và XML; không nhấn video trong tab Top để thay thế.

Mọi lượt thử vẫn chịu giới hạn thời gian/từ khóa và thời gian toàn đợt. Lệnh Stop của người vận hành vẫn hủy các lượt thử. Chỉ áp dụng retry khi tab thật sự KHÔNG TỒN TẠI / không tìm thấy sau vuốt ngang; không thử lại khi đã thấy tab nhưng selected không được xác minh, hoặc đang ở màn hình bảo mật.

## Log cần quan sát

```text
LIVE_SEARCH_FLOW_ATTEMPT: 1/4
LIVE_SEARCH_TAB_NOT_FOUND: ...
LIVE_SEARCH_MISSING_TAB_RETRY: 1/4
LIVE_SEARCH_APP_RESTART_REQUESTED: afterAttempt=1/4
LIVE_SEARCH_APP_RELAUNCH_REQUESTED: nextAttempt=2/4
LIVE_SEARCH_APP_RELAUNCH_READY: nextAttempt=2/4
LIVE_SEARCH_FLOW_ATTEMPT: 2/4
LIVE_SEARCH_TAB_FOUND: ...
LIVE_SEARCH_TAB_SELECTED: selected=true; grid=True; category=LIVE.
LIVE_SEARCH_FLOW_SUCCESS: attempt=2/4
```

Nếu không thành công: `LIVE_SEARCH_RETRY_LIMIT_REACHED: 4/4` và `LIVE_SEARCH_BATCH_STOP`.

## Giữ nguyên

- OCR và lấy mẫu 8 card (4+2+2), PHASE2, PHASE3.
- Các module PHASE5/comment được **giữ nguyên 100%**, không sửa cơ chế gửi.
- Không `pm clear`, không xóa cache, không can thiệp tài khoản, không tự thao tác trên màn hình bảo mật.
- Không sử dụng lại tệp build .exe cũ trong ZIP.

## Cách chạy

Giải nén ZIP vào một thư mục mới, chạy `BUILD_RUN.bat` trên Windows có .NET SDK 8, Appium và kết nối ADB như trước.

## Mức kiểm thử

Đã rà soát luồng mã, cú pháp dấu ngoặc C# bằng bộ phân tích từ vựng, JSON và ZIP integrity. **Không có .NET SDK trong môi trường đóng gói nên chưa build Windows và chưa chạy Appium với Cloud Phone thực tế.**
