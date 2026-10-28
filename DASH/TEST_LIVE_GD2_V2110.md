# Kiểm tra thủ công V2.1.10

1. dotnet build AndroidMultiDeviceV1\\AndroidMultiDeviceV1.csproj -c Release -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
2. Appium + ADB Online, trên Phone 01 tìm từ khóa `ghép đôi`.
3. So sánh từng card với screenshot ở cùng thời điểm. Xác minh 171 không bị rút thành 71; 1.4K+ không bị phóng đại thành 14K+.
4. Kiểm tra log `quy tắc=LeadingOneRecoveredCandidate` hoặc `DecimalDotRecoveredCandidate` khi xuất hiện.
5. Kiểm tra danh sách card trong cửa sổ tìm LIVE có dòng OcrCandidate và nút Stop hoạt động.
6. Thử card badge Like: phải bỏ qua số; Unknown: không dùng số làm Viewer.
7. Đảm bảo không mở LIVE, không tự gửi bình luận.
