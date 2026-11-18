# Update modules

A module updates one product that winget does not cover (Adobe Acrobat Reader patches,
1Password, an in-house application...). It is a **signed PowerShell script**; LiteDeploy runs it
on the computers through `LiteDeploy.Worker.exe`, as SYSTEM, like the winget packages.

## How it runs

1. In *Tools > Software Updates > Package list...*, *Add module* puts `module:<Name>` in the
   package list (processes and label come from the module header); *Parameters...* sets the
   values of its parameters, if it has any (see *Module parameters*).
2. *Check* or *Update checked*: LiteDeploy copies the module to
   `\\<computer>\ADMIN$\LiteDeploy\modules\<Name>.ps1` (`%windir%\LiteDeploy\modules`, a folder
   only administrators and SYSTEM can write to), then starts the worker as SYSTEM (WinRM and a one-time scheduled task, or PsExec `-s`).
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
| Shared modules folder (*App > Options > Software updates*), usually a network share | Modules of your IT team, used by the LiteDeploy of every administrator (same name: wins over the shipped one) |
| `%LOCALAPPDATA%\LiteDeploy\modules\` | Your own modules (same name: yours wins over the shared and the shipped ones) |

A file is a module when one of its first 20 lines is `# LiteDeploy-Module`. Its name (file
name without `.ps1`) has letters, digits and `. - _` only.

When the shared folder cannot be reached (offline, no access), its modules are not listed and
the package list editor says so; the shipped and your own modules are still listed.

### Shared modules folder: permissions

The shared folder changes nothing to the security of the run: wherever a module comes from,
the worker runs it only with a valid Authenticode signature from the expected publisher (step 3
above). Still, a module runs as SYSTEM on every computer of a run, so:

- give **write** access to the folder (share and NTFS) only to the IT administrators who
  publish modules; everyone else who uses LiteDeploy needs read access only;
- put only signed modules there, and do not set `AllowUnsignedModules` on a computer that
  uses the shared folder.

LiteDeploy reads the folder with the account of the administrator who runs it (the computers
never read it: the module is copied to `ADMIN$` first).

## Shipped modules

From an earlier in-house update script:

| Module | Product | How |
|---|---|---|
| `AdobeReader` | Adobe Acrobat Reader and Acrobat (MSI installs, 32 and 64-bit, MUI) | Product recognized by its MSI product code; latest build among the versions winget knows; MSP patch from `ardownload2.adobe.com`, signed by Adobe, applied with `msiexec /p`. Outlook (Adobe add-ins) is not closed: when it blocks the patch, the message says so |
| `OnePassword` | 1Password installed per machine | Latest version from winget; official MSI from `downloads.1password.com`, signed by AgileBits; `MANAGED_INSTALL=1 MANAGED_UPDATE=1`; old version removed first on 1638; Start menu host restarted if the MSI hangs on `AI_UnpinFromStartScreen` |
| `OracleJava` | Oracle Java 8 (winget `Oracle.JavaRuntimeEnvironment`) | Stops only the system Java (bundled Java runtimes are left alone), upgrades with winget, checks that `java8path` still answers, asks for a restart (3010) when it stopped Java |
| `PrinterDrivers` | Printer drivers listed in the shared cache (`<cache>\PrinterDrivers\drivers.csv`) | Check: every listed driver installed (`Get-PrinterDriver`), else `outdated` with the number missing; `error` without a cache or a readable list. Update: copies the package of each missing driver to `%windir%\LiteDeploy\work\PrinterDrivers`, refuses it unless its catalog (`.cat`) has a valid Authenticode signature, `pnputil /add-driver`, `Add-PrinterDriver`. No process closed, no printer created |
| `TeamsBootstrapper` | New Microsoft Teams provisioned for all users (`MSTeams` package) | Check: provisioned version against the offline package of the cache (`<cache>\Teams\MSTeams-x64.msix`) when there is one; `notinstalled` when Teams is not provisioned. Update: `teamsbootstrapper.exe` from the cache or from Microsoft's documented link, signed by Microsoft Corporation, run with `-p` (or `-p -o <msix>` with the offline package, also signed by Microsoft); its JSON output must say `success`. Closes `ms-teams` |

`PrinterDrivers` and `TeamsBootstrapper` are generic: their content comes from the shared installer
cache (see below), nothing in the module is specific to an organisation. Like every module, they
only act on computers where the check says `outdated`: `TeamsBootstrapper` updates the computers
where Teams is already provisioned and reports `notinstalled` elsewhere.

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

### Cache layout

```text
<cache>\
  Adobe\                        AdobeReader (filled by the computers when writable)
  1Password\                    OnePassword (filled by the computers when writable)
  PrinterDrivers\               PrinterDrivers (filled by an administrator, read only for the computers)
    drivers.csv
    ExampleVendor\Universal\x64\example.inf, example.cat, ...
    ExampleVendor\Label\x64\label.inf, label.cat, ...
  Teams\                        TeamsBootstrapper (filled by an administrator, read only for the computers)
    teamsbootstrapper.exe       optional: downloaded from Microsoft when absent
    MSTeams-x64.msix            optional offline package (also MSTeams-x86.msix, MSTeams-arm64.msix)
```

`PrinterDrivers\drivers.csv` has the columns `Name,Inf`: `Name` is the driver name exactly as
`Get-PrinterDriver` shows it once installed (the model name in the INF), `Inf` the path of the INF
relative to the `PrinterDrivers` folder. The whole folder of the INF is copied to the computer:
give each driver package its own folder (an INF directly in `PrinterDrivers` is refused, and so is
a path that leaves the folder). Several drivers of the same INF are staged once.

```text
Name,Inf
Example Printer Driver,ExampleVendor\Universal\x64\example.inf
Example Printer Driver PS,ExampleVendor\Universal\x64\example.inf
Example Label Printer,ExampleVendor\Label\x64\label.inf
```

Use the extracted driver package of the vendor (INF, catalog and files), not its setup program.
The module refuses a package whose catalog files (the `CatalogFile` entries of the INF, else the
`.cat` files next to it) are missing or not validly signed; `pnputil` then checks the files of the
package against the catalog when it stages it.

`Teams\`: `teamsbootstrapper.exe` and the offline packages come from Microsoft's page *Bulk deploy
the Microsoft Teams client* (learn.microsoft.com). Without a copy in the cache, the module
downloads the bootstrapper from the link of that page
(`https://go.microsoft.com/fwlink/?linkid=2243204&clcid=0x409`). Both files are refused unless they
have a valid signature of Microsoft Corporation. Keep the bootstrapper current: Microsoft asks for
its latest version. Without an offline package, the check has no version to compare with
(Teams updates itself) and reports `uptodate` with the provisioned version; put a newer
`MSTeams-<arch>.msix` in the cache to roll out a version.

Permissions for these two folders: the computer accounts (*Domain Computers*) need **read**
only; write access stays with the administrators who fill them.

## Writing a module

Start from [modules/Template-Module.ps1](modules/Template-Module.ps1).

Header (first lines):

```powershell
# LiteDeploy-Module
# Label: Adobe Acrobat Reader
# Processes: Acrobat, AcroRd32, AcroCEF, RdrCEF
```

Contract (version 2; a module without `# Parameter:` line is a version 1 module and runs as before):

| | |
|---|---|
| Action | `-Action Check` or `-Action Update` |
| Parameters | Environment variables `LITEDEPLOY_PARAM_<Name>`, one per parameter of the header (see *Module parameters*); an empty value sets no variable |
| Cache | `LITEDEPLOY_CACHE`: the shared installer cache, when one is set |
| Result | one line `LDW1<TAB>type=module<TAB>status=...` (function `Write-LiteDeployResult` of the template); `status`: `uptodate`, `outdated`, `notinstalled` or `error`; optional `installed`, `available`, `message` (URI-escaped) |
| Exit code | `0`; `3010` after an update that needs a restart; anything else is a failure |
| Time limit | 15 minutes per action |

Rules:

- Keep the script **ASCII only** (Windows PowerShell 5.1 reads a file without BOM as ANSI).
- Download only over HTTPS, to a folder only SYSTEM and administrators can write to
  (`%windir%\LiteDeploy\work`), **never** `C:\Windows\Temp`.
- Check the signature of every installer before running it (`Get-AuthenticodeSignature`,
  status `Valid` and the vendor's organisation in the subject).
- Never put a password or a key in a module: use a secret parameter.

### Module parameters

One generic module can be configured per site (an environment name, the file name of the
installer in the cache, an enrollment token...). Each parameter is one header line, in the
first 20 lines of the script:

```powershell
# Parameter: Environment default=Production Name of the environment the product connects to
# Parameter: InstallerFile default="example 1.2.msi" File name of the installer in the shared cache
# Parameter: EnrollmentToken secret Enrollment token (password of a stored credential)
```

| Part | Rule |
|---|---|
| Name | `^[A-Za-z][A-Za-z0-9_]{0,31}$`; the module reads `$env:LITEDEPLOY_PARAM_<Name>` |
| `secret` | Optional, right after the name: the value is the password of a stored credential |
| `default=<value>` | Optional, right after the name: value used when none is set (`default="a b"` for spaces, `default=` for an empty value). Without default, the parameter is **required** |
| Description | The rest of the line, shown in LiteDeploy |

The administrator sets the values in *Tools > Software Updates > Package list...*: select a
`module:` row, then *Parameters...*. They are saved on the administrator's computer in
`%LOCALAPPDATA%\LiteDeploy\module-settings.xml` (not in `packages.txt`, whose format does not
change). A secret parameter holds the **name** of a credential of *Tools > Credentials*, never
the secret: the dialog offers the stored credentials in a list and never shows or asks for a
password; the password of that credential is read only when a run starts.

On each run, LiteDeploy writes `<Name>.params` next to `<Name>.ps1` in
`\\<computer>\ADMIN$\LiteDeploy\modules`, with access for SYSTEM and Administrators only (set when
the file is created: `%windir%` lets every user read). The worker reads the file, deletes it at
once, and gives the values of the parameters **declared in the header of the signed script** to
the module process only, as environment variables (any other value of the file is ignored). A
required parameter without value: the module is not started and its result is `error`, with the
names of the missing parameters. At the end of the run the worker deletes the parameter files of
every module of the run, whatever happened, and LiteDeploy deletes them again through `ADMIN$`
(in case the worker could not start). The same transport works with both start methods (WinRM and
a one-time scheduled task, or PsExec `-s`): no value is ever put on a command line or in a
scheduled task.

Security of secret parameters:

- A secret value never appears on a command line, in the worker messages, in the console, in the
  logs of LiteDeploy or in `updates.xml`: the worker replaces it with `***` in everything the
  module reports (result line, errors), and the error messages of LiteDeploy name the parameter
  and the credential, never the value.
- On the computer the value exists only in the parameter file (administrators and SYSTEM, for
  the time between the copy and the start of the module) and in the environment of the module
  process (SYSTEM).
- The file is copied over SMB (`ADMIN$`), like the module itself: require SMB signing, and
  SMB encryption where the network is not trusted.
- The module must not write the value to its output, a log or an exception message. When an
  installer needs it on its command line, that command line is visible to the local
  administrators of the computer while it runs, and an MSI verbose log (`/l*v`) records the
  properties: do not enable one.
- Parameters are set from the LiteDeploy application only: `Invoke-LiteDeployWorker` of the
  PowerShell module does not pass them (a required parameter is then reported missing).

Sign it with the LiteSuite code-signing certificate (the one of the `SIGNING_CERTIFICATE`
secret), with a timestamp:

```powershell
Set-AuthenticodeSignature .\MyModule.ps1 -Certificate $cert -TimestampServer http://timestamp.digicert.com -HashAlgorithm SHA256
```

Modules put in `modules\` of the repository are shipped in `LiteDeploy.zip` and signed by the
release workflow with the rest of the package.
