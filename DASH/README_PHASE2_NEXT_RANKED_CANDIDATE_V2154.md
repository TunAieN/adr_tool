# V2.1.54 — Thử card kế tiếp trong cùng 8 card sau khi LIVE không đạt điều kiện

## Bằng chứng từ log/ZIP người dùng
- Đợt kiểm thử 23:29–23:30: card màn hình 2, ô 3 bị OCR đọc `7m` (7.000.000) nhưng ảnh badge trong ZIP thể hiện biểu tượng người + **74**, và PHASE3 trong phòng đọc được **68** từ `tv_online_audience_num`.
- Chương trình trước đây kết thúc toàn bộ đợt khi PHASE3 trả về `LIVE_REJECTED: LIVE_CONDITION_FAILED` dù 8 card đã thu thập có những ứng viên khác.

## Thay đổi
- Chặn giá trị OCR không đủ tin cậy dạng **một chữ số + M** (như `7m`): không dùng để xếp hạng, kể cả khi hai ảnh lặp lại cùng lỗi OCR. Ảnh crop/giá trị OCR thô vẫn lưu ở diagnostics.
- Chỉ xét 8 card đã thu thập (4+2+2), xếp theo Viewer OCR từ cao xuống thấp, loại số không đọc được / Like / ngoài cấu hình.
- Mỗi ứng viên có thư mục chẩn đoán `phase2/attempt-NN` và `phase3/attempt-NN`, không ghi đè lần thử trước.
- Nếu mở phòng mà PHASE3 thấy Viewer không đạt ngưỡng hoặc danh tính không xác minh: thoát LIVE, xác nhận trở về lưới, thử card kế tiếp. Không cần quét lại thêm card.
- Khi chuyển ứng viên, vuốt có giới hạn theo vị trí ước lượng của màn hình hiện tại và vị trí ghi trong bản chụp cũ. Trước khi nhấn luôn đối chiếu ảnh (ngưỡng nghiêm ngặt) hoặc tiêu đề + Viewer bằng hai lần đọc, không nhấn bằng tọa độ cũ hay Viewer đơn lẻ.
- Nếu không thể xác nhận đã về lưới, hết thời gian hoặc có lỗi kỹ thuật thì dừng. Khi có `LIVE_ACCEPTED` dừng ngay, không mở các phòng khác.
- Giao diện kết quả phân biệt "Gợi ý OCR" và "Đã xác minh phòng".

## Log kỳ vọng
```
PHASE2 NEXT_CANDIDATE: rank=1/4; ocrViewer=3287; viewport=1; slot=4
PHASE2 TARGET_VERIFIED: rank=1; evidence=PREVIEW_MATCH; ...
PHASE3 LIVE_CONDITION_FAILED: Viewer=68 ngoài [100, ∞].
PHASE3 LIVE_REJECTED: LIVE_CONDITION_FAILED; đã quay về lưới kết quả.
PHASE2 RETURN_GRID_CHECK: rank=1; grid=True
PHASE2 CANDIDATE_REJECTED: rank=1; ...
PHASE2 NEXT_CANDIDATE: rank=2/4; ...
```
*Thứ tự chỉ minh họa; do `7m` bị loại, ứng viên đứng đầu theo dữ liệu ZIP là 3287, rồi 1300, 803, 212. Đọc Viewer thực tế vẫn cần chạy trên Android.*

## Giới hạn
Chưa build bằng .NET 8 trên Windows hoặc thử trên Cloud Phone thật. Các điều kiện nhận dạng card LIVE bằng thumbnail động / OCR vẫn có thể không đủ bằng chứng, khi đó tool không nhấn phỏng đoán. Không thay đổi tính năng bình luận.
