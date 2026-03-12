[CmdletBinding()]
param(
    [ValidateSet('Register', 'Delete', 'Start', 'Stop', 'Restart')]
    [string]$Action = 'Register',
    [string]$ServiceName = 'LP2SVR',
    [string]$DisplayName = 'LP2SVR',
    [string]$Description = 'LP2 polling service.',
    [string]$ExecutablePath,
    [switch]$StartAfterInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ServiceExecutablePath {
    param(
        [string]$ProvidedPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ProvidedPath)) {
        return (Resolve-Path -LiteralPath $ProvidedPath).Path
    }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $candidatePaths = @(
        (Join-Path $repoRoot 'LP2SVR\bin\Release\net10.0-windows\LP2SVR.exe'),
        (Join-Path $repoRoot 'LP2SVR\bin\Debug\net10.0-windows\LP2SVR.exe'),
        (Join-Path $repoRoot 'LP2SVR\bin\x64\Release\net10.0-windows\LP2SVR.exe'),
        (Join-Path $repoRoot 'LP2SVR\bin\x64\Debug\net10.0-windows\LP2SVR.exe'),
        (Join-Path $repoRoot 'LP2SVR\bin\Release\net10.0-windows\publish\LP2SVR.exe'),
        (Join-Path $repoRoot 'LP2SVR\bin\Debug\net10.0-windows\publish\LP2SVR.exe'),
        (Join-Path $repoRoot 'LP2SVR\bin\x64\Release\net10.0-windows\publish\LP2SVR.exe'),
        (Join-Path $repoRoot 'LP2SVR\bin\x64\Debug\net10.0-windows\publish\LP2SVR.exe')
    )

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path -LiteralPath $candidatePath) {
            return (Resolve-Path -LiteralPath $candidatePath).Path
        }
    }

    throw 'LP2SVR.exe was not found. Specify -ExecutablePath explicitly.'
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & sc.exe @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed: $($Arguments -join ' ')"
    }
}

function Get-ExistingService {
    return Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
}

function Stop-ServiceIfRunning {
    param(
        [Parameter(Mandatory)]
        [System.ServiceProcess.ServiceController]$Service
    )

    if ($Service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        return
    }

    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    $Service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator.'
}

switch ($Action) {
    'Register' {
        $resolvedExecutablePath = Resolve-ServiceExecutablePath -ProvidedPath $ExecutablePath
        $escapedServiceName = $ServiceName.Replace('"', '\"')
        $binaryPath = '"{0}" --ServiceName "{1}"' -f $resolvedExecutablePath, $escapedServiceName
        $existingService = Get-ExistingService

        if ($null -eq $existingService) {
            Invoke-Sc -Arguments @('create', $ServiceName, 'binPath=', $binaryPath, 'start=', 'auto', 'DisplayName=', $DisplayName)
        }
        else {
            Stop-ServiceIfRunning -Service $existingService
            Invoke-Sc -Arguments @('config', $ServiceName, 'binPath=', $binaryPath, 'start=', 'auto', 'DisplayName=', $DisplayName)
        }

        Invoke-Sc -Arguments @('description', $ServiceName, $Description)
        Invoke-Sc -Arguments @('failure', $ServiceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/5000/restart/5000')
        Invoke-Sc -Arguments @('failureflag', $ServiceName, '1')

        Write-Host "Service '$ServiceName' registered with executable: $resolvedExecutablePath"

        if ($StartAfterInstall) {
            Start-Service -Name $ServiceName
            Write-Host "Service '$ServiceName' started."
        }
    }
    'Delete' {
        $existingService = Get-ExistingService
        if ($null -eq $existingService) {
            Write-Host "Service '$ServiceName' does not exist."
            break
        }

        Stop-ServiceIfRunning -Service $existingService
        Invoke-Sc -Arguments @('delete', $ServiceName)
        Write-Host "Service '$ServiceName' deleted."
    }
    'Start' {
        $existingService = Get-ExistingService
        if ($null -eq $existingService) {
            throw "Service '$ServiceName' does not exist."
        }

        Start-Service -Name $ServiceName
        Write-Host "Service '$ServiceName' started."
    }
    'Stop' {
        $existingService = Get-ExistingService
        if ($null -eq $existingService) {
            throw "Service '$ServiceName' does not exist."
        }

        Stop-ServiceIfRunning -Service $existingService
        Write-Host "Service '$ServiceName' stopped."
    }
    'Restart' {
        $existingService = Get-ExistingService
        if ($null -eq $existingService) {
            throw "Service '$ServiceName' does not exist."
        }

        Stop-ServiceIfRunning -Service $existingService
        Start-Service -Name $ServiceName
        Write-Host "Service '$ServiceName' restarted."
    }
}
