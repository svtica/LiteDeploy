# LiteDeploy roadmap

Ideas kept for later. Nothing here is planned for a given version.

## Next

- **Software updates**, first version done (winget packages). Next steps:
  - Adobe Acrobat Reader patches and 1Password, installers checked with
    `LiteSuite.AuthenticodeVerifier` before they run (as the MAJ-Logiciels tool does);
  - a message to the logged-on user (`msg.exe`) before closing an application and after the
    update;
  - the network identity check of the MAJ-Logiciels panel (Kerberos) before connecting;
  - credentials other than the current user.

## Later

### Tools folder

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
