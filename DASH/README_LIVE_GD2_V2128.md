# V2.1.28 – PHASE3 TIMING & FAST PROFILE

- Giữ nguyên tìm kiếm 4+2+2, OCR và Phase 2.
- Ghi PHASE3 START, PROFILE_CLICK_START, PROFILE_READ, LIVE_RETURN_CHECK và các TIMING (ms).
- Giảm vòng đọc hồ sơ từ 5 xuống 3; chờ 400 ms chỉ giữa các lần thử; không chờ cố định 700 ms trước lần thử đầu.
- Giới hạn đọc Viewer 3 lần, mỗi lần chờ 450 ms giữa lượt.
- Đọc lại sau Back tối đa 2 lần, kiểm tra LIVE trước khi kết luận.
- Không bỏ xác minh và không click liên tục; giữ thông tin chẩn đoán PNG/XML, trạng thái chống trùng trong tiến trình.
- Chưa đo hiệu năng trên thiết bị. Gửi log mới để xác định Appium source hay click/back đang chậm.
