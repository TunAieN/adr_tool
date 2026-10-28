ACCOUNT LIFECYCLE V5.4 - THAY DOI HIEN THI (DUA TREN AccV53)
============================================================
1. Cau hinh theo tung Phone: "Gioi han thoi gian chay tai khoan (gio)".
   - So phut goc trong settings.json va worker KHONG doi don vi.
   - Vi du: 3600 phut -> UI 60.00 gio; 10 phut -> UI 0.17 gio.
   - Khi bam Luu, quy doi ve phut gan nhat. Toan bo gia tri 1..10080
     phut duoc kiem thu la khong doi sau mo -> luu.
   - So thap phan sau dau phay de giu tuong thich voi cai dat cu < 1 gio.
2. Kho tai khoan: bo cot "Con lai", rut gon "Da chay" theo hh:mm:ss.
   - Tong gio khong ve 00 khi vuot 24 gio. Vi du 25:03:09.
   - Tai khoan chua chay: 00:00:00; du lieu khong du: Chua xac dinh.
   - Tai khoan lich su V5 cu thieu bang chung: xem tooltip cot Da chay.
3. Backend remaining, deadline, auto switch, pm clear, per-device policy,
   Live CAPTCHA, Google Login, LIVE/OCR/Phase 5 van GIU NGUYEN.

CAI PATCH TU V5.3
1. STOP tat ca worker va thoat tool, sao luu source, settings.json
   va account-vault.json.
2. Giai nen P54.zip tai thu muc GOC cua source V5.3 (thu muc co DASH).
   Chon ghi de; file moi Core/AccountDurationDisplay.cs duoc them vao.
3. Chay DASH/BUILD_RUN.bat tren Windows (.NET 8 SDK).
4. Thu o Phone 04: 3600 phut cu phai hien 60.00 gio; luu lai khong doi
   so phut. Kiem tra cot Da chay va dieu khien cho phep chuyen TK.

KIEM TRA
- python DASH/TEST_DISPLAY_V54.py
- Tests V5.2/V5.3, FAST, Dashboard V1/V2, Profile, Gallery.
- Chua build WinForms / test tren XCloudPhone tai moi truong hien tai.
