# V1.6.2.6 — Gallery photo permission after TikTok app-data reset

Based on V1.6.2.5, no modifications to account vault, ADB switching, onboarding, Google Login, display name or Bio logic.

## Root cause
After `pm clear com.ss.android.ugc.trill`, TikTok starts without previously approved media-library permissions. Android/TikTok may show a request to access photos when Upload photo is opened. The older workflow went directly to `l37` thumbnail matching; a permissions overlay leaves `l37` absent, incorrectly reported as an image-matcher issue.

## Changes
- `AndroidMultiDeviceV1/Core/ProfileUpdates.cs`: a bounded `EnsureGalleryPhotoAccessAsync` state check is now called **after** Camera -> Upload photo, **before** image matching.
- When an explicit photos/videos access permission is detected, tap **Allow all photos** / **Allow** using Android PermissionController resource IDs, check that Gallery appears, and continue to the existing content-matching routine.
- Do not tap a generic Allow on contact/notification prompts. Do not approve unrelated permissions.
- If already granted, proceed directly; no extra prompts are generated.
- Retry Gallery loading in a bounded way; if permission granting returns to the Upload photo menu, reopen that menu once.
- If the popup has different buttons (e.g., partial access only) or `l37` stays missing, stop the photo step and log a technical issue. No blind image selection or duplicate Save & post.
- This change does not call `pm clear` or modify the saved history.

## Upgrade
Close the manager. Back up the whole project and `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
For patch: overwrite `AndroidMultiDeviceV1\Core\ProfileUpdates.cs` in the V1.6.2.5 source directory. Rebuild using `BUILD_RUN.bat`.

## Expected log fragments
`Ảnh: đã yêu cầu mở Gallery; kiểm tra quyền truy cập ảnh trước khi tìm thumbnail.`
`Ảnh: phát hiện hộp thoại yêu cầu quyền truy cập ảnh/video sau khi reset TikTok.`
`Ảnh: đã chọn Cho phép truy cập thư viện ảnh; chờ Gallery hiển thị thumbnail.`
`Ảnh: Gallery đã tải sau khi cấp quyền; bắt đầu so khớp thumbnail.`
`Ảnh: Gallery lượt 1/4; tìm thấy ... phần tử; so khớp ...`

## Testing limitations
The file's syntax structure and archive were checked in this environment, but Windows/.NET 8 and actual XCloudPhone UI testing were NOT available. The exact permission UI may vary on Android 16: if `Allow all photos` does not appear, report the current dialog's screenshot/Inspector XML and logs for a specific selector update. The patch does not blindly grant permissions.
