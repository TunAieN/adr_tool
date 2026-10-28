V5.9.1 – FIX BUILD CS1929 / CS8629
=================================
A. Apply ONLY over AccV59 source (or use the V5.9.1 FULL source):
   1. Stop the app and make a backup of source, settings.json, account-vault.json.
   2. Extract the PATCH ZIP into the directory containing the DASH folder.
      Overwrite DASH\AndroidMultiDeviceV1\Core\LoginWorkflow.cs and Storage.cs.
   3. Run DASH\BUILD_RUN.bat (Windows, .NET 8 SDK).

B. Changed source lines:
   LoginWorkflow.cs: XDocument.Parse(xml).Root?.DescendantsAndSelf().Any(...) ?? false.
   Storage.cs: use a pattern-matched non-null LastStart before conversion.

C. These changes only correct a C# compilation error and one nullable warning.
   Account policies, Google picker UI behavior, vault schema, CAPTCHA,
   Live Search, OCR and Phase 5 were not intentionally changed.

D. Source checks (not an actual C# build):
   python DASH\TEST_BUILD_FIX_V591.py
   python DASH\TEST_RETRY_REBIND_V59.py
   Run BUILD_RUN.bat on Windows to confirm compiler success.
