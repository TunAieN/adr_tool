# LIVE Search V2.1.14 — kéo đủ để xuất hiện hàng card mới

Cơ sở: mã nguồn V2.1.13. Giữ nguyên OCR, đăng nhập, Multi-Device và kho tài khoản.

## Sửa lỗi theo log 01:14–01:15
- V2.1.13 kéo **5% của màn hình 624px = ~31px**; mỗi card hàng đầu cao **~339px**. Vì thế 4 lần kéo chỉ dịch khoảng một phần hàng và chưa xuất hiện thẻ LIVE mới.
- Mặc định mới **30% (~187px)**; giới hạn 15–40%, không vượt quá một hàng card trên màn hình mẫu. `dragGesture` tốc độ 180px/giây, không fling.
- Sau từng lần kéo: chờ cấu hình (mặc định 4.500ms), đợi ba lần đọc UI ổn định, đọc OCR chỉ với card mới, ghi nhớ các card cũ.
- Bộ theo dõi card điều chỉnh giới hạn dịch chuyển theo % kéo; ngưỡng cũ 170px đã không còn phù hợp với kéo 30%, dễ tính nhầm card cũ thành card mới.
- Cấu hình đã lưu 5–14% tự chuyển lên 30% lúc đọc; các cấu hình từ 15–40% được giữ nguyên. **Các cài đặt khác không bị thay đổi**.

## Hướng dẫn nâng cấp
1. Đóng Android Multi-Device Manager, sao lưu thư mục hiện tại và `live-manager-settings.json`.
2. Giải nén PATCH vào gốc dự án V2.1.13, ghi đè giữ nguyên cấu trúc. Hoặc dùng bản FULL giải nén vào thư mục khác.
3. Chạy `BUILD_RUN.bat` trên Windows .NET 8. Giữ Appium tại 127.0.0.1:4723 và ADB kết nối.
4. Trong **Cấu hình LIVE > Nguồn LIVE**, chỉnh `Độ dài kéo` 30%, chờ sau kéo 4500ms, chờ mỗi card 650ms. Nên thử 1 máy trước.
5. Chạy **Tìm LIVE** với từ khóa `live`. Theo dõi log chuyển từ `4 card mới` → `0 card mới` → `2 card mới` sau khoảng 2 lượt kéo.
6. Nếu vẫn chỉ có 0 card mới, thử 35% và gửi log cùng 2 ảnh trước/sau kéo. Không đặt >40% để tránh bỏ qua nguyên hàng.

**Giới hạn:** chỉ tìm, đọc OCR, so sánh và hiển thị đề xuất. Không mở phòng, không gửi bình luận. OCR Viewer vẫn là số ước lượng từ nhãn của TikTok. Chưa được kiểm thử với XCloudPhone hoặc build WinForms trên Windows trong môi trường này.
