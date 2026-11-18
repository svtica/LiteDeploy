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
- *Subnets* menu: the subnets of this computer and saved subnets with a name. *Edit subnets...*
  takes one subnet per line (`10.1.2.0/24 Garage`, /20 to /22 split into /24, `#` for comments)
  and imports a text or CSV file. A `subnets.txt` next to `LiteDeploy.exe` gives the team's
  list; it is site data, kept out of this public repository.
- Save and open lists; check the computers to deploy to.
- **Ping** all or the checked computers before deploying.

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
- *Tools > Software Updates...*: one row per computer with its updates available and its errors
  (*Show packages*: one row per installed package). Check the computers (*Select all*, Ctrl+A),
  then *Check* (all the computers when none is checked) and *Update checked*. Packages that are
  not installed are neither updates nor errors.
- No credential chosen: the first check of a session offers to add one (or use the stored one)
  and to make it the default.
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
- Right-click a computer: pause it, outside business hours only, computer card (logged-on
  users, pending restart...), Wake-on-LAN. Each row shows the step in progress.
- *Tools > Scheduled Updates*: check or update on a schedule, with the internal engine (while
  LiteDeploy is open) or Windows scheduled tasks (`LiteDeploy.exe /schedule <name>`).
- **Update modules** for products winget does not cover: signed PowerShell scripts run as
  SYSTEM by the worker ([docs/modules.md](docs/modules.md)).

### Process reports
- One line per computer and event: started, unreachable, authentication failure, exit code.
- Right-click on a computer: ping, remote desktop, management console, administrative share
  (`C$`, public desktop, public start menu, public folders).

### Tools
- *Tools > Credentials*: accounts in the Windows Credential Manager (shared with LiteTask), for
  the software updates (*App > Options*).
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

