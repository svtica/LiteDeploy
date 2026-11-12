# LiteDeploy roadmap

Ideas kept for later. Nothing here is planned for a given version.

## Next

- **Software updates**, done: winget packages, selection of the rows to update, package list
  editor with winget search. Next steps:
  - update modules: done (docs/modules.md). Next: modules for Adobe Acrobat Reader and
    1Password, from the MAJ-Logiciels tool;
  - a message to the logged-on user (`msg.exe`) before closing an application and after the
    update;
  - the network identity check of the MAJ-Logiciels panel (Kerberos) before connecting;
  - credentials other than the current user.

## Features of the MAJ-Logiciels panel (Patrick Bédard)

Kept, in three steps:

- **A (done)**: pause or exclusion of a computer (with its reason), "outside business hours
  only" computers, live display of the step of each computer, shared installer cache.
- **B (done)**: scheduler (internal, while LiteDeploy is open, or Windows scheduled tasks that
  start `LiteDeploy.exe /schedule <name>`; switched in the window), Wake-on-LAN, computer card
  (logged-on users, pending restart, last restart).
- **C (done)**: credentials (Windows Credential Manager, LiteSuite.CredentialStore shared with
  LiteTask), retrieval of the logs of a computer, processes and handles of a computer, a
  `LiteDeploy` PowerShell module (docs/powershell.md).
- Next: LiteTask uses LiteSuite.CredentialStore and LiteSuite.Common; LitePM opens a remote
  computer from the command line (for a "Open in LitePM" of a computer).

## Later

### Tools folder (first step done: Tools > Manage Tools)

Same idea as LiteTask's tools folder (`LiteTaskData\tools`, catalog in `ToolCatalog`): a `tools\`
folder next to `LiteDeploy.exe` where optional third-party tools are dropped or downloaded, each
one described by a catalog entry (name, executable, how to call it, download source and
signature check). LiteDeploy shows the actions of the tools it finds and hides the others.

Possible integrations:

| Tool | Action on a computer |
|---|---|
| PsExec | already used; would move to the catalog |
| FreeRDP (also used by Royal TS) | open a remote desktop session |
| TeamViewer | start a remote control session |
| Royal TS | open the computer in an existing Royal TS document |

The catalog and download code of LiteTask (`ToolCatalog`, `ToolManager`) would be the starting
point, moved to `LiteSuite.Common` so both tools share it.

### Migration to .NET 10

See [dotnet-migration.md](dotnet-migration.md).
