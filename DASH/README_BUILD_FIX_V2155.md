# V2.1.55 — Fix build errors CS0136 + warning cleanup

Source: V2.1.54 NEXT_RANKED_LIVE_CANDIDATE_FIX. All business behavior, account vault, settings, card ranking, OCR, Phase2 and Phase3 logic are otherwise unchanged.

## Fixes
- CS0136 (LiveSearchEngine.cs): method parameter `attempt` renamed to `candidateAttempt` for `ValidateOpenedLiveAsync`; inner `for (int attempt=...)` loops are unchanged. This fixes errors previously reported at lines 1217, 1270 and 1294. Diagnostics remain in phase3/attempt-XX directory.
- CS8629 (LiveSearchEngine.cs): nullable audience counts are extracted via integer pattern matching in the same-screen duplicate predicate; logic stays the same.
- CS0108 (LiveSettingsDialog.cs): private settings getter renamed `CaptureSettings` so it no longer hides WinForms Control.Capture.

## On Windows
1. Extract the archive to a fresh folder (do not mix older source versions).
2. Run `BUILD_RUN.bat` to compile and start the program.
3. If build fails, send the complete new build log.

Offline verification: source files changed exactly at the three intended areas, ZIP CRC/integrity check passed. `.NET 8 SDK` is unavailable in this execution environment, so compilation and Android runtime tests have NOT been performed here.
