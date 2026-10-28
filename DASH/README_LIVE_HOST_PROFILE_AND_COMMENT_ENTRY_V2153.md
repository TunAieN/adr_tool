# V2.1.53 – cập nhật selectors hồ sơ LIVE và mở giao diện bình luận

## Phân tích lỗi

Trong phiên 22:54, PHASE3 đọc được Viewer nhưng không mở được hồ sơ vì chỉ tìm avatar `k13`. ID mới người vận hành cung cấp là `k1s`. Tên hiển thị cũng đổi từ `q16` sang `q2e`. Username giữ `user_name`.

## Thay đổi

- PHASE3 ưu tiên `k1s`, dự phòng `k13` và chỉ nhấn nút đang hiển thị trong XML LIVE mới chụp; nếu thiếu thông tin thì dừng.
- Đọc tên hiển thị `q2e` (dự phòng `q16`), đọc username `user_name` như cũ.
- Sau khi có dữ liệu, nhấn Android Back **một lần**, rồi xác nhận vẫn ở phòng LIVE bằng `tv_online_audience_num`.
- Bộ kiểm thử giao diện bình luận (`LiveCommentUiProbe` và thao tác gửi một lần do người dùng chủ động kích hoạt) ưu tiên nhận diện `h4s`, dự phòng `h42`, sau đó chữ `Type...` / `Type…`. Click vùng có `bounds` thực tế, không đoán tọa độ. Không thay đổi cơ chế gửi lặp tự động.
- Bảo toàn OCR/PHASE1/PHASE2/Search, thiết bị, kho tài khoản và cấu hình cũ.

## Các log cần quan sát

```
PHASE3 PROFILE_CLICK_START: tìm nút chủ LIVE k1s hoặc k13.
PHASE3 PROFILE_TAP_FAST: selector=k1s; ...
PHASE3 PROFILE_READ 1/3: username=FOUND, name=FOUND, nameSource=q2e
PHASE3 PROFILE_CLOSE_START: Android Back.
PHASE3 LIVE_RETURN_CHECK: live=True
PHASE3 LIVE_ACCEPTED
PHASE5 OPEN_RESOLVE: found=True; method=h4s; ...
```

Đây là log dự kiến, chưa phải log thực tế. Build và thử trên Windows/Cloud Phone để xác nhận.
