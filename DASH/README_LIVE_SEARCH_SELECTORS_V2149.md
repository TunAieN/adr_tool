# V2.1.49 – LIVE Search: ka6/hwj và tương thích k_g/hvp

## Lỗi

Sau khi bấm Home, V2.1.48 chỉ nhận diện `k_g/hvp`. Máy mới dùng `ka6` cho nút Search và `hwj` cho ô nhập, nên báo `HOME_SEARCH_BUTTON_NOT_FOUND` dù đang ở Home.

## Đã sửa

- Nút mở Search: `//android.widget.ImageView[@resource-id="com.ss.android.ugc.trill:id/ka6"]` và dự phòng `k_g`.
- Ô nhập: `//android.widget.EditText[@resource-id="com.ss.android.ugc.trill:id/hwj"]` và dự phòng `hvp`.
- Nút submit: giữ `//android.widget.Button[@resource-id="com.ss.android.ugc.trill:id/tv_search_textview"]` (đã có sẵn).
- Áp dụng selector mới trong cả bước vào Search, khôi phục về Home, điều kiện chờ ô nhập và nhập từ khóa. Không thay đổi quét 8 card 4+2+2, OCR, hay các bước Profile/Google.
- Sau khi nhấn Home, chờ có giới hạn tối đa 8 giây để Search xuất hiện; không Back khỏi Home.
- Log ghi rõ ID nút và ô nhập đã nhận diện; giữ ảnh/XML chẩn đoán khi không tìm được.
- Chỉ click khi XML đã xác nhận có selector, không click tọa độ Search phỏng đoán.

## Log kỳ vọng

```text
LIVE_SEARCH_ENTRY_MISSING: chưa thấy ka6/k_g/hwj/hvp...
LIVE_SEARCH_HOME_TAP_REQUESTED...
LIVE_SEARCH_RECOVERY_READY: ... button=ka6 ...
LIVE_SEARCH_BUTTON_CLICKED: id=ka6; afterRecovery=true
LIVE_SEARCH_INPUT_READY: id=hwj
Đã vào kết quả LIVE cho “ghép đôi”.
```

## Kiểm thử

Trên Windows, giải nén ZIP sang thư mục mới, chạy `BUILD_RUN.bat`, giữ Phone 02 ADB trực tuyến, mở TikTok ở một trang phụ và thử **Tìm LIVE**. Nếu thất bại, gửi log `LIVE_SEARCH_*` và `diagnostics/live-search-navigation-*` (che dữ liệu nhạy cảm).

Chỉ kiểm tra offline selector và toàn vẹn ZIP tại đây; chưa build .NET trên Windows hoặc chạy trên Android thật.
