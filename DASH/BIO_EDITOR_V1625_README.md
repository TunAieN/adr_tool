# V1.6.2.5 — Bio editor navigation fix

Based on V1.6.2.4. Fixes Bio not opening after the successful Name change.

- Detect Edit profile screen by screen heading + Name/Bio rows (not just tvContent).
- Try a clickable Bio row / parent and verify Bio edit screen and `hvc` before typing.
- Keep Bio journal: `SAVE_PENDING` prevents duplicate Save on uncertain results; read back Edit profile row and mark VERIFIED if it matches.
- No changes to Multi-Device or account vault.

Install PATCH by copying `AndroidMultiDeviceV1/Core/ProfileUpdates.cs` and
`AndroidMultiDeviceV1/Config/selectors.json` over V1.6.2.4 source.
Run `BUILD_RUN.bat` on Windows. Back up local configuration first.

Test status: source/JSON/package validation only; Windows compilation and live device not tested.
