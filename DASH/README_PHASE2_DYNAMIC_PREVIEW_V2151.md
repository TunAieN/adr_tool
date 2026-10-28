# V2.1.51 – PHASE2 dynamic LIVE thumbnail / recheck

## Gốc lỗi
V2.1.50 so sánh 12x12 RGB toàn thumbnail. Video LIVE đổi frame nên score 0.238, vượt ngưỡng nghiêm ngặt 0.105; 5 lần reverse vẫn không đủ bằng chứng.

## Sửa
- Giữ kiểm tra ảnh nghiêm ngặt đầu tiên (0.105 + runner-up margin 0.035). KHÔNG nới ngưỡng để nhấn nhầm.
- Sau khi cuộn ngược đủ số màn hình, bổ sung đối chiếu nội dung: OCR tiêu đề ổn định + Viewer gần số gốc (35%) + đúng cột, tiêu đề phải khác đáng kể card đối thủ.
- Yêu cầu hai lần đọc độc lập và kiểm tra lại vị trí từ XML mới trước khi nhấn. Không dùng Viewer đơn độc làm RoomID.
- Lưu metadata-first / metadata-confirm JSON và OCR crop vào `diagnostics/live-scroll-*/phase2`.
- Nếu không đọc được tiêu đề hoặc không đủ bằng chứng, vẫn dừng an toàn.
- Giữ nguyên các giai đoạn LIVE Search selector `ka6/hwj`, tab LIVE strict, 4+2+2, Phase3 và các quy tắc Profile.

## Log
`PHASE2 DYNAMIC_PREVIEW`, `PHASE2 METADATA_RECHECK`, `PHASE2 METADATA_CONFIRM`, `PHASE2 TARGET_METADATA_VERIFIED`, `PHASE2 TARGET_VERIFIED`.

## Giới hạn
Đây là phương án dự phòng khi LIVE thumbnail đổi frame nhưng caption còn ổn định. Nếu OCR tiêu đề không ổn định, bản này vẫn từ chối nhấn, không thể hứa 100%. Chưa build/chạy trên Windows CloudPhone.
