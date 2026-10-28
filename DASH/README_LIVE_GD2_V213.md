# V2.1.3 — LIVE Grid Card Roots

## Lỗi trong V2.1.2
Hai nhãn LIVE ở `(144,68)` và `(153,68)` nằm ngoài GridView `(0,102,352,481)` nên là nhãn tab; nhãn trên ảnh thumbnail không nhất thiết xuất hiện trong XML Appium. Cũ yêu cầu phải thấy nhãn LIVE trên card mới có thể xác định card.

## Cập nhật
- Duyệt các phần tử thực trong GridView, tìm các khung có chiều rộng phù hợp một ô card; không dựa vào badge text LIVE.
- Nhận diện các card dù phần tử LIVE trên hình không có text trong Page Source.
- Hỗ trợ đọc chuỗi số dạng `483`, `1.7K+`, `2,4K+`, `12.6K+` nếu số thực sự có trong XML.
- Phân biệt rõ: có card nhưng không có viewer trong XML, với không có card.
- Ghi chi tiết rect của các phần tử trực tiếp trong GridView nếu thất bại. Giữ cơ chế chờ mỗi lần cuộn, không gửi bình luận.
- **Vẫn chỉ đánh dấu `Verified` khi có metadata viewer rõ ràng**, không tự đánh đồng con số bất kỳ là Viewer.

## Cài đặt
Đóng tool, sao lưu V2.1.2, chép `AndroidMultiDeviceV1/Core/LiveSearchEngine.cs` từ PATCH ghi đè file tương ứng, chạy `BUILD_RUN.bat`.

## Kiểm thử
Thử Phone 01 với từ khóa `liên quân`. Nếu log có `đã nhận diện ... ô card, nhưng chưa đọc được số Viewer qua XML`, hãy gửi file `live-search-last-ui.xml` sau khi xem lại dữ liệu riêng tư để làm bộ đọc số Viewer. Nếu chưa nhận diện card, gửi dòng log `phần tử trực tiếp=...` hoặc XML đó.

Không có môi trường .NET Windows và XCloudPhone để build/kiểm thử trực tiếp trong lần tạo bản vá này.
