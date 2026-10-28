# V2.1.50 – Ngăn click nhầm video LIVE trong trang Top

## Nguyên nhân

V2.1.49 dùng selector quá rộng `//android.widget.TextView[@text='LIVE']` và `//*[@content-desc='LIVE']`; Appium có thể click vào LIVE badge/card trên Top, chứ không phải tab danh mục. Thêm nữa, `GridView` có sẵn trên Top, làm trạng thái "đã vào LIVE" bị xác nhận sai.

## Thay đổi

- Chỉ xét tab LIVE nếu nằm trong thanh tab phía trên, cùng hàng với Top/Users/Videos/etc.
- Ưu tiên XPath Inspector: `//android.widget.FrameLayout[@content-desc="LIVE"]/android.widget.TextView[@text="LIVE"]`.
- Có fallback dành cho giao diện cũ nhưng chỉ trên thanh danh mục Search, không dùng XPath LIVE toàn màn hình.
- Không bấm tab nữa nếu @selected=true.
- Sau click phải xác minh @selected=true trên tab LIVE **và** GridView kết quả trước khi ghi `Đã vào kết quả LIVE`.
- Nếu không thấy tab hoặc không xác nhận được việc chọn, dừng lượt tìm và lưu XML/PNG trong `diagnostics/live-search-navigation-*`; không quét nhầm Top.
- Giữ nguyên ka6/hwj, k_g/hvp, Home Recovery tối đa 5 Back và logic quét 4+2+2/Phase2/3.

## Log kỳ vọng

```text
LIVE_SEARCH_BUTTON_CLICKED: id=ka6
LIVE_SEARCH_INPUT_READY: id=hwj
LIVE_SEARCH_TAB_FOUND: method=FrameLayout/LIVE/TextView; selected=False
LIVE_SEARCH_TAB_TAP_REQUESTED: method=StrictXPath
LIVE_SEARCH_TAB_SELECTED: selected=true; grid=True; category=LIVE.
Đã vào kết quả LIVE cho “live”.
```

## Test

Cần build trên Windows bằng BUILD_RUN.bat và thử qua Appium cùng thiết bị. Đã kiểm tra offline cấu trúc XPath với XML LIVE Search mẫu; chưa xác minh thực tế trên Cloud Phone.
