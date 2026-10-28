# V2.1.26 — OCR INTEGER EXTENSION GUARD

Fixes the false reading **22770** for a badge showing **227**. When two OCR
readings of the same non-abbreviated integer badge differ by an added suffix
of 2+ digits (for example `227` vs `22770`), the sample is classified as
`SuspiciousIntegerExtension` / OCR conflict and is **excluded from viewer-based
ranking**. This conservative fix never guesses the shorter number solely from
conflicting OCR: it prevents inflated counts and wrong room selection.

Preserved: 4+2+2 = 8 samples stop; phase2 screen matching/reverse scrolling
and opening verified target; screenshot/diagnostic logging. No automatic
comments are added.

Check example after build: `wide=22770`, `contrast=227`, `alternate=22770`
should log `quy tắc=SuspiciousIntegerExtension`, Viewer=?, then rank a reliably
read other card (e.g. 955) instead.

Run `BUILD_RUN.bat` on Windows with .NET 8 SDK, Appium and the Android device.
Not compiled or Android-tested in this environment.
