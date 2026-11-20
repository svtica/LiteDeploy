# Update modules

A module updates one product that winget does not cover (Adobe Acrobat Reader patches,
1Password, an in-house application...). It is a **signed PowerShell script**; LiteDeploy runs it
on the computers through `LiteDeploy.Worker.exe`, as SYSTEM, like the winget packages.

## How it runs

1. In *Software catalog...* (tool bar of the main window), *Add module* puts `module:<Name>` in the
   package list (processes and label come from the module header); *Parameters...* sets the
   values of its parameters, if it has any (see *Module parameters*). A module that declares
   `# Instances: yes` is added once per product, as `module:<Name>/<Instance>` (see *Module
   instances*).
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
5. On the explicit *Install* action (see *Install action* below), when the module declares
   `# Install: yes` and reports `notinstalled`, the worker runs `-Action Install`, then
   `-Action Check` again.

Unsigned modules are refused. For tests only, `AllowUnsignedModules` set to `true` in
`%LOCALAPPDATA%\LiteDeploy\settings.xml` lets the worker run them (`--allow-unsigned-modules`):
never in production, the script runs as SYSTEM on every computer of the run.

Servers: on a server the worker runs no `Update` and no `Install` (of a module or of a winget
package) unless *Include servers* is checked in *App > Options* (`--allow-servers`); a domain
controller also needs `AllowDomainControllers` in `settings.xml` (`--allow-domain-controllers`).
`Check` runs everywhere. See *Software updates* in the README.

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

Most of them come from an earlier in-house update script:

| Module | Product | How |
|---|---|---|
| `CachedInstaller` | Any vendor MSI or EXE installer kept in the shared cache, one instance per product (see *CachedInstaller*) | Check: installed version (Uninstall entries, 64 and 32-bit, by `ProductCode` or `DetectName`) against the version of the installer; Update/Install: copy to `%windir%\LiteDeploy\work\CachedInstaller`, valid signature of the expected publisher required, silent install, exit code 0, 3010 or 1641, check again. Closes only the processes of its `CloseProcesses` parameter |
| `AdobeReader` | Adobe Acrobat Reader and Acrobat (MSI installs, 32 and 64-bit, MUI) | Product recognized by its MSI product code; latest build among the versions winget knows; MSP patch from `ardownload2.adobe.com`, signed by Adobe, applied with `msiexec /p`. Outlook (Adobe add-ins) is not closed: when it blocks the patch, the message says so |
| `MsixProvision` | Any MSIX package (and its dependency packages) provisioned for all users from the shared cache (`<cache>\<Folder>`) | Check: provisioned version (`Get-AppxProvisionedPackage -Online`, `DisplayName` = `PackageName`) against the version in the manifest of the package of the cache; `notinstalled` when not provisioned; `error` without a cache, a folder or a package of that name. Update (and Install): copies the package, its dependencies and its license to `%windir%\LiteDeploy\work\MsixProvision\<PackageName>`, refuses each package unless validly signed by the `Publisher` of its manifest (and by one of the organisations of the `Publisher` parameter, separated by `;`, when set), enables the optional features of `Features`, `Add-AppxProvisionedPackage -Online -Regions all`. No process closed. See *MsixProvision* below |
| `OnePassword` | 1Password installed per machine | Latest version from winget; official MSI from `downloads.1password.com`, signed by AgileBits; `MANAGED_INSTALL=1 MANAGED_UPDATE=1`; old version removed first on 1638; Start menu host restarted if the MSI hangs on `AI_UnpinFromStartScreen` |
| `OracleJava` | Oracle Java 8 (winget `Oracle.JavaRuntimeEnvironment`) | Stops only the system Java (bundled Java runtimes are left alone), upgrades with winget, checks that `java8path` still answers, asks for a restart (3010) when it stopped Java |
| `PrinterDrivers` | Printer drivers listed in the shared cache (`<cache>\PrinterDrivers\drivers.csv`) | Check: every listed driver installed (`Get-PrinterDriver`), else `outdated` with the number missing; `error` without a cache or a readable list. Update: copies the package of each missing driver to `%windir%\LiteDeploy\work\PrinterDrivers`, refuses it unless its catalog (`.cat`) has a valid Authenticode signature, `pnputil /add-driver`, `Add-PrinterDriver`. No process closed, no printer created |
| `TeamsBootstrapper` | New Microsoft Teams provisioned for all users (`MSTeams` package) | Check: provisioned version against the offline package of the cache (`<cache>\Teams\MSTeams-x64.msix`) when there is one; `notinstalled` when Teams is not provisioned. Update: `teamsbootstrapper.exe` from the cache or from Microsoft's documented link, signed by Microsoft Corporation, run with `-p` (or `-p -o <msix>` with the offline package, also signed by Microsoft); its JSON output must say `success`. Closes `ms-teams` |

`CachedInstaller`, `MsixProvision`, `PrinterDrivers` and `TeamsBootstrapper` are generic: their content comes from the
shared installer cache (see below), nothing in the module is specific to an organisation. Like every
module, they only act on computers where the check says `outdated`: `TeamsBootstrapper` updates the
computers where Teams is already provisioned and reports `notinstalled` elsewhere. `CachedInstaller` and `MsixProvision`
declare `# Install: yes`: they also accept `-Action Install`, which installs the product on a
computer where the check says `notinstalled` (`-Action Update` leaves such a computer alone).
`PrinterDrivers` and `TeamsBootstrapper` do not declare it, so the *Install* action does not offer them.

### MsixProvision

One module for any MSIX package: add it to the package list and set its parameters
(*Parameters...*). It declares `# Instances: yes`, so several packages can be listed, one row per
package (`module:MsixProvision/ExampleApp`, `module:MsixProvision/OtherApp`, each with its own
parameters; see *Module instances*). Example, for a desktop application shipped as an MSIX bundle
that needs a Windows optional feature:

| Parameter | Example value | |
|---|---|---|
| `PackageName` | `Contoso.ExampleApp` | Required: the package identity name (`Name` of the `Identity` of its manifest, also the `DisplayName` of `Get-AppxProvisionedPackage`) |
| `Folder` | `ExampleApp` | Required: folder of the package under the cache |
| `File` | *(empty)* | Package file name in the folder; empty: the newest `.msix` or `.msixbundle` of the folder whose identity is `PackageName`, by manifest version |
| `Dependencies` | *(empty)* | File names in the folder, separated by `;`; empty: the `.appx` and `.msix` files of the `Dependencies` subfolder, for the architectures the computer runs (x64 computer: `x64`, `x86` and neutral) |
| `License` | `ExampleApp_License.xml` | License XML file name in the folder; empty: `-SkipLicense` |
| `Features` | `VirtualMachinePlatform` | Windows optional features to enable first, separated by `;`; a feature that needs a restart: exit code 3010 after the provisioning |
| `Publisher` | `Contoso Ltd` | Expected organisation (`O=`) of the signer of the package, several separated by `;` (any one is accepted); empty: the signer must be the `Publisher` of the manifest (always checked) |
| `WingetId` | `Contoso.ExampleApp` | Optional: winget package Id of the product, for *Tools > Web versions...* (see *Web versions*); ignored by the module |

```text
<cache>\ExampleApp\
  Contoso.ExampleApp_2.1.0.0_x64.msixbundle
  Contoso.ExampleApp_2.0.0.0_x64.msixbundle   older version, ignored (File empty: the newest wins)
  ExampleApp_License.xml                      optional
  Dependencies\                               optional
    Microsoft.VCLibs.140.00_14.0.33519.0_x64.appx
    Microsoft.VCLibs.140.00_14.0.33519.0_x86.appx
```

The version compared is the one of the package manifest (`AppxManifest.xml`, or for a bundle the
newest application package of `AppxBundleManifest.xml`); only that entry is read, the package is
not extracted. Every package file (main package and dependencies) is refused unless its copy has a
valid Authenticode signature whose subject is the `Publisher` of its own manifest: a dependency
signed by Microsoft passes, a file changed in the cache does not. The license file is not signed;
it must be an XML file. To roll out a new version, put its package in the folder (with `File` empty)
or change `File`. Permissions: the computer accounts need **read** access to the folder only.

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
  ExampleApp\                   MsixProvision, folder of its Folder parameter (filled by an administrator, read only for the computers)
    Contoso.ExampleApp_2.1.0.0_x64.msixbundle
    Dependencies\*.appx         optional dependency packages
  PrinterDrivers\               PrinterDrivers (filled by an administrator, read only for the computers)
    drivers.csv
    ExampleVendor\Universal\x64\example.inf, example.cat, ...
    ExampleVendor\Label\x64\label.inf, label.cat, ...
  Teams\                        TeamsBootstrapper (filled by an administrator, read only for the computers)
    teamsbootstrapper.exe       optional: downloaded from Microsoft when absent
    MSTeams-x64.msix            optional offline package (also MSTeams-x86.msix, MSTeams-arm64.msix)
  CachedInstaller\              CachedInstaller (filled by an administrator, read only for the computers)
    ExampleVendor\ContosoExampleApp-2.4.1.msi
    ExampleVendor\ExampleTool-3.2-setup.exe
    ExampleVendor\OLD\ContosoExampleApp-2.4.0.msi   previous version (Tools > Web versions..., see Web versions)
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

Permissions for these folders (`PrinterDrivers`, `Teams`, `CachedInstaller`): the computer
accounts (*Domain Computers*) need **read** only; write access stays with the administrators who
fill them.

## CachedInstaller

Many vendor programs have no winget package: the IT team keeps their installers in
`<cache>\CachedInstaller`. `CachedInstaller` installs or updates any of them, one **instance** per
product (`module:CachedInstaller/ContosoExampleApp`), each with its own row, its own state and its
own parameter values:

| Parameter | Value |
|---|---|
| `Product` | Name shown in the messages (and as the row label when imported); the instance name when empty |
| `File` | **Required.** Path of the installer under the cache, e.g. `CachedInstaller/ExampleVendor/ContosoExampleApp-2.4.1.msi` (`/` or `\`; a path that leaves the cache is refused) |
| `Type` | `msi` or `exe`; empty: from the extension of `File` |
| `Arguments` | Silent install arguments. `msi`: `/qn /norestart` when empty (the module adds `/i <file>` and a log). `exe`: required (no silent switch is common to every vendor), e.g. `/S` or `/quiet /norestart` |
| `DetectName` | Wildcard on the `DisplayName` of the Uninstall entry, e.g. `Contoso Example App*` (at least 3 characters besides the wildcards) |
| `ProductCode` | MSI product code `{GUID}`, the name of the Uninstall key; used instead of `DetectName` when set. One of the two is required |
| `Version` | Version expected once installed; empty: the `ProductVersion` of the MSI (read from the file without installing it), or the product version (else file version) of the EXE. Set it when the installer's own version differs from the `DisplayVersion` the product registers |
| `Publisher` | **Required.** Organisation (`O=`) of the Authenticode signer of the installer, exactly as in the certificate, e.g. `ExampleVendor Inc.`. Several organisations separated by `;` (any one is accepted), e.g. `ExampleVendor Inc.; Fabrikam Inc.` when the vendor renamed itself and signs with its new legal name; a name in double quotes may hold a `;` |
| `CloseProcesses` | Processes the module closes right before the installer runs (comma-separated, without `.exe`) |
| `WingetId` | Optional: winget package Id of the product, e.g. `Contoso.ExampleApp`, for *Tools > Web versions...* (see *Web versions*). Read by LiteDeploy only, on the administrator's computer; the module ignores it |

- **Check**: the Uninstall entries (64 and 32-bit views) matching `ProductCode` or `DetectName`;
  none: `notinstalled`; an older `DisplayVersion` than the expected version: `outdated`; else
  `uptodate`. The cache must be set (`LITEDEPLOY_CACHE`).
- **Update** (outdated computers) and **Install** (`-Action Install`, declared with
  `# Install: yes`, for computers where the product is `notinstalled`; outdated ones too): the
  installer is copied to `%windir%\LiteDeploy\work\CachedInstaller`, refused unless its
  Authenticode signature is `Valid` and the subject of its signer has `O=<Publisher>` (one of the
  organisations of `Publisher`; the refusal names the organisation that signed), the
  processes of `CloseProcesses` are closed, then it runs (`msiexec /i` for an MSI, the EXE itself
  otherwise; 13 minutes at most). Exit code 0: success; 3010 or 1641: success, restart needed
  (reported as 3010); anything else: `error` with the code (and the MSI log path, kept on failure).
  The module then checks again: the product must be found with the expected version.
- **Processes**: the `# Processes:` line of a header is the same for every instance, so it is
  empty here; each product names its own in `CloseProcesses`. The module closes them itself
  (forcefully, as SYSTEM: unsaved work in them is lost) only when it is about to install; plain
  names only, and system processes (`explorer`, `lsass`, `svchost`, `msiexec`...) are always
  refused. The *Processes to close* column of the row also works: the worker closes those
  before *Update*.

Security:

- Only an installer **signed by the expected publisher** runs: a file changed in the cache, an
  unsigned file or another signer is refused, whatever the parameters say. Choose `Publisher`
  from the certificate of the vendor (*Properties > Digital Signatures* of the file), never from
  the file name. When a vendor renames itself, its new installers are signed with the new legal
  name: check the certificate of a new installer, then add that name to `Publisher` (`Old Name
  Ltd; New Name Inc.`) rather than replacing it, so the installer already in the cache stays
  accepted.
- The computers only **read** the cache (*Domain Computers*: read); only the administrators who
  fill `CachedInstaller` can write to it. Write access to the cache would let someone replace an
  installer with another validly signed file of the same publisher (an older version, another
  product): keep it to the IT administrators.
- The arguments are a command line run as SYSTEM: they come from the administrator's settings
  (or catalog), never from the computers. Do not put a secret in them (they are visible to the
  local administrators while the installer runs, and in the MSI log).

### Module instances

A module that declares `# Instances: yes` in its header can be listed several times, once per
instance: `module:<Name>/<Instance>`. The instance name has `A-Z a-z 0-9 . - _` only (64 characters
at most, not starting with a dot). Every instance runs the same script with its own parameter
values; it has its own row (label), its own state in the summary and its own last result.

- *Add module* on such a module asks for the instance name, adds the row, then opens its
  *Parameters...*; *Parameters...* on an instance row edits the values of that instance only.
- Values: `module-settings.xml`, under the key `<Name>/<Instance>`
  (`<Module name="CachedInstaller/ContosoExampleApp">`). An instance does not inherit the values
  of `module:<Name>`; the defaults of the header still apply.
- Run: the script is copied once; each instance has its own parameter file,
  `<Name>@<Instance>.params`. The worker sets `LITEDEPLOY_INSTANCE` to the instance name for the
  module process, and refuses an instance of a module whose signed header does not declare
  `# Instances: yes`.
- `module:<Name>` keeps its meaning: existing package lists, settings and states are unchanged.
  An earlier LiteDeploy ignores the instance rows and settings (`/` was not valid in an Id).

### Catalog

Typing the same fields for every product is error-prone: *Software catalog > Import instances...*
reads a JSON catalog and creates one instance per product with its parameter values. Where the
dialog opens:

1. the team's **`catalogs` folder**, when a shared modules folder is set and a folder named
   `catalogs` exists next to it (in its parent): modules in `...\LiteDeploy\modules`, catalogs in
   `...\LiteDeploy\catalogs`. Every administrator then imports the same catalogs;
2. else `%LOCALAPPDATA%\LiteDeploy`, with `catalog.local.json` proposed.

Any other file can be chosen.

The catalog only serves the administrators' LiteDeploy: the computers never read it (the values
travel in the parameter files, like any parameter). Keep the shared `catalogs` folder out of the
installer cache and give it the permissions of the shared modules folder (write access for the
administrators who publish, read access for the others): whoever can write a catalog chooses the
files and arguments that the administrators import.

```json
{
  "module": "CachedInstaller",
  "products": [
    {
      "Instance": "ContosoExampleApp",
      "Product": "Contoso Example App",
      "File": "CachedInstaller/ExampleVendor/ContosoExampleApp-2.4.1.msi",
      "DetectName": "Contoso Example App*",
      "Publisher": "ExampleVendor Inc.",
      "WingetId": "Contoso.ExampleApp"
    },
    {
      "Instance": "ExampleTool",
      "Product": "Example Tool",
      "File": "CachedInstaller/ExampleVendor/ExampleTool-3.2-setup.exe",
      "Type": "exe",
      "Arguments": "/S",
      "Version": "3.2.0",
      "DetectName": "Example Tool *",
      "Publisher": "ExampleVendor Inc.",
      "CloseProcesses": "exampletool, exampletool-tray"
    }
  ]
}
```

- `module`: optional, `CachedInstaller` by default; the module must declare `# Instances: yes`.
- `products`: one object per product; `Instance` is required (same rules as above), every other
  member is the value of the parameter of the same name (strings; a number is taken as its text,
  `null` means no value). Write paths with `/` (a `\` must be doubled in JSON).
- Import: rows that are not in the list yet are added (label: `Product`, else the instance name);
  for every listed product, the values of the instance are **replaced** by the catalog's (a
  parameter missing from the catalog is cleared). Secret parameters are never read from a
  catalog: set them in *Parameters...*. Products with an invalid instance name, a duplicate
  instance, or values that are not strings are skipped and reported.
- `WingetId` is a parameter like the others: a catalog can give it (see *Web versions*).

## Web versions

*Tools > Web versions...* (also *Software catalog > Web versions...*) tells which products of the
shared cache have a newer version on the web, and can put that version in the cache. It runs in
LiteDeploy, on the **administrator's computer**, with the administrator's account: the managed
computers never check the web nor download anything for it.

One row per instance of `CachedInstaller` and `MsixProvision` that has parameters
(`module-settings.xml`):

| Column | Content |
|---|---|
| Product (instance) | `Product` (`PackageName` for an MSIX), then the instance name |
| Module | `CachedInstaller` or `MsixProvision` |
| Cache version | Version of the file the instance uses in the cache: the `Version` parameter when set (`CachedInstaller`), else the MSI `ProductVersion`, the product (else file) version of the EXE, or the version of the MSIX manifest (bundle: newest application package). `MsixProvision` with `File` empty: the newest package of `PackageName` in the folder, as the module picks it |
| Web version | `Version` of `winget show --id <WingetId> -e --source winget --architecture x64 --installer-type <type>`; the type follows the instance: `msi` then `wix` for an MSI, `exe`, `inno`, `nullsoft` then `burn` for an EXE, `msix` for an MSIX. A zip that holds an installer of that type (winget shows `wix (zip)`) is kept only when no plain installer of the type is offered |
| Status | *Up to date*, *Newer on the web*, *Not found* (not in the winget source), *No WingetId* (listed anyway; set the parameter to compare it), *Not checked* (no result yet: *Check now*), *Error* (cache file missing, versions that cannot be compared, winget unavailable...; the reason is in the tooltip) |
| Source URL | Installer URL of the winget manifest (its SHA256 is kept with it) |

- Versions are compared by their numbers: missing parts count as zero (`26.06` = `26.6.0.0`), a
  build in parentheses is compared with the part that follows (`6.1.5 (12345)` = `6.1.5.12345`).
- The output of winget is read by its layout, not by its labels, so any display language works.
- *Check now* checks every row, up to 3 `winget show` at a time (the first one alone), with a
  progress bar, the item and step under way, the elapsed and estimated remaining time; each row
  shows its result as soon as it is known. *Cancel* stops after the current item and keeps the
  results obtained (the rows not reached keep their previous result). *Open vendor page* opens the home page of the manifest (else the
  publisher page). The last results are kept in `%LOCALAPPDATA%\LiteDeploy\web-versions.xml`;
  every check and download is logged in `%LOCALAPPDATA%\LiteDeploy\logs\web-versions-<date>.log`.
- Sortable columns; the size of the window is remembered.
- The cache and the instances are settings of the Windows user (see *Setting up another
  administrator computer*). When the current user has no cache set, the window says so with
  *Open Options...*; when no instance has parameters yet, with *Import from catalog...* (the import
  of the package list editor; the rows of the new instances are added to the package list).
- The winget packages of `packages.txt` need none of this: the worker compares them with winget
  on each computer. `AdobeReader` and `OnePassword` fetch their installer from the vendor
  themselves.

### Download to the cache

*Download to the cache* (checked rows, else the current row; also on the right-click menu) acts on
rows *Newer on the web* that have an HTTPS installer URL and its SHA256 in the manifest:

1. The installer is downloaded over **HTTPS only** (every redirect too) to a new temporary
   folder of the administrator.
2. It is refused unless its SHA256 is the one of the winget manifest **and** its Authenticode
   signature is `Valid` with `O=<Publisher>` of the instance (one of its organisations separated
   by `;`; `CachedInstaller`: `Publisher` is required). A valid signature of another organisation
   is refused with its name ("signed by *New Name Inc.* instead of *Old Name Ltd*"): if the vendor
   renamed itself, add that name to `Publisher`. For an MSIX: the package name of its manifest must be `PackageName`, the signer
   must be the `Publisher` of its manifest, and `O=<Publisher>` when that parameter is set, as the
   module checks it before provisioning. A download older than the file of the cache is refused.
   An unsigned installer is therefore never put in the cache this way, even with a matching
   SHA256: download it by hand if you trust it.

   **Zip installers.** Some manifests give a zip that holds the MSI or EXE (`InstallerType: zip`,
   `NestedInstallerType`, `NestedInstallerFiles`; `winget show` displays `wix (zip)` and does not
   show the path inside the zip). A zip has no Authenticode signature, so: its SHA256 is checked
   against the manifest, then the **only** `.msi` (or `.exe`, after the `Type` of the instance) it
   holds is extracted to the temporary folder (an entry whose path leaves the folder is refused;
   4 GB at most, 10 000 entries at most) and that file's signature is checked as above. The
   installer, not the zip, goes to the cache with the versioned name. With several files of the
   type in the zip, LiteDeploy keeps the one with the same file name as the current file of the
   instance, else the only one that names x64 (a word such as `x64`, `win64`, `amd64`, `x86_64` or
   `64-bit` in its path), else the only one that is not for another architecture (ARM64, 32-bit: a
   word such as `arm64`, `x86`, `win32`). LiteDeploy only handles x64 installers (as the winget
   query, `--architecture x64`). A zip with no such file, still several of them, or an
   installer of another type: *zip installer: download it manually*.
3. It is copied **next to the current file**, under a versioned name
   (`ContosoExampleApp-2.4.1.msi` -> `ContosoExampleApp-2.5.0.msi`; the version is added when the
   name has none), through a `.partial` file whose SHA256 is checked again. An existing file is
   never overwritten: a file of that name with the same content is kept, another content stops
   the download.
4. The `File` parameter of the instance is set to the new file in `module-settings.xml` (same
   folder part); a `Version` parameter that was set is changed to the web version. The computers
   get the new file at their next run.
5. The previous file is moved to the `OLD` subfolder of that folder (with a time stamp when `OLD`
   already holds a file of that name), so the last version stays at hand for a rollback.

Only the file the instance uses is moved; nothing else in the cache is touched, and nothing is
ever deleted from the cache. A failure before step 4 leaves the cache and the settings as they
were; when only the move to `OLD` fails, the new version is in place and a warning says so.

At the end, one line per product: *OK*, *Refused* (a check failed: SHA256, signature, zip...) or
*Failed* (network, disk...), with a short reason. When something was refused or failed, *Yes*
opens the details (full messages, the new `File` parameters, the log file) in a window whose text
can be copied; every step is also in the log file.

Permissions: the download needs **write** access to the folder of the installer in the cache,
which only the administrators who fill the cache have (see *Shared installer cache*); without it,
the download stops with a clear message and nothing changes. `module-settings.xml` is per
administrator: another administrator who uses the same instances must update its `File` too (for
example by importing the team's catalog again).

### Daily check

In the window: *Check web versions every day at HH:mm* runs the check once a day while LiteDeploy
is open (timer of the internal scheduler, whatever the engine of the scheduled updates; a check
missed while LiteDeploy was closed is not run later). It only reports (console of the main window,
results file and log), unless *Download new versions automatically* is also checked (off by
default). Saved in `%LOCALAPPDATA%\LiteDeploy\settings.xml` (`WebCheckTime`, `WebAutoDownload`).

## Setting up another administrator computer

Nothing of the software updates is shared between administrators by LiteDeploy itself: every
setting lives in the **Windows user profile** that runs it (`%LOCALAPPDATA%\LiteDeploy`). On each
computer, for each administrator account, once:

| What | Where | File |
|---|---|---|
| Shared installer cache (network path) | *App > Options > Software updates* | `settings.xml` (`UpdateCacheShare`) |
| Shared modules folder (network path) | *App > Options > Software updates* | `settings.xml` (`SharedModulesFolder`) |
| Credential of the software updates | *Tools > Credentials*, then *App > Options* | Windows Credential Manager, `settings.xml` (`UpdateCredential`) |
| Include servers, remote method, business hours | *App > Options > Software updates* | `settings.xml` |
| Software catalog | *Software catalog...* (tool bar of the main window) | `packages.txt` |
| Module parameters (instances) | *Software catalog... > Import instances...* (or *Parameters...*) | `module-settings.xml` |

1. Type or paste the network paths of the cache and of the shared modules folder (the boxes show
   the expected form; *Select* starts at the typed path, shares are under *Network*). *Save*
   checks that this computer can read them with this account and warns otherwise (*Save anyway*
   keeps them, for a share that is down for a moment).
2. Add the credential and choose it.
3. Open *Software catalog...*, *Import instances...*: the dialog opens the shared `catalogs` folder
   next to the shared modules folder (see *Catalog*). Import each catalog of the team; secret
   parameters are then set in *Parameters...*.
4. *Tools > Web versions...* lists the instances; *Check now*.

Keep the catalogs in the shared `catalogs` folder rather than in each profile: an update of a
product (new file, new version) is then imported again by every administrator from the same file.

## Writing a module

Start from [modules/Template-Module.ps1](modules/Template-Module.ps1) (it declares
`# Install: yes` and shows an Install branch: remove both if your module cannot install).

Header (first lines):

```powershell
# LiteDeploy-Module
# Label: Adobe Acrobat Reader
# Processes: Acrobat, AcroRd32, AcroCEF, RdrCEF
```

Contract (version 2; a module without `# Parameter:` line is a version 1 module and runs as before;
a module without `# Install: yes` line never receives `-Action Install`):

| | |
|---|---|
| Header | `# LiteDeploy-Module` (required), `# Label:`, `# Processes:`, `# Install: yes` (optional, see *Install action*), `# Parameter:` lines; all in the first 20 lines |
| Action | `-Action Check` or `-Action Update`; `-Action Install` only when the header declares `# Install: yes` (add `'Install'` to the `ValidateSet` of `-Action`) |
| Parameters | Environment variables `LITEDEPLOY_PARAM_<Name>`, one per parameter of the header (see *Module parameters*); an empty value sets no variable |
| Cache | `LITEDEPLOY_CACHE`: the shared installer cache, when one is set |
| Instance | `LITEDEPLOY_INSTANCE`: the instance name, for `module:<Name>/<Instance>` (modules with `# Instances: yes`, see *Module instances*) |
| Result | one line `LDW1<TAB>type=module<TAB>status=...` (function `Write-LiteDeployResult` of the template); `status`: `uptodate`, `outdated`, `notinstalled` or `error`; optional `installed`, `available`, `message` (URI-escaped) |
| Exit code | `0`; `3010` after an update or an install that needs a restart; anything else is a failure |
| Time limit | 15 minutes per action |

### Install action

By default LiteDeploy only updates: a computer where a package is `notinstalled` is never touched.
The *Install* action is explicit and opt-in:

- In the grid of the main window, select computers, right-click, *Install or update* and pick
  **one** package. The menu lists every software of the active profile: *update* where the last
  check says `outdated`, *install* only where it says `notinstalled` on at least one selected
  computer (paused computers excluded), and for a module only when its header has the
  line `# Install: yes` (key and value case-insensitive; any other value means no). A dialog
  names the package and the number of computers before anything starts.
- *Install* is never part of *Update checked* nor of the scheduled updates (a schedule runs `scan`
  or `upgrade` only; a schedule file edited by hand to another action is refused). Paused
  computers are skipped; "outside business hours only" computers are installed during business
  hours only on request, like updates.
- The worker (`LiteDeploy.Worker.exe install --package ...`) receives only the package chosen. It
  runs the check first and installs only where the check says `notinstalled`; an installed
  package (up to date or outdated) is reported as skipped (`Already installed`). Then it checks
  again, so the new state is shown at once.
- Module: same signature check, parameters (`LITEDEPLOY_PARAM_<Name>`), result line, exit codes
  (`0`, `3010`, other = failure) and 15-minute limit as `-Action Update`. The worker reads the
  header of the signed script again before `-Action Install`; a module without `# Install: yes`
  is refused with a failed result: `Install refused: the module <Name> does not declare
  "# Install: yes" in its header.` No process is closed before an install.
- winget package: `winget install --id <Id> -e --source winget --scope machine --silent
  --accept-package-agreements --accept-source-agreements --disable-interactivity`, with the same
  reading of the exit code as an upgrade (0: installed; nothing to apply: skipped; any other code:
  failed, with its meaning).

A module that installs must handle `-Action Install` where the product is missing and leave an
installed product alone (report its state and exit `0`); see the Install branch of the template.

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

The administrator sets the values in *Software catalog...* (tool bar of the main window): select a
`module:` row, then *Parameters...*. They are saved on the administrator's computer in
`%LOCALAPPDATA%\LiteDeploy\module-settings.xml` (not in `packages.txt`, whose format does not
change). A secret parameter holds the **name** of a credential of *Tools > Credentials*, never
the secret: the dialog offers the stored credentials in a list and never shows or asks for a
password; the password of that credential is read only when a run starts.

On each run, LiteDeploy writes `<Name>.params` (`<Name>@<Instance>.params` for an instance) next to `<Name>.ps1` in
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
