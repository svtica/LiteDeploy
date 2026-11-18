# Changelog

All notable changes to LiteDeploy are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.17] - 2026-11-18

### Changed

- *Software Updates* has a single entry: *Computers > Software Updates...* (Ctrl+M). The bold
  top-level item of the menu bar and the *Tools > Software Updates...* duplicate are removed; no
  menu item is bold any more.
- The computer list (*Computers > Edit List*) shows the operating system and the last logon of
  the Active Directory inventory in both lists, with clickable, sortable column headers (name,
  operating system, last logon); the tooltip adds the version and the OU.
- *App > Options > Software updates*: the shared installer cache and the shared modules folder are
  typed or pasted (the empty boxes show the expected form of a network path); the cache has a
  *Select* button too, and the folder dialog starts at the typed path. *Save* checks that each
  folder exists and can be read from this computer with the current account (a few seconds at
  most per folder) and warns, with *Save anyway*, instead of failing later.
- *Import from catalog...* opens the team's `catalogs` folder when one exists next to the shared
  modules folder (in its parent), else `%LOCALAPPDATA%\LiteDeploy` as before.
- The README and `docs/modules.md` describe what each administrator computer needs: the settings
  are per Windows user profile (cache, shared modules folder, credential, package list, module
  parameters), the catalogs are imported from the shared `catalogs` folder.

### Fixed

- The computer list and the main window's list showed servers and disabled computer accounts
  that *Software Updates* hides. They now use the same Active Directory inventory and the same
  rules: disabled accounts are never listed, servers only with **Include servers** (a check box
  in the computer list, the same setting as in *Software Updates* and *App > Options*, kept in
  sync between the windows). *Add all reachable* and *All >>* add only the computers shown; *Use
  List* applies the same filter to the main window's list and names the computers left out.
  Computers that are not in Active Directory (typed by hand, read from a text file, found by a DNS
  scan) have no known operating system and are kept.
- *Tools > Web versions...* looked empty on a computer where the current user had no cache set or
  no instance with parameters yet. Every instance with parameters is now listed, even before the
  first check (*Not checked*) or without a `WingetId` (*No WingetId*, with how to set it), and the
  window says what is missing with a button that fixes it: *Open Options...* (no cache) or *Import
  from catalog...* (no instance; the rows of the imported instances are added to the package
  list). The messages about a missing cache or missing instances say that these settings belong
  to the Windows user of this computer.

## [1.0.16] - 2026-11-18

### Added

- Server guard of the software updates: before `upgrade` and `install`, the worker reads
  `Win32_OperatingSystem.ProductType` (registry `ProductOptions` as fallback) and does nothing on a
  server (ProductType 3) unless the new option `--allow-servers` is given, nor on a domain
  controller (ProductType 2) or a computer whose type cannot be read unless
  `--allow-domain-controllers` is given too: one `error` line saying why and how to allow it, a
  `done` line, exit code 102. `scan`, `info` and `processes` are unchanged (report only).
- Setting *Include servers (updates and installs)* in *App > Options > Software updates*
  (`UserSettings.IncludeServers`, off by default): LiteDeploy then passes `--allow-servers`.
  `AllowDomainControllers` (no window, `settings.xml` only) adds `--allow-domain-controllers`,
  only together with it. Scheduled updates skip the computers already known as servers that are
  not included (logged); scheduled checks still run on them.
- Operating system type in the computer card (*Workstation*, *Server*, *Domain controller*). The
  `info` message and the `done` line of every run carry `os` (caption, version, release) and
  `ostype` (`workstation`, `server`, `domaincontroller`); `updates.xml` keeps them per computer
  (`OsCaption`, `OsType` attributes; files of earlier versions are read as before).
- *Software Updates* is a self-sufficient, non-modal window: it lists every enabled computer of
  Active Directory without any prior selection (filtered to the computers checked in the main
  window when there are some; the *Selection* filter can be removed), plus the computers of the
  main window's list and computers added by name (*Add...*, kept in `updates.xml`).
- Active Directory inventory in `adcache.xml` (backward compatible): operating system and version,
  last logon (`lastLogonTimestamp`), disabled account, OU. Servers (operating system containing
  "Server") are hidden unless *Include servers* is checked (`IncludeServers` in `settings.xml`);
  disabled accounts are never shown.
- New columns: OS (the one read on the computer, `OsCaption`/`OsType` in `updates.xml`, before
  Active Directory), last logon, *Pause* and *Outside business hours* with a tooltip giving the
  reason; every column sorts, columns can be resized and moved. Details of the selected computer
  under the list.
- Search box (Ctrl+F: name, OS, OU, package) and status filters (*To update*, *Errors*, *Up to
  date*, *Not checked*, *Paused*, *Outside business hours*, *Offline/stale*: last logon older
  than `UpdatesStaleDays`, 30 by default); status bar *shown / total / checked*.
- *Actions* menu in the tool bar, same as the right-click menu; actions apply to several
  computers (checked, else selected): pause / resume, outside business hours only (set or clear),
  install, computer card, Wake-on-LAN, get the logs, remove a computer added by name.
- Keyboard: F5 check, Ctrl+U update, Esc stop (confirmation), Ctrl+A select the rows shown.
- *Software Updates* in the menu bar of the main window and in the *Computers* menu.
- Window size and position, splitter, columns, view and filters kept in `settings.xml`.
- *Tools > Web versions...* (also *Package list > Web versions...*): for every instance of
  `CachedInstaller` and `MsixProvision`, the version of its file in the shared cache (MSI
  `ProductVersion`, EXE version, MSIX manifest version or the `Version` parameter) against the
  latest version known to winget (`winget show`, x64 installer of the type of the instance; output
  read by its layout, any display language). Statuses *Up to date*, *Newer on the web*, *Not
  found*, *No WingetId*, *Error*; *Check now*, *Open vendor page*; sortable columns, remembered
  size; results in `%LOCALAPPDATA%\LiteDeploy\web-versions.xml`, log in
  `logs\web-versions-<date>.log`. Runs on the administrator's computer only.
- *Download to the cache* (checked rows or current row): HTTPS-only download to a temporary
  folder, SHA256 of the winget manifest and Authenticode signature of the `Publisher` of the
  instance required (MSIX: package name and manifest publisher too), copy next to the current
  file under a versioned name (never over an existing file), `File` (and `Version` when set)
  updated in `module-settings.xml`, previous file moved to the `OLD` subfolder. Needs write
  access to the cache folder; clear message otherwise.
- Optional daily check at a set time while LiteDeploy is open, report only unless *Download new
  versions automatically* is on (off by default).
- Update modules `CachedInstaller` and `MsixProvision`: optional parameter `WingetId` (also
  accepted in a catalog), read by LiteDeploy only and ignored by the modules.

### Changed

- *Select all* checks the rows shown; checks are kept when the filters change; *Select outdated*
  checks exactly the rows shown that have updates.
- Updates: one confirmation, which names the "outside business hours only" computers left out
  during business hours (instead of a separate question).
- Credential offer: at most one question per session; the credential chosen becomes the default.
- Computer card: in a resizable window with *Copy* (several computers at once), opened by a
  double-click too.

## [1.0.15] - 2026-11-18

### Changed

- Update module `MsixProvision` declares `# Instances: yes`: several MSIX packages can be listed,
  one row per package (`module:MsixProvision/<Instance>`), each with its own parameters.
- README: the shipped generic modules and *Import from catalog...*, with an example catalog.

## [1.0.14] - 2026-11-17

### Added

- Explicit *Install* action for software not yet on a computer (*Tools > Software Updates*,
  right-click > *Install*): one package, chosen by the administrator, on the selected computers
  where the last check says `notinstalled`, after a confirmation naming the package and the number
  of computers. Not part of *Update checked* nor of the schedules (a schedule runs check or update
  only); paused and "outside business hours only" computers are handled as for updates. The new
  worker command `install` checks first, installs only where the package is not installed, then
  checks again. winget packages: `winget install --id <Id> -e --source winget --scope machine --silent
  --accept-package-agreements --accept-source-agreements --disable-interactivity`. Update modules
  (backward compatible): a module that declares `# Install: yes` in its header receives
  `-Action Install`, with the same result line, exit codes, time limit, signature check and
  parameters as `-Action Update`; a module without that line is refused with a clear error. The
  module template shows an Install branch. See `docs/modules.md`.
- Update module `CachedInstaller`: installs or updates any vendor MSI or EXE installer kept in the
  shared cache (`<cache>\CachedInstaller`), one instance per product. Each product is described by
  parameters (`File`, `Type`, `Arguments`, `DetectName` or `ProductCode`, `Version`, `Publisher`,
  `CloseProcesses`). Check compares the Uninstall entry (64 and 32-bit) with the version of the
  installer (MSI `ProductVersion`, EXE version, or `Version`); Update and Install copy the
  installer to `%windir%\LiteDeploy\work\CachedInstaller`, refuse it unless it has a valid
  Authenticode signature of the expected publisher (`O=<Publisher>`), run it silently and accept
  the exit codes 0, 3010 and 1641. The module declares `# Install: yes` and accepts
  `-Action Install`.
- Module instances: a module that declares `# Instances: yes` can be listed once per product as
  `module:<Name>/<Instance>`, with its own row, state and parameter values (key `<Name>/<Instance>`
  in `module-settings.xml`, parameter file `<Name>@<Instance>.params`, `LITEDEPLOY_INSTANCE` for
  the module process). The script is copied once per run; the worker refuses an instance of a
  module that does not declare it. `module:<Name>` Ids are unchanged. *Add module* asks for the
  instance name and *Parameters...* edits the values of the selected instance.
- *Package list > Import from catalog...*: creates the instances of a JSON catalog
  (`%LOCALAPPDATA%\LiteDeploy\catalog.local.json` by default) and their parameter values; secret
  parameters are never read from a catalog. See `docs/modules.md`.
- Shared update modules folder (*App > Options > Software updates*, usually a network
  share): the modules of an IT team, used by the LiteDeploy of every administrator. Modules are
  found in the shipped `modules\` folder, then the shared folder, then
  `%LOCALAPPDATA%\LiteDeploy\modules`; same name: the later wins. An unreachable shared folder is
  skipped (the package list editor says so). Security is unchanged: the worker runs a module only
  with a valid signature from the expected publisher; the folder should be writable by IT
  administrators only (docs/modules.md).
- Update module parameters (module contract version 2, backward compatible): a module declares
  `# Parameter: <Name> [secret] [default=<value>] <description>` lines in its header and reads
  `$env:LITEDEPLOY_PARAM_<Name>`, so one generic module can be configured (environment name,
  installer file name in the cache, enrollment token...). The values are set in
  *Package list > Parameters...* and saved in `%LOCALAPPDATA%\LiteDeploy\module-settings.xml`
  (`packages.txt` does not change). A secret parameter takes a stored credential
  (*Tools > Credentials*), chosen in a list; its password is never shown, never put on a command
  line and masked (`***`) in the module results. For each run, LiteDeploy writes a parameter file
  next to the module in `%windir%\LiteDeploy\modules` (SYSTEM and Administrators only); the worker
  deletes it at once, gives the declared values to the module process only, and reports `error`
  without starting the module when a required parameter has no value. See `docs/modules.md`.
- Update module `PrinterDrivers`: installs the printer drivers listed in the shared installer
  cache (`<cache>\PrinterDrivers\drivers.csv`, columns `Name,Inf`) on the computers that miss
  them. Each package is copied to `%windir%\LiteDeploy\work\PrinterDrivers`, refused unless its
  catalog has a valid Authenticode signature, staged with `pnputil /add-driver` and installed
  with `Add-PrinterDriver`. No printer is created and no process is closed.
- Update module `TeamsBootstrapper`: new Microsoft Teams provisioned for all users with
  Microsoft's `teamsbootstrapper.exe` (from `<cache>\Teams` or Microsoft's documented download
  link). Compares the provisioned `MSTeams` version with the offline package of the cache
  (`<cache>\Teams\MSTeams-x64.msix`) when there is one; the bootstrapper and the package are
  refused unless signed by Microsoft Corporation. Closes `ms-teams` before the update.
- Update module `MsixProvision`: provisions any MSIX package (and its dependency packages) for
  all users from the shared installer cache, configured with module parameters (`PackageName`,
  `Folder`, optional `File`, `Dependencies`, `License`, `Features`, `Publisher`). Check compares
  the provisioned version with the manifest version of the package of the cache (read without
  extracting it). Update and Install copy the files to
  `%windir%\LiteDeploy\work\MsixProvision\<PackageName>`, refuse every package unless validly
  signed by the publisher of its manifest (and by the expected organisation when `Publisher` is
  set), enable the requested Windows optional features (3010 when one needs a restart) and run
  `Add-AppxProvisionedPackage -Online -Regions all`. Declares `# Install: yes` and accepts
  `-Action Install`. No process is closed.
- Unit tests: every shipped module follows the contract (header, comment-based help,
  `Write-LiteDeployResult` of the template, `$ErrorActionPreference = 'Stop'`).
- `docs/modules.md`: layout of the shared installer cache and share permissions for the new
  modules, with an example `drivers.csv`.

### Changed

- *Get the logs*: the extra log folders are now a setting, `ExtraLogFolders` in
  `%LOCALAPPDATA%\LiteDeploy\settings.xml` (entries `label|folder under %windir%|newest files`,
  separated by `;`), empty by default. The logs of the modules and of winget are still taken.
- Generic wording in the documentation, the comments and the earlier entries of this file.

## [1.0.13] - 2026-11-17

### Added

- Update of LiteDeploy itself from its GitHub releases: *Help > Check for updates...* shows the
  current and new versions with the release notes and offers to install the new version.
  The package is downloaded (github.com release of svtica/LiteDeploy only), extracted and its
  signature checked (LiteSuite publisher, or the signer of the running LiteDeploy) before
  anything is replaced; LiteDeploy then closes and a script copies the new files and restarts
  it. Nothing is deleted, the files of the user next to `LiteDeploy.exe` are kept
  (`adcache.xml`, `subnets.txt`, `RemoteManagement.msc`, `*.local.*`... and the modules added
  to `modules\`), no Windows service is stopped (LiteDeploy has none), and Windows asks for administrator rights
  only when the installation folder is not writable. Log: `%TEMP%\LiteDeploy-update.log`.
  Deployments or scheduled updates in progress are confirmed before closing.
- *App > Options > Check for updates when the application starts* (on by default): the check
  runs in the background once the main window is shown; a new version is offered with the same
  dialog, nothing is shown when there is none or when GitHub cannot be reached.

### Changed

- *Tools > Manage Tools*: LitePM is downloaded, extracted and checked with LiteSuite.Common
  (`GitHubReleaseClient`, `UpdateDownloadPolicy`, `UpdatePackage`) instead of a copy of that
  code: only a release asset of svtica/LitePM is accepted, the size of the package is limited.
- NuGet package LiteSuite.Common 1.1.0 (self-update), in LiteDeploy and LiteDeploy.Worker.

## [1.0.12] and earlier

### Changed

- *Software Updates*: one row per computer, with its summary (updates available with their
  versions, errors, up to date) instead of one row per package and computer; *Show packages*
  gives the rows per package (packages that are not installed are left out). Computers can be
  checked: *Check* reads the checked computers (all of them when none is checked), *Update
  checked* updates the packages with an update of the checked computers. *Select all* (Ctrl+A)
  and *Select none*.
- *Software Updates*: before the first check or update of a session run as the current user,
  LiteDeploy offers to add a credential when none is stored (or to use the stored one), then to
  make it the default (*App > Options*), else it is used until LiteDeploy is closed.
- Computer list: the Active Directory search runs by itself: the domain computers are listed
  when the window opens and filtered as you type or change an option (*Search* is no longer
  needed).

### Fixed

- Software updates: a package that is not installed on a computer is no longer reported as an
  error (winget "no package found", or a success without the package in its output). A long
  package Id shortened by winget ("...") is still recognized.

- Messages of PsExec with garbled accents (French Windows): its error output is read in the OEM
  code page. An exit code 2 of PsExec (file not found after the copy, often an antivirus)
  now suggests the WinRM method.

- *App > Options* reset the settings it does not show (theme, unsigned modules) when saving.

- Dark mode: detail lists (process reports, software updates, computers) drawn by LiteDeploy:
  readable headers, dark grid lines and selection; dark tabs.
- French interface: texts of the computer list window shortened, they overlapped other controls.

- Console of the main window: it kept every message since start-up and
  rewrote the whole text at each new line, so memory and CPU grew with every
  deployment. It now keeps the last 500 messages.
- Deployments: standard output and standard error of PsExec are read at the
  same time; reading them one after the other could block a deployment when
  PsExec wrote a lot of output. "Stop All Processing" now stops the PsExec
  that is running.
- Active Directory: the search results, the directory entries and the ping
  used to check the domain controller are released after use (one directory
  entry per computer was bound and never released at each update).
- Ping (main window, process reports, **Computers > Ping**, computer list):
  every `Ping` object is released after use, and waiting for the ping threads
  no longer keeps a CPU core busy.
- Hidden windows: the timers of the ping window (every millisecond) and of the
  computer list window (every 500 ms) ran for the whole session, even when
  the windows were never shown. They now run only while the window is shown.
- MSI deployments: the Windows Installer objects are released once the
  product name and version are read (the `.msi` file stayed open), and the
  MSI log files are closed even when reading them fails.
- Dialogs shown with `ShowDialog` (MSI options, deployment preparation,
  working directory, options, DNS scan, splash screen) are disposed when
  closed; processes started for the remote session, the management console,
  the admin shares and the logs are released.
- Build from a clean clone: `PsExec.exe` and `RDPWInst.exe` (third-party,
  not in the repository) are only copied to the output when present, instead
  of failing the build.
- DNS scan: the host list is no longer filled by several threads at once
  without a lock.

### Added

- Computer list > *Subnets*: besides the subnets of this computer, saved subnets with a name
  (`10.1.2.0/24 Garage`; /20 to /22 split into /24). *Edit subnets...* edits the list of the
  user (`%LOCALAPPDATA%\LiteDeploy\subnets.txt`) and imports a text or CSV file; a
  `subnets.txt` next to `LiteDeploy.exe` gives the team's list.

- *Software updates*, right-click a computer > *Open in LitePM*: LitePM opens connected to that
  computer through WMI, as the current user (`LitePM.exe -DONOTCHECKPREVINSTANCE -REMOTE
  <computer>`, needs a LitePM with `-REMOTE`). Only plain computer names or addresses are passed.

- **Credentials** (*Tools > Credentials*): accounts stored in the Windows Credential Manager of
  the user (`LiteSuite.CredentialStore`, shared with LiteTask). The software updates, the
  scheduler, the computer card and the copies to `ADMIN$` use the account chosen in *Options*
  (PsExec `-u -p -s`: the worker still runs as SYSTEM).
- **Get the logs** (right-click): logs of the modules, newest winget logs of SYSTEM and logs of
  an earlier in-house update script, copied to `%LOCALAPPDATA%\LiteDeploy\logs\<computer>\<date>`.
- **Processes and handles** (right-click): processes of a computer, the most handles first,
  with private memory and threads (`LiteDeploy.Worker.exe processes`).
- **PowerShell module** `powershell\LiteDeploy` (docs/powershell.md): state, package list,
  worker, pause, Wake-on-LAN from scripts, for existing update scripts to live next to
  LiteDeploy. Signed with the release; CI imports it and checks it.

- **Scheduled updates** (*Tools > Scheduled Updates*, or *Schedule...* in Software Updates), from
  the agent of the previous update panel: a check or an update of a list of computers, once or on
  some days at a time, Wake-on-LAN first if asked. Paused computers are skipped, and during
  business hours the "outside business hours only" ones. Two engines, switched in the window:
  internal (while LiteDeploy is open; a run missed while it was closed is not caught up) or
  Windows scheduled tasks (folder `\LiteDeploy`, `LiteDeploy.exe /schedule <name>` as the
  logged-on user, no stored password). Log per day in `%LOCALAPPDATA%\LiteDeploy\logs`.
- **Computer card** (right-click in Software Updates): logged-on users, last restart, pending
  restart, Windows version, free space on C:, network adapters (`LiteDeploy.Worker.exe info`).
- **Wake-on-LAN** (right-click): magic packets to the MAC addresses the worker reported, kept in
  the state file while the computer is off.

- Software updates, from the previous update panel:
  - right-click a computer: **pause** it (with a reason; skipped by Check and Update until
    resumed) or mark it **outside business hours only** (during business hours, LiteDeploy asks
    before updating it);
  - **live display**: each row shows the step in progress (connecting, checking, closing the
    application, updating);
  - **shared installer cache** for the modules (Adobe, 1Password), signature checked on every
    install.
- *App > Options*: theme (automatic, light, dark), shared cache, business hours, unsigned
  modules (tests).

- Modules from an earlier in-house update script, shipped in `modules\`: `AdobeReader`
  (Reader and Acrobat MSP patches), `OnePassword` (per-machine MSI) and `OracleJava` (Java 8 with
  the system Java stopped and checked).

- **Update modules** (docs/modules.md): signed PowerShell scripts that update a product winget
  does not cover (inspired by the Adobe and 1Password modules of an earlier update script).
  LiteDeploy copies them to `%windir%\LiteDeploy\modules` on the computer (administrators
  only) and the worker runs them as SYSTEM only when their Authenticode signature is valid and
  from the LiteSuite publisher; *Add module* in the package list editor. Template:
  `docs/modules/Template-Module.ps1`.

- **Tools** menu: *Software Updates...*, *Manage Tools...* and *LitePM*.
- Software updates: the rows are checked to choose what to update (*Select outdated* checks
  every row with an update), and *Update checked* updates on each computer only its checked
  packages. Without checked computers, the window works on the whole list.
- Package list editor: search the winget catalog, add a result, set the processes to close
  and the label (instead of editing the file in Notepad).
- *Manage Tools*: version and location of PsExec and LitePM; download or update them
  (PsExec from live.sysinternals.com with a Microsoft signature; LitePM from its GitHub
  release with the LiteSuite signature policy of LiteTask), open LitePM. LitePM provided by
  LiteTask (its tools folder) is found too.
- Windows theme: light or dark mode of Windows and its accent color, dark title bar, menus and
  lists (setting `Theme` in `settings.xml`: auto, light or dark).

- PsExec missing at start-up: LiteDeploy offers to download `PsExec64.exe` from
  live.sysinternals.com instead of only showing an error. The file is kept only when its
  signature is valid, trusted by Windows and from Microsoft Corporation; it is saved next to
  `LiteDeploy.exe`, or in `%LOCALAPPDATA%\LiteDeploy\tools` when that folder is read-only.
- The Mark of the Web is removed from PsExec and `LiteDeploy.Worker.exe` at start-up (files
  copied from a browser download), so Windows does not block them.

- **Software updates** (*Computers > Software Updates...*), for the checked computers:
  - `LiteDeploy.Worker.exe`, copied and run as SYSTEM by PsExec (`-s -c -f`): no share, no
    scheduled task and no password stored on the computers. It checks the installed and
    available version of each winget package of the list, and on *Update* closes the
    package's processes and upgrades it. winget is found in the App Installer folder, as in
    an earlier update script, whose package list format it reuses.
  - Package list: `updates/packages.txt` (example shipped with LiteDeploy), copied to
    `%LOCALAPPDATA%\LiteDeploy\packages.txt` by *Package list...*.
  - State of each computer in `%LOCALAPPDATA%\LiteDeploy\updates.xml`: read when the window
    opens, written only when a version, a status, a result or an error changed.
  - Four computers at a time; *Stop* ends the running PsExec.

- `LiteSuite.Common.dll`, components shared with LiteTask (built for .NET Framework 4.8 and
  .NET 10, tested on both): PsExec command line, Authenticode verification and update signature
  policy, Mark of the Web removal, process failure messages, parameter parsing. Ported from
  LiteTask; LiteDeploy's PsExec quoting now comes from it. The DLL ships next to `LiteDeploy.exe`
  and is signed with it.

- Unit tests (MSTest, `tests/LiteDeploy.Tests`, run in CI): PsExec command
  line, localization, translation keys still in use.
- French user interface: English stays the default, French is used when
  Windows displays French or when chosen in *App > Options* (`lang/fr.xml`,
  same format as LiteTask; Quebec typography).
- Options window that saves its settings per user
  (`%LOCALAPPDATA%\LiteDeploy\settings.xml`): language, MSI logging folder,
  Active Directory refresh at start-up. It previously saved nothing.
- Memory soak test (`tools/soak/Measure-LiteDeploy.ps1` and the
  **Memory soak** workflow): builds the base branch and the pull request side
  by side and drives each one for 30 or 180 minutes.
- Build workflow on pushes and pull requests to `main`.
- Release action: `bump-version.ps1` (`-Version x.y.z.w` or
  `-Bump patch|minor|major`; clean tree, `main`, fast-forward pull, greater
  version and new tag required; commits "Bump version to X", tags
  `v<Major.Minor.Build>`, pushes, exposes `version` and `tag` to the calling
  workflow) and **Release** workflow, which runs it and then calls the build
  (`workflow_call` with the tag, `secrets: inherit`) to publish
  `LiteDeploy.zip`.
- `docs/dotnet-migration.md`: blockers, effort and recommendation for a
  migration to .NET 10.
- Release package `LiteDeploy.zip` built by CI: `LiteDeploy.exe`, its
  `.config` and `RemoteManagement.msc` at the zip root, with README, LICENSE
  and CHANGELOG. A CI step checks the LiteTask contract (asset name,
  `LiteDeploy.exe` and its dependencies at the root of the zip).
- Authenticode signing of `LiteDeploy.exe` in CI (`tools/ci/Sign-Binaries.ps1`,
  SHA-256, RFC 3161 timestamp, `signtool verify /pa`) with the
  `SIGNING_CERTIFICATE` and `CERTIFICATE_PASSWORD` secrets. Without them the
  package is unsigned (warning on tags) and the signing script is still
  tested with a throw-away self-signed certificate.

### Changed

- Main window: the computer list is on the left, the deployment and the console on the right.

- **PsExec is optional.** LiteDeploy starts without it; the software updates, the computer card,
  the processes and the schedules need it only with the PsExec method (WinRM by default). The
  standalone deployments of the main window (file or command through PsExec) are **hidden by
  default** (*App > Options > Show the standalone deployments*); PsExec is offered for download
  when they are used. The memory soak test shows them through its settings file.

- **LiteSuite.Common** moved to its own repository (svtica/LiteSuite.Common) and comes in as the
  NuGet package `LiteSuite.Common` 1.0.1 (GitHub Packages, `nuget.config` with package source
  mapping; token `LITESUITE_PACKAGES_TOKEN`). `LiteSuite.Common.dll` still ships next to
  `LiteDeploy.exe`; `LiteDeploy.Worker.exe` embeds it and loads it from the resource, so PsExec
  can still copy the worker alone (checked by the CI).

- **New LiteSuite publisher certificate**: the pinned thumbprint is now
  `D21B5D39EA4C7BDC69C94C86FC4602E8A1CA15B4` (update modules, LitePM download). Modules signed
  with the old certificate are refused.

- Software updates, computer card, processes: the worker is started by **WinRM and a
  one-time scheduled task as SYSTEM** by default, the method of an earlier update script
  (worker copied to `%windir%\LiteDeploy`, messages relayed from `%windir%\LiteDeploy\work`,
  task and file deleted at the end). PsExec stays available in *App > Options*.
- Main window: the credentials group is titled *Credentials (standalone deployments)*;
  *Saved...* fills the user name and password from a stored credential (*Tools > Credentials*).

- 64-bit only: one platform in the solution and the project (the x86 configurations are gone),
  build output in `bin\Release\` instead of `bin\x64\Release\`.

- PsExec command line built by `PsExecCommandBuilder` (ported from
  LiteTask): `-accepteula -nobanner`, user name and password quoted with the
  Windows rules (a password with spaces or quotes no longer breaks the
  command), password masked in the console.
- The computer list window keeps a stable title (with the Active Directory
  state appended) instead of alternating between two titles; the splash
  screen shows the real version.
- Lighter start-up: the About and Change Log windows are created when
  opened and disposed when closed, the Ping and computer list windows when
  first opened (then reused: a new Ping window per run leaked GDI objects); the status bar counters follow the computer list instead of a
  100 ms timer.
- Right-click actions on a computer (ping, remote desktop, management
  console, admin share, public folders) shared by the main window and the
  process reports; several admin shares can be opened at once; "Stop
  PsExec.exe" does the same as "Stop Current Process".
- Source files and classes renamed to a standard (`Form1` -> `MainForm`,
  `PCListDesigner` -> `ComputerListForm`, `ProcessConsole` ->
  `ProcessReportsForm`, `ActiveDir` -> `ActiveDirectoryCache`...), one
  `LiteDeploy` namespace, a standard header in every source file
  (`docs/conventions.md`). Window titles, control names and the
  `adcache.xml` format are unchanged.
- Project converted to the SDK format, still on .NET Framework 4.8 (explicit
  file lists, existing `AssemblyInfo.cs`, same output folders). Build with
  `msbuild LiteDeploy.sln -restore`. A migration to .NET 10 was evaluated and
  is not recommended for now (`docs/dotnet-migration.md`).
- PsExec is looked for next to `LiteDeploy.exe` (`PsExec.exe`, then
  `PsExec64.exe`), then in the parent folder: when LiteDeploy is installed
  by LiteTask (`LiteTaskData\tools\LiteDeploy\`), it uses the
  `PsExec64.exe` that LiteTask extracts to `LiteTaskData\tools\`. PsExec is
  still not shipped in the package (Sysinternals license). The working
  folder is now always the LiteDeploy folder, whatever folder LiteDeploy is
  started from.

### Removed

- Unused files: duplicate icons at the root (`iconSmall.ico`, `termIcon.ico`, `termIconSmall.ico`),
  14 icons of `Resources/` used nowhere, and `Resources/Scs.xml` (documentation of an unrelated library).

- RDP Wrapper install and remove (`RDPWInst.exe`: flagged as a hacking tool
  by antivirus software and against the Windows client license).
- Windows XP shortcuts of the right-click menus ("Windows 7" renamed
  "Public Folders").
- Dead code: self-update to `Easy Deploy.exe` / `edupdater.exe`, "Easy"
  presets (`DeploySettings`), Active Directory tree window, hidden and
  inactive "Deploy To" list, unused helpers.

- Build output (`bin/`, `obj/`) and Visual Studio user file from the
  repository, including a cached list of Active Directory computer names
  (`adcache.xml`). `.gitignore` now excludes them, along with `*.pfx` and
  other key files.
- Reference to a temporary ClickOnce signing key (`.pfx`, never committed)
  from the project file.
