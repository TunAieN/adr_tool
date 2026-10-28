# V2170 Dynamic Runtime Concurrency — sửa tăng/giảm giới hạn khi ứng dụng đang chạy

Ngày: 26/10/2026. Nền: V2170 Adaptive LIVE Viewer V3 (giữ nguyên OCR và logic chọn LIVE).

## Những thay đổi

- `Core/DeviceExecution.cs`: thay SemaphoreSlim cố định bằng bộ giới hạn động, hàng đợi FIFO, hủy khi chờ, trả lease đúng một lần, giảm giới hạn theo chế độ graceful drain.
- `Core/DeviceOrchestrator.cs`: cập nhật giới hạn runtime, ghi log `CONCURRENCY_LIMIT_UPDATED`, `CONCURRENCY_SLOT_WAIT`, `CONCURRENCY_SLOT_GRANTED`, `CONCURRENCY_SLOT_WAIT_CANCELLED`; áp dụng chung cho START LIVE và START/TEST tài khoản; phân biệt worker đã đăng ký và worker có slot.
- `MainForm.cs`: nút **Lưu** áp dụng giá trị ngay cho orchestrator đang mở, cập nhật tổng hợp `Đang chạy/giới hạn` và `Đợi slot`, ghi log cấu hình; thay đổi Retry/giới hạn thiết bị mỗi LIVE có hiệu lực trong những chu kỳ tiếp theo.
- `Core/LiveAutonomousWorkflow.cs`, `Core/LiveRoomContinuousCoordinator.cs`, `Core/LiveVerifiedSessionHandoff.cs`, `LiveSearchDialog.cs`: giới hạn thiết bị/phòng được đọc từ cấu hình hiện tại **mỗi khi xin slot phòng mới** thay vì giữ bản sao cũ; giới hạn phục hồi session cho workflow cũng được cập nhật lúc kiểm tra retry mới.

## Cách cài

1. **Sao lưu** thư mục bản V3 đang chạy cùng các file cấu hình/kho tài khoản ở vị trí riêng.
2. Giải nén bản **FULL SOURCE** vào **một thư mục mới**, không đè lên folder hiện đang chạy.
3. Mở CMD trong thư mục mới, chạy `BUILD_RUN.bat`. Yêu cầu .NET 8 SDK trên Windows.
4. Nếu dùng PATCH, chép đúng 7 file trong thư mục `AndroidMultiDeviceV1` của patch vào đúng đường dẫn tương ứng trong source V3, sau đó `dotnet build` và khởi động bản mới.
5. Mở kho, kết nối ADB, chạy 4 thiết bị, Start thiết bị thứ năm; xác nhận có log `CONCURRENCY_SLOT_WAIT`, sau đó đổi từ 4 → 5, bấm Lưu. Kiểm tra `CONCURRENCY_LIMIT_UPDATED` và `CONCURRENCY_SLOT_GRANTED` trên thiết bị chờ mà không bấm Start lại.
6. Khi đã có 5 máy chiếm slot và giới hạn mới là 5, **máy thứ sáu vẫn phải chờ** — đúng ý nghĩa giới hạn tối đa.

## Kiểm thử tự động trên Windows

```cmd
dotnet run --project QA_Concurrency_Runtime\QA_Concurrency_Runtime.csproj -c Release
```

Bài test bao gồm 4→5 (đánh thức FIFO), giảm 5→3 không dừng phiên, hủy worker khi chờ, đảm bảo lease không được trả hai lần và cập nhật nhiều lần khi có hàng đợi.

## Điều kiện bảo vệ

- Giảm giới hạn **không** ép dừng/đăng xuất máy đang chạy. Số slot hiện hữu có thể tạm > giới hạn mới và sẽ giảm dần khi worker kết thúc.
- Không cấp mới khi đã đầy; không cho worker chờ đã hủy quay lại.
- Giới hạn phòng LIVE mới chỉ áp dụng **lần xin slot tiếp theo**. Các lease phòng đang hoạt động giữ nguyên.
- Các module OCR V3, tài khoản, login, comment và selector **không bị chỉnh sửa** bởi bản vá này.

## Giới hạn kiểm thử

Tại môi trường tạo bản này chưa có Windows/.NET SDK và Appium/XCloudPhone; chưa biên dịch hay chạy trên điện thoại thật. Cần chạy smoke test bằng .NET 8, sau đó kiểm thử thực tế trên Windows trước khi dùng dữ liệu quan trọng.
