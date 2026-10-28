# LIVE Search V2.1.2 — tìm card động theo cấu trúc GridView

## Vì sao V2.1.1 không thấy card?
- Bản cũ loại các badge LIVE xuất hiện trước 20% chiều cao màn hình. Ở màn hình Search LIVE, badge của hàng card đầu có thể nằm ngay dưới thanh tab, nên hai nhãn LIVE vẫn có trong XML mà card trả về 0.
- XPath có chỉ số `[2]` là vị trí một card cụ thể, không phải XPath đại diện cho mọi card. Các thẻ tiếp theo có thể có `[1]`, `[3]`, `[4]` và nhiều mức bao khác nhau.

## Sửa trong bản V2.1.2
- Xác định GridView động và duyệt các nhãn LIVE nằm bên trong card; không còn dùng mốc 20% toàn màn hình.
- Từ badge tìm ngược lên GridView item thực tế (mỗi item có index khác nhau), hỗ trợ card lồng sâu.
- Khi badge nằm ngoài cây GridView do layout đặc biệt, chỉ nhận qua vùng tọa độ thật của GridView.
- Chống đếm trùng nhiều badge cùng card, giữ quy tắc đọc chậm một lượt rồi cuộn.
- Khi không nhận diện được, log in vùng GridView/badge và lưu XML chẩn đoán **chỉ tại máy cục bộ** trong `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\live-search-last-ui.xml` (xem lại thông tin riêng tư trước khi chia sẻ).

## Cài đặt
1. Sao lưu dự án và dữ liệu.
2. Chép file `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs` từ PATCH vào V2.1.1.
3. Chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK.
4. Thử Phone 01, từ khóa `ghép đôi`.

**Giai đoạn 2 chỉ đọc kết quả Search; không vào phòng hoặc đăng bình luận.**

**Chưa build .NET hoặc kiểm thử bằng XCloudPhone trực tiếp trong môi trường tạo bản vá.**
