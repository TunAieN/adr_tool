# V2.1.52 – LIVE Search: vuốt ngang thanh tab q1m

## Nguyên nhân

Sau khi tìm từ khóa, tab LIVE đôi khi nằm bên phải ngoài vùng nhìn thấy. V2.1.51 chỉ nhận tab đang hiện, nên báo LIVE_SEARCH_TAB_NOT_FOUND dù vẫn ở kết quả Top.

## Thay đổi

- Tìm thanh category: `//android.widget.HorizontalScrollView[@resource-id="com.ss.android.ugc.trill:id/q1m"]/android.widget.LinearLayout` và lấy bounds của HorizontalScrollView cha.
- Khi không thấy LIVE trong viewport, vuốt từ phải sang trái **chỉ trong chiều cao của thanh tab**, tối đa 3 lần, có đọc XML và log sau mỗi lần.
- Tránh click vào LIVE của một video trong kết quả Top; tab LIVE phải là phần tử danh mục ở header và nằm trọn trong viewport.
- Sau khi LIVE hiện, nhấn XPath tab cũ `//android.widget.FrameLayout[@content-desc="LIVE"]/android.widget.TextView[@text="LIVE"]`, xác minh `selected=true` + GridView rồi mới quét 8 card.
- Không thay đổi quét card, OCR, PHASE2/3 hoặc kho tài khoản.

## Log kỳ vọng

```
LIVE_SEARCH_TAB_BAR_SWIPE_REQUESTED: 1/3; resource=q1m; direction=LEFT
LIVE_SEARCH_TAB_BAR_SWIPE_CHECK: 1/3; changed=True; tabVisible=True
LIVE_SEARCH_TAB_FOUND: method=FrameLayout/LIVE/TextView; selected=False
LIVE_SEARCH_TAB_TAP_REQUESTED: method=StrictXPath
LIVE_SEARCH_TAB_SELECTED: selected=true; grid=True; category=LIVE.
```

Nếu sau 3 lần vẫn chưa thấy, lưu ảnh/XML trong diagnostics/live-search-navigation-*, dừng mà không click video.

## Kiểm thử

Build trên Windows với `BUILD_RUN.bat`; Cloud Phone cần chạy thực tế để xác nhận.
