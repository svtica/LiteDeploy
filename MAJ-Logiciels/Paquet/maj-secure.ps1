# Security helpers shared by the launcher, the worker and the panel (dot-sourced; the panel also sends
# this file's text to the workstations through WinRM). ASCII only: read the same by every PowerShell.
#
# Initialize-SecureFolder: work folder for files that SYSTEM executes or installs.
#   C:\Windows\Temp (and C:\ProgramData) let any user create a folder in advance and stay its owner,
#   then swap a script or an installer between our copy and its run as SYSTEM. The work folder is
#   therefore created with a protected ACL (SYSTEM and Administrators only, no inheritance), and a
#   folder already there that is not exactly that is moved aside, never reused.
# Assert-Publisher: an installer is used only with a valid Authenticode signature from the expected
#   publisher (checked before publishing it on the share and again before running it).

$script:TrustedSids = @('S-1-5-18', 'S-1-5-32-544')   # SYSTEM, BUILTIN\Administrators

# $true when the folder is owned by SYSTEM or Administrators and only they have any right on it
function Test-TrustedFolder([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }   # junction / symbolic link
    $acl = Get-Acl -LiteralPath $Path
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($script:TrustedSids -notcontains $owner) { return $false }
    if (-not $acl.AreAccessRulesProtected) { return $false }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $script:TrustedSids -notcontains $rule.IdentityReference.Value) { return $false }
    }
    return $true
}

function New-ProtectedDirectorySecurity {
    $sec = New-Object System.Security.AccessControl.DirectorySecurity
    $sec.SetOwner([Security.Principal.SecurityIdentifier]'S-1-5-32-544')
    $sec.SetAccessRuleProtection($true, $false)
    foreach ($sid in $script:TrustedSids) {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            [Security.Principal.SecurityIdentifier]$sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
        $sec.AddAccessRule($rule)
    }
    return $sec
}

# Creates (or checks) <Root>\<Relative>: every level under Root (an existing system folder such as
# C:\ProgramData) is made a folder only SYSTEM and Administrators can touch. Returns the full path and
# the messages to log (folders moved aside). Throws when a level cannot be made safe.
function Initialize-SecureFolder([string]$Root, [string]$Relative) {
    $messages = @()
    $level = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    foreach ($part in @($Relative.Split('\') | Where-Object { $_ })) {
        $level = Join-Path $level $part
        if ((Test-Path -LiteralPath $level) -and -not (Test-TrustedFolder $level)) {
            # Renaming needs the right to delete children of the parent (Administrators), not rights on
            # the folder itself: an untrusted folder can always be moved aside
            $asideName = "$part.untrusted-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Rename-Item -LiteralPath $level -NewName $asideName -Force -ErrorAction Stop
            $messages += "untrusted folder moved aside: $level -> $asideName"
        }
        if (-not (Test-Path -LiteralPath $level)) {
            # Created with its ACL in one step: there is no moment when it inherits the parent's rights
            [void][IO.Directory]::CreateDirectory($level, (New-ProtectedDirectorySecurity))
        }
        if (-not (Test-TrustedFolder $level)) { throw "work folder $level could not be secured" }
    }
    return [pscustomobject]@{ Path = $level; Messages = @($messages) }
}

# Expected publishers (signer subject, regular expressions) of the installers of the optional modules,
# overridable in package.json: "Publishers": { "Adobe": [...], "OnePassword": [...] }
function Get-ExpectedPublishers($PackageConfig) {
    $r = @{ Adobe = @('O=Adobe Inc\.', 'O=Adobe Systems'); OnePassword = @('O=AgileBits Inc\.') }
    if ($PackageConfig -and $PackageConfig.Publishers) {
        if ($PackageConfig.Publishers.Adobe)       { $r.Adobe = @($PackageConfig.Publishers.Adobe) }
        if ($PackageConfig.Publishers.OnePassword) { $r.OnePassword = @($PackageConfig.Publishers.OnePassword) }
    }
    return $r
}

# Throws unless $Path has a valid Authenticode signature whose signer subject matches one of $Publishers
# (regular expressions, e.g. 'O=Adobe Inc\.'). Returns the signer subject.
function Assert-Publisher([string]$Path, [string[]]$Publishers) {
    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    $name = Split-Path $Path -Leaf
    if ($sig.Status -ne 'Valid') { throw "signature of $name is not valid ($($sig.Status): $($sig.StatusMessage))" }
    $subject = [string]$sig.SignerCertificate.Subject
    if (-not ($Publishers | Where-Object { $subject -match $_ })) { throw "unexpected publisher for $name : $subject" }
    return $subject
}
