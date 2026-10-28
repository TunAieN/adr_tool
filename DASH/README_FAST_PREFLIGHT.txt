V2170 FAST PREFLIGHT TEST - 27-10-2026
======================================
MUC TIEU
  Tang toc luong TEST account moi -> pm clear -> onboarding -> Google ->
  verify username -> handoff LIVE; giu nguyen LIVE engine / OCR / Phase 5.

DA SUA
  [1] Core/FastPreflightIdleScope.cs (moi)
      UIAutomator2 waitForIdleTimeout 10000ms -> 1000ms (neu gia tri goc mac dinh),
      chi trong session PRE-FLIGHT cua dung thiet bi. Read-back de xac minh.
      Khoi phuc idle ban dau TRUOC LIVE handoff va trong teardown / Stop.
      Neu Appium khong ho tro setting: khong ep doi, tiep tuc cau hinh goc.
  [2] Core/DeviceOrchestrator.cs
      Bat preflight scope o session TEST; bao cao tong thoi gian Preflight;
      Restore truoc khi vao LiveVerifiedSessionHandoff. Khong tao session LIVE moi.
  [3] Core/TikTokStateManager.cs
      Profile bottom tap theo bounds XML duoc kiem chung, thay cho thu 3 XPath;
      chuyen fallback cu neu khong co bounds.
      Kiem tra popup Contacts trong tung vong cho Google / first launch,
      chi nhan Don't allow khi XML co chinh xac TikTok + contacts +
      Android PermissionController deny ID.
      Khi Source XML da chung minh TikTok-owned UI thi bo qua package RPC thua.
  [4] Core/LoginWorkflow.cs
      Rut ngan cho contacts ngay sau I agree tu 12s xuong 3s;
      giao viec popup xuat hien muon cho vong post-Google;
      ghi elapsedMs cho buoc post-Google.

KHONG DOI
  - pm clear TikTok + Google Play Services theo thiet ke hien tai.
  - Continue with Google / credential vault / handle verification.
  - LiveSearchEngine / LiveVisualOcr / phan biet Person-Like / Phase 5.
  - Concurrency, systemPort, session recovery, room flow.

CAI DAT
  FULL: Giai nen FAST.zip vao thu muc moi, chay BUILD_RUN.bat tren Windows
        co .NET 8 SDK. Nen giu thu muc ban cu de rollback.
  PATCH: Giai nen PATCH.zip de vao goc thu muc code V2170 goc, cho phep
         ghi de 3 file cu va them 1 file moi. KHONG giai nen vao dist.
  Thu FAST TEST tren mot thiet bi truoc khi dung chung multi-device.

LOG CAN DOI CHIEU
  FAST_PREFLIGHT_IDLE_APPLIED  previousMs=10000 currentMs=1000
  FAST_PROFILE_TAP             verifiedBottomBounds=true
  FAST_POST_GOOGLE_CONTACTS_DENIED
  FAST_POST_GOOGLE_RESULT      elapsedMs=... uiReady=...
  FAST_PREFLIGHT_RESULT        elapsedMs=... outcome=Ready
  FAST_PREFLIGHT_IDLE_RESTORED currentMs=10000 verified=true
  SAFE_IDLE_APPLIED            scope=LIVE_WORKFLOW (ma LIVE goc)

KIEM TRA
  python TEST_FAST_PREFLIGHT.py
  dotnet build AndroidMultiDeviceV1/AndroidMultiDeviceV1.csproj -c Release
  Sau do kiem tra thuc te Phone 02 / 08 voi luong TEST tai khoan moi.

CANH BAO
  Khong co .NET 8 SDK hoac XCloudPhone trong moi truong build o day.
  Da kiem tra source/offline; chua xac nhan build Windows hoac toc do that.
  Khong cam ket mot muc giam thoi gian co dinh tren moi cloud phone.
