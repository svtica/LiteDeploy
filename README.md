# LiteDeploy

[![License: Unlicense](https://img.shields.io/badge/license-Unlicense-green.svg)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/svtica/LiteDeploy)](https://github.com/svtica/LiteDeploy/releases/latest)
[![Release](https://github.com/svtica/LiteDeploy/actions/workflows/release.yml/badge.svg)](https://github.com/svtica/LiteDeploy/actions/workflows/release.yml)

**Lightweight deployment tool: runs a program, a command or an MSI package on many Windows computers at once through PsExec.**

LiteDeploy is a Windows Forms application for system administrators. Build a list of computers,
choose what to run and how, start the deployment, and follow each computer in the process reports.

## Features

### Computer list
- Add computers by name or IP address, or from an **Active Directory** search (cached locally,
  refreshed on demand or at start-up; the domain computers are listed when the window opens and
  filtered as you type), or a **DNS scan** of a /24 subnet.
- Same computers as *Software Updates*: the search uses the same Active Directory inventory,
  disabled computer accounts are never listed and servers only when **Include servers** is checked
  (the same setting as in *Software Updates* and *App > Options*; changing it in one window changes
  it everywhere). Both lists show the operating system and the last logon (columns sort on click;
  the tooltip adds the version and the OU). Computers that are not in Active Directory (typed by
  hand, read from a text file, found by a DNS scan) have no known operating system and are kept.
  *Use List* applies the same filter to the main window's list.
- *Subnets* menu: the subnets of this computer and saved subnets with a name. *Edit subnets...*
  takes one subnet per line (`10.1.2.0/24 Garage`, /20 to /22 split into /24, `#` for comments)
  and imports a text or CSV file. A `subnets.txt` next to `LiteDeploy.exe` gives the team's
  list; it is site data, kept out of this public repository.
- Save and open lists; check the computers to deploy to.
- **Ping** all or the checked computers before deploying.
- Right-click the list: ping, management console and administrative share of the selected
  computer; and, on the checked computers (else the selected one), the same actions as in
  *Software Updates*: remote desktop, computer card, TeamViewer ID, command prompt, quick commands,
  Wake-on-LAN, get the logs, pause / resume, outside business hours only, and *Export to Royal
  TS...* (see *Remote tools*).

### Standalone deployments (PsExec)
Hidden by default: *App > Options > Show the standalone deployments*. PsExec is needed only for
them (and for the PsExec method of the software updates); it is asked for when they are used.
- Run an executable with arguments, or a command, on every checked computer.
- **MSI packages**: product name and version read from the package, `msiexec` options and logging.
- PsExec options: copy the executable, overwrite, working directory, connection time-out,
  interactive session, run priority, don't wait for each process.
- Identity: the current user, other **credentials** (password masked in the console and logs),
  or the **SYSTEM** account.
- Ready-made commands: restart, shut down.
- Stop the current PsExec or all processing.

### Software updates
- **Software Updates** (*Computers > Software Updates...*, Ctrl+M) opens a window of its own,
  non-modal, that needs no prior selection: every enabled
  computer of **Active Directory** (from the local cache, refreshed in the background), the
  computers of the main window's list, and computers added by name (*Add...*, for computers that
  are not in Active Directory; kept in `updates.xml`). When computers are checked in the main
  window, it opens filtered to them (*Selection: N computers ×* removes the filter). Typical use:
  open it, *Select outdated*, *Update checked* (one confirmation).
- One row per computer: OS (the one read on the computer when known, else Active Directory), last
  logon (Active Directory), status, updates available, last result, last change, *Pause* and
  *Outside business hours* (tooltip: the reason, the business hours). Every column sorts; columns
  can be resized and moved. *Show packages*: one row per installed package. Under the list, the
  details of the selected computer.
- Filters, combined: search box (Ctrl+F; name, OS, OU or package), status buttons (*To update*,
  *Errors*, *Up to date*, *Not checked*, *Paused*, *Outside business hours*, *Offline/stale*: last
  logon older than 30 days by default, `UpdatesStaleDays` in `settings.xml`), and **Include
  servers** (servers are hidden by default; an operating system containing "Server" is a server).
  Disabled computer accounts are never shown. The status bar shows *shown / total / checked*.
- Checks are kept when the filters change. *Select all* (Ctrl+A) checks the rows shown, *Select
  none* unchecks everything, *Select outdated* checks exactly the rows shown that have updates.
  *Check* (F5) reads the checked computers (every computer shown when none is checked); *Update
  checked* (Ctrl+U) updates them; Esc stops (with a confirmation). Packages that are not
  installed are neither updates nor errors: they are installed only on explicit request
  (*Install*).
- During business hours, "outside business hours only" computers are left out of an update; the
  confirmation names them (to update one now, *Allow updates during business hours* first).
- No credential chosen: the first run of a session asks once to add one (or use the stored one);
  the credential chosen becomes the default (*App > Options*).
- Window size and position, splitter, columns, view, status filters and *Include servers* are kept
  in `%LOCALAPPDATA%\LiteDeploy\settings.xml`.
- `LiteDeploy.Worker.exe` runs as SYSTEM on the computer, started by one of two methods
  (*App > Options*), with nothing to install and no stored password:
  - **WinRM + SYSTEM task** (default): the worker is copied
    to `%windir%\LiteDeploy` (`ADMIN$`), a PowerShell session registers a one-time scheduled
    task as SYSTEM in `\LiteDeploy\`, relays the messages the worker writes in
    `%windir%\LiteDeploy\work`, then deletes the task and the file (leftovers of a stopped run
    are deleted at the next run). Needs WinRM on the computers.
  - **PsExec** (`-s -c -f`): the worker is copied and started by PsExec.
- Package list: *Package list...* searches the winget catalog
  (`%LOCALAPPDATA%\LiteDeploy\packages.txt`). State kept in
  `%LOCALAPPDATA%\LiteDeploy\updates.xml`.
- The account is the current user or a stored credential (*Tools > Credentials*), chosen in
  *App > Options*. The deployment credentials of the main window (*Saved...*) come from the
  same store.
- Actions (right-click, or the *Actions* menu of the tool bar) apply to the checked computers, else
  to the selected rows: *Install* one package where the last check says it is not installed
  (explicit, with a confirmation; modules only when they declare `# Install: yes`; never part of
  *Update checked* or of the schedules), pause / resume, outside business hours only (set or
  clear), computer card (Active Directory data, logged-on users, pending restart, TeamViewer
  ID...; in a window that can be copied; also on double-click), the remote tools below,
  Wake-on-LAN, get the logs. Each row shows the step in progress.

### Remote tools
Shared by *Software Updates* and the main window's list (checked computers, else the selected ones):
- **TeamViewer ID**: read on the computer by the worker (`ClientID`, else `ClientIDOfTSUser`, under
  `HKLM\SOFTWARE\TeamViewer` or its 32-bit `WOW6432Node` key), shown in the computer card, the
  details and the tooltip of *Software Updates*, and kept in `updates.xml`. *TeamViewer ID...*
  shows the saved IDs at once (read from the computers when none is known, or with *Read from the
  computers*), with *Copy* and, when TeamViewer is installed on the administrator's computer,
  *Connect* (`TeamViewer.exe -i <ID>`).
- **Remote desktop**: one window per computer; a confirmation above 5 windows.
- **Command prompt on the computer**: a local console window attached to the computer, by the
  method of *App > Options*: PsExec (`cmd.exe` as SYSTEM) or WinRM (`Enter-PSSession`, a
  PowerShell session as the account). With the credential of the software updates: for WinRM the
  password is handed over protected by DPAPI for the current Windows user (never in clear text);
  PsExec only accepts a password on its command line (readable by the same user and by local
  administrators), as for the standalone deployments.
- **Quick commands**, fixed (no free text), run as SYSTEM by the worker, output in a window that
  can be copied: refresh the group policies (`gpupdate /force`), the computer policies only
  (`gpupdate /target:computer /force`), flush the DNS cache, restart the print spooler, restart the
  computer in 5 minutes (users are warned) and cancel a planned restart. Restarting the spooler or
  the computer, or any command on several computers, asks first.
- **Export to Royal TS...**: Remote Desktop connections of the checked computers (else every
  computer shown), the operating system as description and the OU as folder path: a CSV file
  (`Name,ComputerName,Description,Path`, UTF-8 with BOM) for *Data > Import > Comma Separated
  File*, or a RoyalJSON document (`.rjson`, also usable as the content of a Dynamic Folder).
- **Servers are left alone by default**: the worker reads the operating system type
  (`Win32_OperatingSystem.ProductType`) and refuses to update or install anything on a server
  unless *App > Options > Software updates > Include servers (updates and installs)* is checked
  (worker option `--allow-servers`); the computer gets an error saying so. Domain controllers
  are refused even then, unless `AllowDomainControllers` is also set to `true` by hand in
  `%LOCALAPPDATA%\LiteDeploy\settings.xml` (worker option `--allow-domain-controllers`, no
  window on purpose). A computer whose type cannot be read is treated as a domain controller.
  *Check* still works on servers (report only) and records the Windows version and type of each
  computer in `updates.xml`; scheduled updates skip the computers already known as servers that
  are not included.
- *Tools > Scheduled Updates*: check or update on a schedule, with the internal engine (while
  LiteDeploy is open) or Windows scheduled tasks (`LiteDeploy.exe /schedule <name>`).
- **Update modules** for products winget does not cover: signed PowerShell scripts run as
  SYSTEM by the worker ([docs/modules.md](docs/modules.md)). Shipped generic modules take their
  files from the shared installer cache (*App > Options > Software updates*): `CachedInstaller`
  (vendor MSI or EXE installers), `MsixProvision` (MSIX packages), `PrinterDrivers`,
  `TeamsBootstrapper`. A team's own modules can live in a shared modules folder (same options).
- **Import from a catalog**: `CachedInstaller` and `MsixProvision` take one row per product
  (`module:<Name>/<Instance>`). Instead of adding each row by hand, describe the products in a
  JSON file and use *Package list... > Import from catalog...*: it adds the missing rows and sets
  their parameters. The dialog opens the team's `catalogs` folder when there is one next to the
  shared modules folder (modules in `...\LiteDeploy\modules`: catalogs in
  `...\LiteDeploy\catalogs`), else `%LOCALAPPDATA%\LiteDeploy` (`catalog.local.json`); any other
  file can be chosen, one module per file. Secret parameters are never read from a catalog.
  Example (fictitious values; full format in [docs/modules.md](docs/modules.md)):

  ```json
  {
    "module": "CachedInstaller",
    "products": [
      {
        "Instance": "ExampleApp",
        "Product": "Contoso Example App",
        "File": "ExampleVendor/ExampleApp-2.1.0-x64.msi",
        "Type": "msi",
        "Arguments": "/qn /norestart",
        "DetectName": "Contoso Example App*",
        "Publisher": "Contoso Ltd"
      }
    ]
  }
  ```

  Then *Check*: each product has its own state. *Install* (right-click) installs it where it is
  missing. Keep the catalog out of any repository if it names your own products or paths.
- **Each administrator computer** needs its own setup: the settings are kept per Windows user
  profile (`%LOCALAPPDATA%\LiteDeploy`), not shared. On every computer and for every
  administrator account that runs LiteDeploy, set once in *App > Options > Software updates* the
  shared installer cache and the shared modules folder (type or paste the network path; *Save*
  warns when this computer cannot read it), choose the credential (*Tools > Credentials*), then
  open *Package list...* and *Import from catalog...* the team's catalogs (from the shared
  `catalogs` folder): this writes the package list and the module parameters of that user. See
  [docs/modules.md](docs/modules.md#setting-up-another-administrator-computer).

### Process reports
- One line per computer and event: started, unreachable, authentication failure, exit code.
- Right-click on a computer: ping, remote desktop, management console, administrative share
  (`C$`, public desktop, public start menu, public folders).

### Tools
- *Tools > Credentials*: accounts in the Windows Credential Manager (shared with LiteTask), for
  the software updates (*App > Options*).
- *Tools > Web versions...*: for the products of the shared installer cache (instances of
  `CachedInstaller` and `MsixProvision` with a `WingetId` parameter), the version of the cache
  against the latest one known to winget; *Download to the cache* puts a newer version next to
  the current file once its SHA256 and its publisher signature are checked, updates the instance
  and moves the previous file to `OLD`. Optional daily check, report only unless the automatic
  download is on. Runs on the administrator's computer only; see
  [docs/modules.md](docs/modules.md#web-versions). Every instance with parameters is listed, even
  before the first check or without a `WingetId` (status *No WingetId*). On a computer where the
  cache or the instances are not set up yet for the current user, the window says so and offers
  *Open Options...* or *Import from catalog...*.
- Right-click a computer in Software Updates: processes and handles, get the logs.
  *Get the logs* copies the newest `*.log` files of the update modules (`%windir%\LiteDeploy\work`)
  and of winget (SYSTEM) to `%LOCALAPPDATA%\LiteDeploy\logs\<computer>\<date>`. Other folders
  are added in `%LOCALAPPDATA%\LiteDeploy\settings.xml` (no window for it), one entry
  `label|folder under %windir%|newest files` per folder, separated by `;`; empty by default:

  ```xml
  <ExtraLogFolders>myscript|Temp\MyScript|10</ExtraLogFolders>
  ```

  The files of `%windir%\Temp\MyScript` go to the `myscript` subfolder. The folder must stay
  under `%windir%` (no drive, no `..`); the number of files is 1 to 100 (10 when left out).
  Invalid entries are ignored.
- PowerShell module `powershell\LiteDeploy` for scripts ([docs/powershell.md](docs/powershell.md)).
- *Tools > Manage Tools*: download or update PsExec (Microsoft signature required) and LitePM
  (LiteSuite release); *Tools > LitePM* opens it.

### Interface
- Follows the Windows theme (light or dark mode, accent color).
- English by default, **French** offered (*App > Options*, or the Windows display language).
- Options saved per user in `%LOCALAPPDATA%\LiteDeploy\settings.xml`.
- *Help > Check for updates...*: installs the latest release of LiteDeploy (signature checked,
  files of the user kept, LiteDeploy restarted); also checked at start-up unless turned off in
  *App > Options*.

## Installation

- **From LiteTask** (recommended): *Tools* menu > LiteDeploy. LiteTask downloads
  the latest release, checks its Authenticode signature and installs it in
  `LiteTaskData\tools\LiteDeploy\`; LiteDeploy then uses the `PsExec64.exe`
  provided by LiteTask.
- **Manually**: download
  [LiteDeploy.zip](https://github.com/svtica/LiteDeploy/releases/latest/download/LiteDeploy.zip),
  extract it and run `LiteDeploy.exe`. PsExec ([Sysinternals](https://learn.microsoft.com/sysinternals/downloads/psexec))
  is not redistributed here: when it is missing, LiteDeploy offers to download `PsExec64.exe`
  from live.sysinternals.com and keeps it only if it is signed by Microsoft. You can also put
  `PsExec.exe` next to `LiteDeploy.exe` yourself.
- Administrative privileges on the target computers are required for most
  remote operations.

## Usage

1. **Computers > Edit List**: add the computers (manually, Active Directory, DNS scan), then
   **Use List**.
2. Choose the file to execute (or an MSI package) and its arguments, or type a command.
3. Set the PsExec options and the identity (current user, credentials or SYSTEM).
4. **Start Deploy**, then follow each computer in **Process Reports**.

## Technology Stack

- **Platform**: .NET Framework 4.8 (Windows Forms), SDK-style project
- **Language**: C#
- **Remote execution**: Sysinternals PsExec; Active Directory (LDAP), ICMP ping, DNS
- **Shared components**: NuGet package [LiteSuite.Common](https://github.com/svtica/LiteSuite.Common)
  (with LiteTask): `LiteSuite.Common.dll` ships next to `LiteDeploy.exe`, and is embedded in
  `LiteDeploy.Worker.exe`, which stays a single file for PsExec

## Development Status

Actively maintained. Next: native software updates (see [docs/roadmap.md](docs/roadmap.md)).
Conventions: [docs/conventions.md](docs/conventions.md).

## System Requirements

- Windows 10 or later
- .NET Framework 4.8
- PsExec (Sysinternals)
- Network access to target systems
- Appropriate permissions on remote systems

## Security Considerations

- PsExec receives the password on its command line (it has no other way): a process command
  line is readable by the same user and by local administrators. Prefer the current user or
  SYSTEM when possible.
- Credentials typed in LiteDeploy are not saved.
- The Active Directory cache (`adcache.xml`) and computer lists stay on the workstation: never
  commit them (this repository is public).

## Building and releasing

- Build: `msbuild LiteDeploy.sln -restore /p:Configuration=Release`
  (64-bit, output in `bin\Release\`). The package `LiteSuite.Common` comes from GitHub Packages
  (`nuget.config`): set the user environment variable `LITESUITE_PACKAGES_TOKEN` to a personal
  access token with `read:packages` only.
- Tests: `tests/LiteDeploy.Tests` (MSTest), run by the CI; LiteSuite.Common is tested in its own
  repository.
- Release: *Actions > Release > Run workflow* (or `bump-version.ps1` on `main`).
  The version is bumped, tagged `v<Major.Minor.Build>`, built, signed when the
  `SIGNING_CERTIFICATE` / `CERTIFICATE_PASSWORD` secrets exist, and published as
  `LiteDeploy.zip`. See [CHANGELOG.md](CHANGELOG.md).
- Memory soak test: label a pull request `soak` or `soak-quick`
  (see [tools/soak/README.md](tools/soak/README.md)).

## License

This software is released under [The Unlicense](LICENSE) - public domain.

