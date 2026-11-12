# Changelog

All notable changes to LiteDeploy are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

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
    the MAJ-Logiciels tool of Patrick Bédard, whose package list format it reuses.
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
