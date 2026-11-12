# Update modules

A module updates one product that winget does not cover (Adobe Acrobat Reader patches,
1Password, an in-house application...). It is a **signed PowerShell script**; LiteDeploy runs it
on the computers through `LiteDeploy.Worker.exe`, as SYSTEM, like the winget packages.

## How it runs

1. In *Tools > Software Updates > Package list...*, *Add module* puts `module:<Name>` in the
   package list (processes and label come from the module header).
2. *Check* or *Update checked*: LiteDeploy copies the module to
   `\\<computer>\ADMIN$\LiteDeploy\modules\<Name>.ps1` (`%windir%\LiteDeploy\modules`, a folder
   only administrators and SYSTEM can write to), then starts the worker with PsExec (`-s`).
3. The worker checks the **Authenticode signature** of the script. It runs it only if the
   signature is valid and from the LiteSuite publisher (the certificate that signs the
   LiteDeploy releases, pinned like in LiteTask) or from the signer of the worker itself.
   A tampered script, another publisher or an invalid signature is always refused.
4. `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File <Name>.ps1 -Action Check`
   reports the versions; on *Update checked*, when the module reports `outdated`, the worker
   closes the processes of the header, runs `-Action Update`, then `-Action Check` again.

Unsigned modules are refused. For tests only, `AllowUnsignedModules` set to `true` in
`%LOCALAPPDATA%\LiteDeploy\settings.xml` lets the worker run them (`--allow-unsigned-modules`):
never in production, the script runs as SYSTEM on every computer of the run.

## Where modules are found

| Folder | Content |
|---|---|
| `modules\` next to `LiteDeploy.exe` | Modules shipped with LiteDeploy (signed by the release) |
| `%LOCALAPPDATA%\LiteDeploy\modules\` | Your own modules (same name: yours wins) |

A file is a module when one of its first 20 lines is `# LiteDeploy-Module`. Its name (file
name without `.ps1`) has letters, digits and `. - _` only.

## Shipped modules

From the MAJ-Logiciels tool of Patrick Bédard:

| Module | Product | How |
|---|---|---|
| `AdobeReader` | Adobe Acrobat Reader and Acrobat (MSI installs, 32 and 64-bit, MUI) | Product recognized by its MSI product code; latest build among the versions winget knows; MSP patch from `ardownload2.adobe.com`, signed by Adobe, applied with `msiexec /p`. Outlook (Adobe add-ins) is not closed: when it blocks the patch, the message says so |
| `OnePassword` | 1Password installed per machine | Latest version from winget; official MSI from `downloads.1password.com`, signed by AgileBits; `MANAGED_INSTALL=1 MANAGED_UPDATE=1`; old version removed first on 1638; Start menu host restarted if the MSI hangs on `AI_UnpinFromStartScreen` |
| `OracleJava` | Oracle Java 8 (winget `Oracle.JavaRuntimeEnvironment`) | Stops only the system Java (bundled Java runtimes are left alone), upgrades with winget, checks that `java8path` still answers, asks for a restart (3010) when it stopped Java |

The winget packages of the same tool (7-Zip, Notepad++, VLC) are in the example package list,
`updates/packages.txt`.

They are signed with the release. Until the release is signed (`SIGNING_CERTIFICATE` secret),
the worker refuses them, except with `AllowUnsignedModules` for a test.

## Shared installer cache

Optional (*App > Options > Software updates*): a shared folder, `\\server\share`. The worker
gives it to the modules (`LITEDEPLOY_CACHE`); `AdobeReader` and `OnePassword` take their
installer from `<cache>\Adobe` or `<cache>\1Password` when a copy is there, and put the one
they download there for the other computers (one writer at a time, `.lock`). The signature of
the vendor is checked before every install, whether the file comes from the cache or from the
Internet, so a file changed in the cache is refused.

The modules run as SYSTEM: on the computers they use the **computer account**
(`DOMAIN\COMPUTER$`). Give *Domain Computers* read access to the share, and write access only if
the computers should fill the cache themselves.

## Writing a module

Start from [modules/Template-Module.ps1](modules/Template-Module.ps1).

Header (first lines):

```powershell
# LiteDeploy-Module
# Label: Adobe Acrobat Reader
# Processes: Acrobat, AcroRd32, AcroCEF, RdrCEF
```

Contract:

| | |
|---|---|
| Parameter | `-Action Check` or `-Action Update` |
| Result | one line `LDW1<TAB>type=module<TAB>status=...` (function `Write-LiteDeployResult` of the template); `status`: `uptodate`, `outdated`, `notinstalled` or `error`; optional `installed`, `available`, `message` (URI-escaped) |
| Exit code | `0`; `3010` after an update that needs a restart; anything else is a failure |
| Time limit | 15 minutes per action |

Rules:

- Keep the script **ASCII only** (Windows PowerShell 5.1 reads a file without BOM as ANSI).
- Download only over HTTPS, to a folder only SYSTEM and administrators can write to
  (`%windir%\LiteDeploy\work`), **never** `C:\Windows\Temp`.
- Check the signature of every installer before running it (`Get-AuthenticodeSignature`,
  status `Valid` and the vendor's organisation in the subject).
- Never put a password or a key in a module.

Sign it with the LiteSuite code-signing certificate (the one of the `SIGNING_CERTIFICATE`
secret), with a timestamp:

```powershell
Set-AuthenticodeSignature .\MyModule.ps1 -Certificate $cert -TimestampServer http://timestamp.digicert.com -HashAlgorithm SHA256
```

Modules put in `modules\` of the repository are shipped in `LiteDeploy.zip` and signed by the
release workflow with the rest of the package.
