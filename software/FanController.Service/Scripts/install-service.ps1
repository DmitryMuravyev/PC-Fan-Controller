#Requires -RunAsAdministrator

param(
    [string]$ServiceName     = "FanControllerService",
    [string]$DisplayName     = "Fan Controller Service",
    [string]$Description     = "Manages Fan Controller USB device.",
    [string]$BinaryName      = "FanController.Service.exe",
    # By default, looks for the binary next to this script
    [string]$InstallDir      = "$env:ProgramFiles\FanControllerService",
    # PawnIO driver (CPU / motherboard sensors of LibreHardwareMonitor)
    [switch]$SkipPawnIO,
    [string]$PawnIOUrl       = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe"
)

$ErrorActionPreference = "Stop"

# ---- Helpers ----------------------------------------------------------------

function Write-Step  { param([string]$msg) Write-Host "[*] $msg" -ForegroundColor Cyan    }
function Write-Ok    { param([string]$msg) Write-Host "[+] $msg" -ForegroundColor Green   }
function Write-Err   { param([string]$msg) Write-Host "[!] $msg" -ForegroundColor Red     }

# ---- Locate binary ----------------------------------------------------------

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$BinaryPath = Join-Path $PSScriptRoot $BinaryName

if (-not (Test-Path $BinaryPath)) {
    Write-Err "Binary not found: $BinaryPath"
    Write-Err "Place '$BinaryName' next to this script and try again."
    exit 1
}

# ---- Stop + remove old installation (idempotent) ----------------------------

Write-Step "Checking for existing service..."
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Stopping existing service..."
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $existing.WaitForStatus("Stopped", (New-TimeSpan -Seconds 30))
    }
    Write-Step "Removing existing service..."
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2   # give SCM time to remove it
    Write-Ok "Old service removed."
}

# ---- Copy files -------------------------------------------------------------

Write-Step "Creating install directory: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Write-Step "Copying binary..."
Copy-Item -Path $BinaryPath -Destination $InstallDir -Force

# Copy every file that lives next to this script (configs, DLLs, etc.),
# excluding the install (uninstall) script(s) themselves
Write-Step "Copying supporting files..."
Get-ChildItem -Path $PSScriptRoot -File | Where-Object {
    $_.Name -notin @(
        "install-service.ps1",
        "PawnIO_setup.exe"
        #, "uninstall-service.ps1"
    )
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $InstallDir -Force
}

$InstalledBinary = Join-Path $InstallDir $BinaryName

# ---- Lock down permissions --------------------------------------------------
# Only SYSTEM + Administrators can read/execute; no regular users.

Write-Step "Setting directory permissions..."

# Well-known SIDs — language-independent, no LSA lookup needed
# S-1-5-18  = NT AUTHORITY\SYSTEM   (universal, never changes)
# S-1-5-32-544 = BUILTIN\Administrators (universal, never changes)
$SystemSid = [System.Security.Principal.SecurityIdentifier]::new(
    [System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
$AdminSid  = [System.Security.Principal.SecurityIdentifier]::new(
    [System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)

$Rights       = [System.Security.AccessControl.FileSystemRights]::FullControl
$Inheritance  = [System.Security.AccessControl.InheritanceFlags]"ContainerInherit, ObjectInherit"
$Propagation  = [System.Security.AccessControl.PropagationFlags]::None
$AccessType   = [System.Security.AccessControl.AccessControlType]::Allow

$Rules = @(
    [System.Security.AccessControl.FileSystemAccessRule]::new(
        $SystemSid, $Rights, $Inheritance, $Propagation, $AccessType),
    [System.Security.AccessControl.FileSystemAccessRule]::new(
        $AdminSid,  $Rights, $Inheritance, $Propagation, $AccessType)
)

try
{
    $Acl = Get-Acl -Path $InstallDir
    $Acl.SetAccessRuleProtection($true, $false)   # disable inheritance, remove inherited rules
    foreach ($Rule in $Rules) { $Acl.AddAccessRule($Rule) }
    Set-Acl -Path $InstallDir -AclObject $Acl
    Write-Ok "Permissions set via PowerShell ACL."
}
catch
{
    # Fallback to icacls.exe if PowerShell ACL still fails for any reason
    Write-Step "PowerShell ACL failed ($($_.Exception.Message)), falling back to icacls.exe..."
    $Result = icacls.exe $InstallDir `
        /inheritance:r `
        /grant "*S-1-5-18:(OI)(CI)F" `
        /grant "*S-1-5-32-544:(OI)(CI)F"

    if ($LASTEXITCODE -ne 0)
    {
        Write-Err "icacls.exe also failed (exit code $LASTEXITCODE)."
        Write-Err "Output: $Result"
        exit 1
    }
    Write-Ok "Permissions set via icacls.exe fallback."
}

# ---- PawnIO driver ----------------------------------------------------------
# LibreHardwareMonitor reads CPU, chipset and Super I/O sensors through the PawnIO
# driver. It is detected the way LibreHardwareMonitor does it: by its uninstall entry.

function Get-PawnIOVersion {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $key = $base.OpenSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO")
            if ($key) {
                $version = $key.GetValue("DisplayVersion")
                $key.Close()
                if ($version) { return [string]$version }
            }
        }
        finally { $base.Close() }
    }
    return $null
}

function Install-PawnIO {
    $pawnIOVersion = Get-PawnIOVersion
    if ($pawnIOVersion) {
        Write-Ok "PawnIO driver found (version $pawnIOVersion)."
        return
    }

    # An installer placed next to this script is used instead of downloading
    $setupPath = Join-Path $PSScriptRoot "PawnIO_setup.exe"
    $downloaded = $false

    if (-not (Test-Path $setupPath)) {
        $setupPath = Join-Path $env:TEMP "PawnIO_setup.exe"
        Write-Step "Downloading the PawnIO installer: $PawnIOUrl"
        [Net.ServicePointManager]::SecurityProtocol =
            [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $PawnIOUrl -OutFile $setupPath -UseBasicParsing
        $downloaded = $true
    }

    try {
        # Only a validly signed installer is run
        $signature = Get-AuthenticodeSignature -FilePath $setupPath
        if ($signature.Status -ne "Valid") {
            throw "the installer signature is not valid ($($signature.Status))."
        }
        Write-Step "PawnIO installer signed by: $($signature.SignerCertificate.Subject)"

        Write-Step "Installing the PawnIO driver..."
        $process = Start-Process -FilePath $setupPath -ArgumentList "-install", "-silent" -Wait -PassThru

        switch ($process.ExitCode) {
            0       { Write-Ok "PawnIO driver installed." }
            3010    { Write-Ok "PawnIO driver installed. Restart the computer to make all host sensors available." }
            default { throw "the installer failed (exit code $($process.ExitCode))." }
        }
    }
    finally {
        if ($downloaded) { Remove-Item $setupPath -Force -ErrorAction SilentlyContinue }
    }
}

if ($SkipPawnIO) {
    Write-Step "PawnIO check skipped."
}
else {
    try {
        Install-PawnIO
    }
    catch {
        # The service works without it, with fewer host sensors
        Write-Err "PawnIO: $($_.Exception.Message)"
        Write-Err "The service is installed anyway; CPU / motherboard sensors may be unavailable."
    }
}

# ---- Register service -------------------------------------------------------

Write-Step "Registering service '$ServiceName'..."
New-Service `
    -Name        $ServiceName `
    -DisplayName $DisplayName `
    -Description $Description `
    -BinaryPathName $InstalledBinary `
    -StartupType Automatic `
    | Out-Null

# Run as LocalSystem (SYSTEM account) – default for New-Service,
# but we set it explicitly for clarity.
sc.exe config $ServiceName obj= "LocalSystem" | Out-Null

# Optional: restart the service automatically on failure
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-Ok "Service registered."

# ---- Start service ----------------------------------------------------------

Write-Step "Starting service..."
Start-Service -Name $ServiceName
$svc = Get-Service -Name $ServiceName
$svc.WaitForStatus("Running", (New-TimeSpan -Seconds 30))

Write-Ok "Service '$ServiceName' is running."
Write-Ok "Install directory : $InstallDir"
Write-Ok "Done."
