# V1.6.1.2 — Photo Gallery Matcher

## Sửa lỗi trong log 04:14:59

Trước đây `ProfileUpdates.cs` chủ động dừng ở câu "Đã chuẩn bị và chuyển ảnh lên điện thoại, nhưng chưa thể xác minh thumbnail...". Đây là chức năng CHƯA ĐƯỢC TRIỂN KHAI, không phải lỗi tài khoản.

Bản này bổ sung **thử nhận diện ảnh bằng đối chiếu trực quan** giữa tệp JPEG đã chuyển bằng ADB và ảnh chụp TikTok Gallery qua Appium. Lấy vị trí các ảnh bằng resource-id `com.ss.android.ugc.trill:id/l37`, **không dựa vào [1]/[2]/[3]**. Chỉ click khi ảnh đạt ngưỡng tin cậy nghiêm ngặt, và không có ảnh thứ hai gần giống. Có tối đa 4 màn hình cuộn để tìm ảnh mới chuyển. Nếu không tìm được, ảnh sẽ ở trạng thái `GALLERY_REVIEW`; đã hoàn thành Name/Bio vẫn được giữ; hồ sơ chưa được đánh dấu hoàn tất và tài khoản nhận `RETRY_PENDING` kỹ thuật, không phải `NEEDS_REVIEW` bảo mật.

### Thận trọng đối với Save & post

TikTok có thể tạo **bài đăng công khai** khi chọn `Save & post`. Vì vậy có thêm ô **Cho phép tự động Save & post** trong `Cấu hình hồ sơ → Ảnh đại diện`, mặc định **TẮT**. Bật nếu chấp thuận. Đặt nhật ký `SAVE_PENDING` trước khi gửi Save; một phiên Save không rõ kết quả sẽ không tự gửi lần hai. `PhotoVerified` chỉ được đặt nếu ảnh đại diện hiện tại khớp nguồn với ngưỡng cao. Nếu không thể xác minh, giữ `SAVE_PENDING` và báo cần đối chiếu, không tuyên bố thành công.

### Các file thay đổi

- `AndroidMultiDeviceV1/Core/AdbAndAppium.cs`: screenshot, geometry, click matched ID, gallery swipe.
- `AndroidMultiDeviceV1/Core/GalleryPhotoMatcher.cs`: so khớp ảnh trên màn hình và kiểm tra avatar.
- `AndroidMultiDeviceV1/Core/ProfileUpdates.cs`: quy trình camera → Upload photo → tìm ảnh → Next → Confirm → Save & post, checkpoint từng tài khoản, tránh lỗi bảo mật giả.
- `AndroidMultiDeviceV1/ProfileUpdateDialog.cs`: checkbox opt-in Save & post.

### Cài đặt

1. Đóng Manager, sao lưu dự án và `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén PATCH đè lên bản **V1.6.1.1**, giữ nguyên cấu trúc thư mục.
3. Chạy `BUILD_RUN.bat` trên Windows với .NET 8.
4. Thử trên một tài khoản được quản lý hợp lệ, **tắt** Save & post trong lần thử đầu để đối chiếu Gallery.
5. Quan sát `Ảnh: Gallery lượt ...; điểm ảnh tốt nhất=...` trong log. Chỉ khi thử nghiệm nhận diện chính xác mới bật Save & post.

### Giới hạn và kiểm chứng

Không có ảnh chụp màn hình của **màn hình TikTok Gallery** và không có thiết bị XCloudPhone để thử trực tiếp trong môi trường tạo bản vá. Do đó ngưỡng khớp có thể cần điều chỉnh cho cách TikTok vẽ thumbnail và nút `l37`. **Bản này chưa được build trên Windows và chưa xác minh được luồng công khai ngoài thực tế.** Ngưỡng so khớp được thiết kế thận trọng: ưu tiên không chọn nhầm ảnh hơn việc tự động hoàn thành bằng mọi giá. Ảnh đã lưu lên TikTok có thể mất thời gian xuất hiện.
