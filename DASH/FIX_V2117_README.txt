V2.1.17 - LIVE Search diagnostics and pending tracking fix (2026-10-23)

- Default keyword timeout: 240s, total: 600s. On load, migrate the EXACT original 65/130 pair only. Custom settings remain unchanged.
- Default swipe: 40%; existing saved swipe percentage is retained.
- Save observed PENDING card data separately from confirmed NEW cards. Enables matching pending candidate fingerprints and viewer evidence against subsequent viewport without falsely counting them as confirmed NEW.
- Preserve pending state when a previously pending card is matched to its old ID.
- Log per-keyword and total remaining seconds, measured visual displacement (0 means unverified), and indicate which timeout was reached.
- Never automatically count unresolved or duplicate cards as NEW. Does not enter LIVE rooms or post comments.

BUILD: Run BUILD_RUN.bat on Windows with .NET 8 SDK installed.
IMPORTANT: This runtime does not provide dotnet, Android/Appium or TikTok access. Build and device integration tests could not be performed here. Back up existing data before replacing the application.
