@{
    RootModule        = 'LiteDeploy.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = '5d2f6c1e-7f43-4a7b-9b0e-3a1d8c2e6f41'
    Author            = 'LiteDeploy contributors'
    Description       = 'LiteDeploy from PowerShell: state of the software updates, package list, worker on the computers (check, update, computer card, processes), pause, Wake-on-LAN. For scripts that live next to LiteDeploy (MAJ-Logiciels tool).'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-LiteDeployState', 'Get-LiteDeployPackage', 'Invoke-LiteDeployWorker',
        'Set-LiteDeployPause', 'Send-LiteDeployWakeOnLan', 'ConvertFrom-LiteDeployMessage'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
