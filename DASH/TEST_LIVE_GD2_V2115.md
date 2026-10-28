# TEST LIVE Search V2.1.15 — ID card sau cuộn

1. Tìm kiếm 'ghép đôi', ghi danh sách 4 Viewer đầu và vị trí tile.
2. Kéo 30%. Nếu 2 thumbnail từ hàng dưới chuyển lên hàng trên, chúng phải giữ ID và không cộng vào nhóm 8.
3. Nếu màn hình có card mới thật, OCR mới cộng một lần.
4. So sánh log trước/sau OCR: 'CHƯA ĐỐI CHIẾU' không được coi là kết quả cuối.
5. Thử trường hợp hai phòng có Viewer bằng nhau: không tự coi hai phòng là một chỉ vì số giống nhau.
6. Mất screenshot/không phân biệt được preview: báo cảnh báo và không tiếp tục đếm bừa.
7. Stop, chạy lại tìm kiếm khác và xác nhận tracker mới không kế thừa ID cũ.
8. Giữ nguyên login, vault và các thao tác thiết bị của V2.1.14.
