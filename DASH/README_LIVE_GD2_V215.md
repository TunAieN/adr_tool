# LIVE Search V2.1.5 — Chẩn đoán card và kéo ngắn

## Đã thay đổi
- Chuyển `swipeGesture` sang `dragGesture` tốc độ xác định 260 px/s, độ dài mặc định 8% chiều cao màn hình (mẫu 352x624 khoảng 50 px). Tránh cuộn quán tính dài.
- Cho phép thiết lập từ 5–20% tại Cấu hình LIVE; chờ sau kéo mặc định 4500ms.
- Nếu nhìn thấy ô card nhưng **không đọc được cả tiêu đề lẫn Viewer** từ XML, **dừng tại viewport hiện tại**: không vuốt, không tính card không xác minh là đạt/không đạt điều kiện, không đổi từ khóa.
- Xuất bộ chẩn đoán XML + screenshot + tóm tắt vào `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\`.

## Quan trọng
Ảnh màn hình hiển thị số Viewer không đồng nghĩa số Viewer có mặt trong cây XML của Appium. Không thể sửa triệt để bộ đọc chỉ từ log. Cần xem `live-search-last-ui.xml` **và** `live-search-last-screen.png` cùng thời điểm để quyết định đọc từ XML hay cần cơ chế đọc ảnh đáng tin cậy.

## Cài đặt
Bản vá gồm 4 file: `Core/LiveSearchEngine.cs`, `Core/AdbAndAppium.cs`, `Core/LiveManagerSettings.cs`, `LiveSettingsDialog.cs`. Ghi đè vào mã nguồn V2.1.4, build với `BUILD_RUN.bat`. Sao lưu cấu hình trước khi cập nhật. Nếu cấu hình cũ có độ dài vuốt >20% (ví dụ 24%), tool sẽ nạp thành 8% để tương thích và tránh cuộn quá dài; lưu lại cấu hình sau khi mở cửa sổ.

Không mở LIVE, không gửi bình luận.

Chưa có kiểm thử trực tiếp bằng Appium trên XCloudPhone hoặc build Windows trong môi trường này.
