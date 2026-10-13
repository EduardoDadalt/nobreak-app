#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Remove o serviço NobreakMonitor, o painel e o atalho. Mantém %ProgramData%\NobreakMonitor
    (configuração e histórico), a menos que -RemoveData seja informado.
#>
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'NobreakMonitor'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'NobreakMonitor'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Serviço removido."
}

Get-Process -Name 'NobreakMonitor.App' -ErrorAction SilentlyContinue | Stop-Process -Force -PassThru | Wait-Process -Timeout 10
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NobreakMonitor' -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Nobreak Monitor.lnk') -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Removido $InstallDir"
}

if ($RemoveData) {
    Remove-Item (Join-Path $env:ProgramData 'NobreakMonitor') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Configuração e histórico removidos."
}
