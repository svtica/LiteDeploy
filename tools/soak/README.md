# LiteDeploy soak test

`Measure-LiteDeploy.ps1` runs LiteDeploy for a long time under a realistic
load and records how its resource usage evolves, to catch leaks that only
show after hours of uptime.

What it does:

- copies the build output to a work folder and puts a PsExec stand-in next
  to `LiteDeploy.exe` (`PsExecStub.cs`, compiled on the fly with the .NET
  Framework `csc.exe`): no network, no remote service, but the same kind of
  output as PsExec (process started/exited, host not reachable, command
  output);
- starts LiteDeploy and drives its UI with UI Automation:
  - fills the computer list with 16 computers (`127.0.0.1`, `localhost`,
    `SOAK-PC01`..`SOAK-PC11`, and `SOAK-ERR01`..`SOAK-ERR03`, which the
    stand-in reports as unreachable);
  - every cycle (20 s): **Start Deploy** of `cmd /c echo` on every computer;
  - every 3rd cycle: **Computers > Ping**, wait for the end, close the window;
  - every 5th cycle: edit the computer list again (**Use List**, answer the
    confirmation, **Check All**);
  - answers any message box (Yes, else OK) and counts it;
- records how long the main window takes to appear, then samples every 30 s:
  Private Bytes, Working Set, kernel handles, GDI objects, USER objects,
  threads, CPU (% of one core) and the .NET GC heap;
- writes a CSV and a Markdown summary: value after the warm-up (short run),
  value at the end, and the linear growth per hour after the warm-up.

A healthy build has a growth per hour close to zero for every counter.

## Running it

In CI: add the `soak` label (180 min) or `soak-quick` label (30 min) to a pull
request, or run the **Memory soak** workflow manually. The workflow builds
`main` and the pull request side by side, on two `windows-latest` runners,
and shows both summaries.

Locally (Windows, PowerShell 5.1+, interactive desktop session):

```powershell
.\tools\soak\Measure-LiteDeploy.ps1 -ExePath .\bin\x64\Release\LiteDeploy.exe -DurationMinutes 240
```

The script never runs the real PsExec and never contacts the `SOAK-*`
computers other than through ping and name resolution.
