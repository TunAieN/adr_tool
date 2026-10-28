# V2170 - TikTok LIVE Search badge OCR correction (26/10/2026)

## Fix phạm vi hẹp

Chỉ sửa `AndroidMultiDeviceV1/Core/LiveVisualOcr.cs`. Phần gửi bình luận, đăng nhập, kho tài khoản, điều phối đa máy, selector và luồng mở phòng hiện có giữ nguyên.

1. `ViewerNumberRoi`: thay quét đúng 1 hàng `tile.Y + tile.Height * 0.032` bằng quét nhiều hàng trong 24px đầu card, dò dải đỏ LIVE sát mép trái. Ưu tiên hàng sớm nhất có độ dài gần cực đại để tránh lệch 1px do viền anti-alias.
2. `BadgeIconRegion`: lấy tọa độ Y theo dải đỏ đã nhận diện, chiều cao icon tính theo *chiều rộng* card, không theo chiều cao card bị cắt ở mép dưới.
3. `DetectBadgeIcon`: giữ phân biệt Person/Like bằng hai mẫu hình, thay minConfidence 0.86 -> 0.72 và minMargin 0.16 -> 0.12. Tránh đọc Viewer nếu icon thực sự là Like/Unknown.
4. ROI OCR số: bắt đầu sau icon, dùng chiều rộng card để ước lượng cao và rộng, giữ được số khi card hàng dưới chỉ hiển thị một phần.

## Đối chiếu ZIP chẩn đoán Phone 02 (live-scroll-20261026-170916-1.zip)

Trong phép thử offline mô phỏng chính xác phép quét đỏ/mặt nạ icon trên 8 vị trí được lấy mẫu (4+2+2):
- Badge đỏ phát hiện: trước 6/8, sau 8/8.
- Icon Person phát hiện: trước 0/8, sau 8/8.
- Tesseract Linux đọc được ít nhất một đầu ra là chuỗi số hợp lệ ở 7/8 vùng ROI mới. Chưa có kiểm thử Windows/.NET/Appium và không khẳng định 7 Viewer được xác minh cuối cùng.

Kết quả thô nằm trong `QA_offline_badge_results.json`; đây là số liệu dựa vào ảnh và logic tương đương bằng Python, **không phải kết quả chạy bộ C# đã biên dịch**.

## Rủi ro cần đánh giá trong lần chạy thật

- Logic `HasDecimalDotPixels`/`ReconcileWithDecimalPixels` hiện có vẫn có thể xử lý nhầm `27K+` thành `2.7K+`, hoặc `4.4K+` thành `44K+`. Chưa sửa phần này vì chưa có bằng chứng đủ chắc chắn để thay thuật toán mà không tạo sai số lớn. Bản sửa chỉ nhằm cho **OCR số thực sự được chạy**, không cam kết thứ hạng Viewer tuyệt đối chính xác.
- Chỉ có 1 bộ ảnh Phone 02 ở 352×624; cần test với Phone 03/04 và với các icon Like thật trước khi sử dụng lâu dài.
- Khi không có Viewer hợp lệ, chương trình vẫn luân phiên từ khóa. Phase 2 vẫn yêu cầu kiểm tra lại đúng card; không nhấn đoán tọa độ.
- Các tệp `bin/`, `obj/`, `dist/` và binary biên dịch cũ đã được bỏ khỏi ZIP để tránh chạy DLL lỗi thời. Source chưa được build trong môi trường tạo gói.

## Cách kiểm thử trên Windows

1. Sao lưu toàn bộ bản V2170 đang chạy. Giải nén ZIP này vào thư mục mới, không ghi đè các kho tài khoản trong AppData.
2. Cài sẵn .NET 8 SDK và Tesseract OCR như bản trước. Chạy `BUILD_RUN.bat` từ thư mục gốc; nếu báo BUILD FAILED, gửi nguyên log biên dịch.
3. Chỉ chạy **một điện thoại** với cùng từ khóa `live` và 8 card (4+2+2) để đo khả năng OCR.
4. Xem các log `OCR card`, `đạt ngưỡng`, `PHASE2 TARGET_FOUND`, `PHASE2 TARGET_VERIFIED`, `PHASE2 TARGET_OPENED` và ảnh trong `diagnostics`. Trường hợp đọc nhầm như 27K/2.7K vẫn phải sửa OCR số trước khi sử dụng chọn phòng hàng loạt.
5. Sau khi kiểm tra một máy, mới so sánh với các thiết bị còn lại. Nếu không xác minh được số hoặc phòng, không để tool nhấn mù.

## Kiểm tra build

Môi trường tạo gói không cài .NET SDK Windows, vì vậy chưa xác nhận `dotnet build` hay chức năng trên XCloudPhone. Không sửa module gửi bình luận hiện có.
