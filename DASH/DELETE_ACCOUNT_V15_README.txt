ANDROID MULTI-DEVICE MANAGER V1.5 - XOA TAI KHOAN KHO
======================================================
Co so: ANDROID_MULTI_DEVICE_V1_USERNAME_EXCEL_FULL (V1.4).
Giữ nguyên: Multi-Device, Google Login, kho ma hoa, import TXT/Excel,
username|email|password, gan/go khoi may, State Manager va Auto Recovery.

CHUC NANG
- Trong KHO TAI KHOAN, chon 1 hoac nhieu dong, bam "Xóa khỏi kho".
- Hien danh sach username, email, may duoc gan (toi da 8 dong) va yeu cau xac nhan.
- Mac dinh nut No (khong xoa). Neu chon Yes, xoa toan bo ban ghi duoc chon
  khoi account-vault.json (username, email, password, phan cong, trang thai trong vault).
- KHONG xoa tai khoan TikTok/Google tren Internet; KHONG tu dang xuat tren dien thoai.
- KHONG anh huong den cac tai khoan con lai va khong dung worker tren may khac.
- Khong cho xoa khi bat ky tai khoan da chon dang RESERVED/ACTIVE.
- Khong cho xoa neu dien thoai duoc gan co worker dang chay,
  dang STOPPING, hoac dang co phien Appium chan doan trong manager.
- Xoa theo lo: neu mot dong bi chan thi KHONG xoa dong nao trong lo do.
- Kiem tra lai trang thai DUOI KHOA dong bo cung voi Start ngay luc ghi kho.
- Ghi du lieu ma hoa xuong file bang cach atomic truoc khi thay danh sach RAM.
- Don cac tham chieu checkpoint (best-effort; loi checkpoint khong phuc hoi ban ghi da xoa).
- Khong ghi mat khau vao log hoac man hinh xac nhan.

CAI BAN VA TREN V1.4
1. DONG tool; sao luu ma nguon va thu muc %LOCALAPPDATA%\AndroidMultiDeviceV1.
2. Giai nen ANDROID_MULTI_DEVICE_V1_DELETE_ACCOUNT_PATCH.zip vao thu muc goc du an;
   cho phep ghi de 4 file C# cung duong dan tuong doi.
3. Chay BUILD_RUN.bat.
4. Mo kho -> chon dong trong KHO TAI KHOAN -> Xoa khoi kho.

FILE THAY DOI:
AndroidMultiDeviceV1/MainForm.cs
AndroidMultiDeviceV1/Core/Storage.cs
AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs
AndroidMultiDeviceV1/Core/SessionCheckpointStore.cs

TEST CAN THUC HIEN TREN WINDOWS:
(1) 1 tai khoan AVAILABLE chua gan -> xoa -> bien mat sau khoi dong lai.
(2) 1 tai khoan AVAILABLE da gan vao may dang IDLE -> xoa duoc.
(3) May dang RUNNING/STOPPING -> xoa bi chan; tai khoan con nguyen.
(4) Tai khoan RESERVED/ACTIVE -> bi chan du worker da dung.
(5) Chon nhieu tai khoan, 1 trong so do dang chay -> khong xoa dong nao.
(6) An No trong dialog -> khong thay doi gi.
(7) Xoa tren Phone 01 trong khi Phone 02 dang chay -> khong anh huong Phone 02.
(8) Nhap lai tai khoan da xoa qua TXT/Excel -> nhan dien nhu ban ghi moi.
(9) Sau khoi dong lai, kho van mo bang mat khau cu, tai khoan khac con nguyen.

Luu y: xoa kho ma hoa dang su dung khong dam bao xoa cac ban sao luu,
file TXT/Excel goc, screenshot/log da tao hoac snapshot o noi khac.

KIEM TRA: JSON/config goc giu nguyen; can BUILD va test UI tren Windows
vi moi truong hien tai khong co dotnet Windows hoac thiet bi XCloudPhone.
