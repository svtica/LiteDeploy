# LiteSuite.Common

Components shared by the LiteSuite tools. Built for .NET Framework 4.8 (LiteDeploy) and
.NET 10 (LiteTask, and LiteDeploy after its migration), C# 7.3, no dependency on the
application that uses them. `LiteSuite.Common.dll` ships next to `LiteDeploy.exe` and is
signed with it.

The library lives in the LiteDeploy repository until LiteTask adopts it; it then moves to
its own repository.

## Contents

| Type | Purpose | Origin |
|---|---|---|
| `PsExecCommandLine` | PsExec arguments: quoting (CommandLineToArgvW rules), credential form, executable and batch commands, progress line recognition | LiteTask `PsExecCommandBuilder` |
| `AuthenticodeVerifier`, `AuthenticodeResult`, `AuthenticodeStatus` | Authenticode check of a file (WinVerifyTrust) and signer thumbprint | LiteTask `AuthenticodeVerifier` |
| `UpdateSignaturePolicy`, `UpdateSignatureVerdict` | Accept or refuse an update package from its signature | LiteTask `AuthenticodeVerifier.vb` |
| `MarkOfTheWeb` | Removes the `Zone.Identifier` stream (like `Unblock-File`) | LiteTask `MarkOfTheWeb` |
| `ProcessFailureFormatter` | Failure message of a child process: exit code, duration, stderr, end of stdout | LiteTask `ProcessFailureFormatter` |
| `ParameterParser` | `key=value` and `-Name Value` parameter strings | LiteTask `ParameterParser` |

Tests: `tests/LiteSuite.Common.Tests`, run on both frameworks by the CI
(`dotnet test tests\LiteSuite.Common.Tests -c Release`).

## Differences with the LiteTask code

- Namespace `LiteSuite`; `PsExecCommandBuilder` is named `PsExecCommandLine` (LiteDeploy keeps
  its own `PsExecCommandBuilder`, which builds a deployment command line from its options).
- `ProcessFailureFormatter` writes the duration with the invariant culture (`39.8 s`); LiteTask
  used the current culture (`39,8 s` on a French Windows).
- `ProcessFailureFormatter` counted the `...` truncation line in "stdout (last N of M lines)"
  (41 instead of 40); it now gives the number of lines kept.
- `MarkOfTheWeb` uses the Win32 file functions: on .NET Framework, `File.Exists` and
  `File.Delete` refuse a path such as `file.exe:Zone.Identifier`, so the LiteTask code would
  never find a mark there. Same behavior on .NET 10.

## Adopting it in LiteTask

1. Reference `LiteSuite.Common.csproj` (or the DLL) from `LiteTask.vbproj` (net10.0-windows).
2. Replace the six LiteTask files listed above with `Imports LiteSuite`, renaming
   `PsExecCommandBuilder` calls to `PsExecCommandLine`.
3. Remove the matching LiteTask tests (they are ported here) and ship `LiteSuite.Common.dll`
   next to `LiteTask.exe`.

Candidates for a later version (they depend on LiteTask's logger or settings today):
`CredentialManager`, `Logger`, `TranslationManager`.
