"""Static source guard for V5.9.1 compile fixes. Not a substitute for dotnet build."""
from pathlib import Path
import re
root = Path(__file__).parent / 'AndroidMultiDeviceV1' / 'Core'
login = (root / 'LoginWorkflow.cs').read_text(encoding='utf-8-sig')
storage = (root / 'Storage.cs').read_text(encoding='utf-8-sig')
assert 'XDocument.Parse(xml).Root?.DescendantsAndSelf().Any(IsUseAnotherAccountNode) ?? false' in login
assert 'XDocument.Parse(xml).DescendantsAndSelf()' not in login
assert not re.search(r'XDocument\.Parse\(\s*\w+\s*\)\.DescendantsAndSelf\s*\(', login)
assert 'if(a.LastStart is { } started)' in storage
assert 'a.FirstUsedAtUtc ??= started.ToUniversalTime();' in storage
assert 'a.FirstUsedAtUtc ??= a.LastStart.Value.ToUniversalTime();' not in storage
assert 'GOOGLE_ACCOUNT_PICKER_TARGET_CLICKED' in login
assert 'VerifiedAnotherAccountPoint(xml)' in login
assert 'return pool.FirstOrDefault(a=>a.State=="AVAILABLE" && !WasUsed(a));' in storage
print('PASS: CS1929 XDocument Root traversal')
print('PASS: CS8629 nullable check')
print('PASS: Google picker semantics, selection policy preserved')
