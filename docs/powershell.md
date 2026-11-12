# LiteDeploy PowerShell module

`LiteDeploy.zip` ships a PowerShell module in `powershell\LiteDeploy`, signed with the release.
It lets scripts, such as those of the MAJ-Logiciels tool, live next to LiteDeploy: they read and
change the same files (`%LOCALAPPDATA%\LiteDeploy`) and run the same worker.

```powershell
Import-Module C:\Tools\LiteDeploy\powershell\LiteDeploy
```

| Function | What it does |
|---|---|
| `Get-LiteDeployState [-ComputerName PC*]` | State per computer and package (`updates.xml`): status, versions, last result, pause |
| `Get-LiteDeployPackage` | Package list (`packages.txt` of the user, else the shipped one) |
| `Invoke-LiteDeployWorker -ComputerName PC01 -Command scan\|upgrade\|info\|processes [-Package ...] [-Credential ...]` | `LiteDeploy.Worker.exe` on the computers through PsExec, as SYSTEM; the messages as objects |
| `Set-LiteDeployPause -ComputerName PC01 [-Reason ...] [-Resume]` | Pause or resume computers |
| `Send-LiteDeployWakeOnLan -ComputerName PC01` | Wake-on-LAN with the MAC addresses LiteDeploy keeps |
| `ConvertFrom-LiteDeployMessage` | `LDW1` lines of the worker to objects |

Examples:

```powershell
# Computers with an update available
Get-LiteDeployState | Where-Object Status -eq outdated | Format-Table ComputerName, Package, Installed, Available

# Check two computers now, with a stored account
Invoke-LiteDeployWorker -ComputerName PC01, PC02 -Command scan -Credential (Get-Credential) |
    Where-Object Type -eq package

# Computer card
Invoke-LiteDeployWorker -ComputerName PC01 -Command info
```

`Invoke-LiteDeployWorker` does not change `updates.xml`: run a check in LiteDeploy to see the
result there. `Set-LiteDeployPause` writes `updates.xml`: close the Software Updates window of
LiteDeploy first, it keeps its own copy while it is open.
