# LiteDeploy roadmap

Ideas kept for later. Nothing here is planned for a given version.

## Next

- **Software updates** (in progress): `LiteDeploy.Worker.exe`, copied and run as SYSTEM by PsExec,
  updates the software of the checked computers (winget, signed Adobe and 1Password installers).
  The *Software updates* view shows each computer's state, kept in a local file
  (`%LOCALAPPDATA%\LiteDeploy`) that is rewritten only when a value changes.

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
