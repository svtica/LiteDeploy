# MAJ-Logiciels — third-party software updates for a Windows fleet

PowerShell tools that keep third-party software up to date on domain workstations through winget,
with a WinForms panel for the IT team and an unattended agent. User interface in French.

- Any winget package: the tracked list is a text file (`packages.txt`).
- Optional modules for software winget handles poorly in a managed fleet:
  **Adobe Reader / Acrobat** (MSP patches cached on the share) and **1Password** (per-machine MSI).
- Works with PDQ Deploy (the package is a plain `.cmd`) or on its own: the panel and the agent start
  the package on each workstation through a one-time scheduled task over WinRM.
- Everything specific to a site lives in a **site profile** (a folder of settings files), kept outside
  the code and re-applied after each update.

## Layout

| Path | Runs on | Content |
|---|---|---|
| `Installer.ps1` | admin VM | installs or updates the code, then imports a site profile |
| `Paquet/` | the share, `<DeployRoot>\Winget-upgrade` | `winget-upgrade.cmd` (entry point), `winget-upgrade.ps1` (launcher: module caches on the share, starts the worker), `winget-worker.ps1` (runs as SYSTEM), `maj-secure.ps1` (protected work folder, signature checks), default `packages.txt` and `package.json` |
| `Outils/` | admin VM, `<MajBase>\Outils` | `outils-maj.ps1` (panel), `agent.ps1` (scheduled agent), `check-updates.ps1`, `deploy-status.ps1`, `verify-winget-upgrade.ps1`, `watch-winget-upgrade.ps1`, `config.ps1` (settings loader, shared helpers), `profil.ps1` (profile export/import) |

## Site profile

A profile is a folder; every file is optional and falls back to a generic default.

| File | Installed to | Purpose |
|---|---|---|
| `config.json` | `<MajBase>\Outils` | `DeployRoot`, `MajBase`, `DeployAccount`, `OrgName`, `AdFilter` (LDAP filter of managed computers), `KeepReports`, `StaleDays`, `LaunchAs` (`System`, default, or `DeployAccount`) — see `Outils/config.example.json` |
| `package.json` | `<DeployRoot>\Winget-upgrade` | `Modules.AdobeReader`, `Modules.OnePassword` (default off), `TimeoutMinutes`, `Messages.Sender`, `Messages.JavaAppName`, `Messages.JavaWarning`, `Publishers.Adobe`, `Publishers.OnePassword` (expected signers, regular expressions on the certificate subject) |
| `packages.txt` | `<DeployRoot>\Winget-upgrade` | tracked winget packages: `Id \| processes to close \| label \| DisplayName regex` |
| `catalog.json` | `<DeployRoot>\Winget-upgrade` | software offered by "Installer un logiciel" (created by the panel when absent) |
| `exclude.txt`, `night-only.txt` | `<DeployRoot>\Winget-upgrade-logs` | computers never deployed / deployed outside business hours only |
| `agent-settings.json` | `<MajBase>` | agent schedules, business hours, queue |

```powershell
# first install, or update + re-apply the profile
.\Installer.ps1 -Profil D:\profils\mysite
# update the code only: settings on the share and in Outils are never overwritten
.\Installer.ps1
# save the current customizations as a profile / put a profile back
& C:\MAJ-Logiciels\Outils\profil.ps1 -Export D:\profils\mysite
& C:\MAJ-Logiciels\Outils\profil.ps1 -Import D:\profils\mysite [-Force] [-DryRun]
```

On import, the current files are saved first in `<MajBase>\Profil-backups\<date>` (itself an importable
profile). The live lists (`exclude.txt`, `night-only.txt`, `agent-settings.json`) are edited from the
panel: an existing one is kept unless `-Force`.

A profile holds site data (computer names, paths, account): keep it out of public repositories.

## Deployment share

```
<DeployRoot>\
  Winget-upgrade\        package + package.json, packages.txt, catalog.json
  Winget-upgrade-logs\   one log per computer, history\, exclude.txt, night-only.txt, last-check.json
  Adobe\, Adobe-x64\     Adobe module: MSP cache (filled by the launcher)
  1Password\             1Password module: MSI cache (filled by the launcher)
```

The launcher derives `DeployRoot` from its own location (parent of `Winget-upgrade`).

## First run

1. Create a profile folder from `Outils/config.example.json` (+ `package.json` / `packages.txt` if
   the defaults do not fit), then `.\Installer.ps1 -Profil <folder>`.
2. Start the panel: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <MajBase>\Outils\outils-maj.ps1`.
   The first "Déployer maintenant" asks for the deployment password, validates it against the domain
   once, and stores it with DPAPI (`%LOCALAPPDATA%\MAJ-Logiciels\state.bin`, readable only by that user
   on that machine).
3. The agent settings window ("Paramètres de l'agent") creates the agent's scheduled task
   (`MAJ-Logiciels-Agent`). The agent starts in **DryRun** mode: it only logs what it would do.

Requirements: Windows PowerShell 5.1, WinRM and SMB (C$) access to the workstations, read access to AD
(LDAP, no RSAT needed), winget on the workstations. Run the installer and the tools as the deployment account.

## Status rules

A computer is **DONE** when its last successful run is newer than the newest known version (winget
versions from `last-check.json`, newest installers of the enabled modules), or when the panel read its
tracked software as up to date after that date. Otherwise **TODO**, **FAILED**, **INCOMPLETE** or **NEVER**.

## Security

- **Work folder on the workstations**: `C:\ProgramData\MAJ-Logiciels\winget-upgrade` (package) and
  `...\install` (panel installs), created with a protected ACL: SYSTEM and Administrators only. A folder
  already there with other rights (created in advance by a user, junction) is moved aside, never reused:
  any user can create a folder in `C:\Windows\Temp` or `C:\ProgramData` first and swap the files SYSTEM
  runs. Older versions used `C:\Windows\Temp\winget-upgrade`; the tools still read logs from there.
- **Installers of the modules**: the Adobe patches and the 1Password MSI are published on the share only
  with a valid Authenticode signature from the expected publisher (`O=Adobe Inc.`, `O=AgileBits Inc.` by
  default, `Publishers` in `package.json`), and checked again on the workstation before `msiexec`.
- **"Installer un logiciel"**: the script run as SYSTEM is fixed; the catalog entries are passed as data
  (`install.json`). An MSI entry of `catalog.json` can name its expected `Publisher`.
- **Who starts the package** (`LaunchAs` in `config.json`):
  - `System` (default): one-time task as SYSTEM. No password is sent to or stored on the workstations; the
    computer account reads the share. Rights for `Domain Computers` on `<DeployRoot>`:
    read on `Winget-upgrade`, modify on `Winget-upgrade-logs`, `Adobe`, `Adobe-x64` and `1Password`
    (caches of the enabled modules). Keep `exclude.txt`, `night-only.txt` and `last-check.json` read-only
    for them (file ACL). Every installer from the caches is signature-checked before it runs.
  - `DeployAccount`: one-time task as the deployment account, as before. Windows keeps that password in
    the LSA secrets of every workstation where the task was registered.
  The panel and the agent still run on the VM as the deployment account (WinRM to the workstations); the
  agent's own task stores its password on that VM only.
- **The share is trusted code**: whoever can write to `Winget-upgrade` (scripts, `packages.txt`,
  `catalog.json`) runs code as SYSTEM on every workstation. Give write access to the IT team only.

## Updating an existing installation

The generic version reads its site settings from files instead of the code. Before the first update of a
site that ran an older version (share path written in the launcher, modules always on), create its profile
(`config.json` with `DeployRoot`; `package.json` with the modules it uses) and update with
`Installer.ps1 -Profil <folder>`: without `package.json`, Adobe and 1Password are off. The panel and the
worker must come from the same version (`packages.txt` columns).

## Tests

`tests/MAJ-Logiciels.Tests.ps1` (Pester 5, run by the LiteDeploy CI under Windows PowerShell 5.1): parsing
and encoding of every script, installer and profile export/import on a fake share, settings and detection
rules, protected work folder, signature checks, launcher on a server (does nothing).

## License

Unlicense, like the rest of LiteDeploy. Written by Patrick Bédard; shipped with LiteDeploy in the
`MAJ-Logiciels` folder next to `LiteDeploy.exe` (menu *App > Software updates*).
