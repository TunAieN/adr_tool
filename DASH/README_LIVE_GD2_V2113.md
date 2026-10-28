# LIVE Search V2.1.13 — cuộn ngắn, đọc card mới, xếp hạng tham khảo

**Cơ sở:** bản FULL V2.1.12. Không sửa đăng nhập Google, kho tài khoản, trạng thái Profile, hay quy trình thay ảnh/bio/tên.

## Lỗi đã sửa
- V2.1.12 dừng tìm nguồn ở lần quét đầu nếu mọi Viewer là `OcrCandidate` / `OcrRepeatedCandidate` dù ảnh đã đọc đúng. Bản mới **không dừng sớm** chỉ vì OCR chưa phải dữ liệu xác minh độc lập.
- Mỗi lần kéo `dragGesture` ngắn đúng tỷ lệ đã cấu hình (mặc định 5%), chờ UI và ba lần đọc cấu trúc ổn định, rồi quét **chỉ những card mới hiện ra**.
- Card tiếp tục xuất hiện sau khi kéo mang cùng ID dựa trên vị trí tương đối; không đếm lại chỉ vì thay tọa độ hoặc vì tiêu đề OCR thay đổi. Trường hợp kéo không dịch chuyển 2 lượt sẽ dừng từ khóa.
- OCR icon người/Like và dấu thập phân giữ nguyên V2.1.12. Tối ưu chỉ OCR những tile mới, không OCR lại video cũ đang thay hình liên tục.
- Gom nhóm tối đa 8 card mới, đánh giá Viewer theo khoảng cấu hình và **chỉ gợi ý** ứng viên có số OCR lớn nhất. Tuyệt đối không click mở phòng, không bình luận, không đổi tài khoản.
- Nếu hết giới hạn thời gian hoặc số lần kéo trước khi đủ 8 card, ghi rõ kết quả từ nhóm chưa đủ 8. Kết quả OCR vẫn chỉ là ước lượng.

## Cài đặt
1. Sao lưu dự án V2.1.12 và tắt tool trước khi cập nhật.
2. Giải nén PATCH V2.1.13 ở gốc dự án; ghi đè đúng 3 tệp:
   - `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs`
   - `AndroidMultiDeviceV1/Core/LiveVisualOcr.cs`
   - `AndroidMultiDeviceV1/LiveSearchDialog.cs`
3. Chạy `BUILD_RUN.bat` trên Windows (.NET 8 SDK), bật Appium trên `http://127.0.0.1:4723` và ADB đang ONLINE.
4. Trong **Cấu hình LIVE → Nguồn LIVE**, ưu tiên: độ dài kéo 5%, chờ sau kéo 4500 ms, mỗi card 650 ms, nhóm 8 card, tăng timeout/từ khóa lên khoảng 180–240 giây và lượt kéo tối đa lên khoảng 25–40 nếu muốn đủ tám card với bước kéo rất ngắn. Cấu hình đã lưu của anh vẫn được giữ nguyên, không bị tự ghi đè.
5. Chọn một máy và bấm **Tìm LIVE**; xem log số `card MỚI` và số lượt kéo.

## Nhật ký dự kiến (minh họa)
```
ghép đôi: màn hình 1, 4 khung, 4 card mới, 0 card đã đọc
OCR card 1/4 ...
ghép đôi: nhóm đang có 4/8 card mới đã đọc.
ghép đôi: đã đọc xong màn hình; kéo NGẮN 5% một lần...
ghép đôi: màn hình 2, 4 khung, 0 card mới, 4 card đã đọc
...
ghép đôi: màn hình N, 4 khung, 2 card mới, 2 card đã đọc
...
ghép đôi: đánh giá nhóm 8 card mới; X ứng viên đạt ngưỡng...
ghép đôi: GỢI Ý phòng Viewer cao nhất trong nhóm... chưa mở phòng.
```

## Ranh giới và giới hạn
- `OcrRepeatedCandidate` chỉ là sự đồng thuận giữa 2 ảnh. Số 1.4K+, 3.1K+... là số hiển thị làm tròn; không được dùng như số chính xác.
- Việc ghép ID sau kéo dựa trên vị trí card khi kéo ngắn. Nếu TikTok thay đổi toàn bộ bố cục/ảo hóa danh sách mà không có chuyển dịch tọa độ, tool sẽ dừng thay vì bịa ID. Khi ấy cần log + XML + screenshot mới.
- Không biên dịch được WinForms/.NET trên môi trường Linux hiện tại; cần chạy BUILD_RUN.bat và thử một thiết bị trên Windows.
