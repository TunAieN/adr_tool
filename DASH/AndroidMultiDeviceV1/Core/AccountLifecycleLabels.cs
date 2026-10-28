namespace AndroidMultiDeviceV1.Core;

public static class AccountLifecycleLabels
{
    public static string Translate(string state,bool everUsed) => state switch
    {
        "AVAILABLE" => everUsed ? "Đã sử dụng" : "Chưa sử dụng",
        "RESERVED" => "Đang đăng nhập",
        "ACTIVE" => "Đang chạy",
        "COMPLETED" => "Đã sử dụng",
        "INTERRUPTED" => "Gián đoạn · đã dùng",
        "RETRY_PENDING" => "Chờ xử lý lỗi",
        "NEEDS_REVIEW" => "Cần kiểm tra",
        "ACCOUNT_CHECKPOINT" => "Cần xác minh",
        "RESTRICTED" => "Bị hạn chế",
        "INVALID" => "Không hợp lệ",
        "DISABLED" => "Đã vô hiệu hóa",
        "PURGED" => "Đã xóa khỏi kho",
        _ => string.IsNullOrWhiteSpace(state) ? "Chưa xác định" : "Khác: "+state
    };
}
