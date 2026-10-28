# LIVE SEARCH ENGINE — V2.1.0 (Giai đoạn 2)

Dựa trên Android Multi-Device LIVE V2.0.2. **Bản này chỉ tìm/đọc kết quả LIVE Search, không mở phòng, không gửi bình luận, không chuyển tài khoản.**

## Giao diện

Chọn đúng Phone trong bảng **Thiết bị** → menu trái **TÌM LIVE** → **Tìm LIVE**. Một cửa sổ riêng hiển thị các card, bằng chứng Viewer và log theo thời gian thực. **Dừng** để hủy. Không thể chạy cùng lúc với worker đăng nhập đang hoạt động trên cùng thiết bị.

## XPath theo Appium Inspector

- Search (Home): `//android.widget.ImageView[@resource-id="com.ss.android.ugc.trill:id/k_g"]`
- Ô từ khóa: `//android.widget.EditText[@resource-id="com.ss.android.ugc.trill:id/hvp"]`
- Gửi tìm kiếm: `//android.widget.Button[@resource-id="com.ss.android.ugc.trill:id/tv_search_textview"]`
- Tab LIVE: `//android.widget.TextView[@text="LIVE"]`
- Card: `//android.widget.GridView/android.widget.FrameLayout[2]/android.view.ViewGroup/android.view.ViewGroup`
- Icon ở badge card: `.../android.widget.ImageView` (chưa đủ metadata để luôn xác nhận đó là hình người)

Có xử lý hệ thống thông báo **Allow TikTok to send you notifications? → Don't allow** khi Appium xác nhận đúng hộp thoại này.

## Logic Giai đoạn 2

1. Dùng cấu hình tại **CẤU HÌNH LIVE** từ V2.0.2 (từ khóa, chế độ Random/Sequential, timeout, số card mỗi lượt, ngưỡng Viewer, số lần cuộn).
2. Từ Search, nhập từ khóa rồi click tìm kiếm. Bắt buộc click tab LIVE ngay cả khi tab Top cũng có GridView.
3. Chỉ đọc các card thuộc GridView có nhãn LIVE, giới hạn số card/lượt.
4. Tìm số cạnh icon ở badge đầu card. Khi metadata icon không chứng minh đó là icon người, hiển thị **Cần xác minh icon người**, không tự kết luận số Viewer hợp lệ.
5. Chỉ đánh dấu "Đạt ngưỡng Viewer" nếu icon được xác nhận bằng metadata và số thuộc ngưỡng đã cấu hình.
6. Cuộn danh sách kết quả, phát hiện ảnh/chữ card lặp và timeout, sau đó sang từ khóa khác.
7. Đóng Appium session khi Stop/hết đợt.

**Lưu ý**: UIAutomator/Appium đôi khi trả ImageView không có resource-id/content-desc phân biệt icon người và các loại icon khác. XPaths đang được dùng đúng như cung cấp, nhưng chưa có dump XML card thực tế để xác minh icon. Bộ lọc chủ động thận trọng, không chọn nhầm khi bằng chứng thiếu. Nhờ lấy XML Appium Inspector của một card LIVE nếu viewer count chỉ ra "Cần xác minh icon người".

## Hướng dẫn sử dụng

1. Sao lưu thư mục dự án và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén **FULL** vào thư mục mới, hoặc áp dụng **PATCH** lên đúng V2.0.2.
3. Chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK. Giữ Appium Server và ADB hoạt động.
4. Kết nối Phone 01 và Refresh. Đảm bảo worker cũ đã Stop.
5. Mở **CẤU HÌNH LIVE** nhập 1–3 từ khóa, lưu.
6. Chọn Phone 01 → **TÌM LIVE** → **Tìm LIVE**.
7. Xem bảng kết quả; nếu không đọc được Viewer, lấy XML kết quả LIVE để hiệu chỉnh selector trước Giai đoạn 3.

## Những gì chưa làm

- Chưa vào phòng LIVE, chưa lấy username chủ phòng sau khi mở phòng.
- Chưa swipe giữa các phòng đang phát (khác với thao tác cuộn danh sách Search).
- Chưa có lịch sử phòng và giới hạn thiết bị cùng phòng.
- Chưa gửi bình luận hoặc bất kỳ tương tác công khai nào.
- Chưa biên dịch/chạy thử trên máy Windows hoặc XCloudPhone thực tế; kiểm tra build bằng `BUILD_RUN.bat`.
