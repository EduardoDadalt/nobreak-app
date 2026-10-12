#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publica e instala o serviço NobreakMonitor (início automático com o Windows) e o painel.

.DESCRIPTION
    - Publica serviço e painel em "$env:ProgramFiles\NobreakMonitor".
    - Registra o serviço com inicialização Automática (sobe no boot, antes do login),
      rodando como LocalSystem.
    - Configura recuperação: reinicia o serviço se ele parar por falha.
    - Cria atalho do painel no Menu Iniciar.
    - Não altera %ProgramData%\NobreakMonitor\config.json se ele já existir.
      Se não existir, cria um com o MODO SIMULAÇÃO ligado (não desliga o PC de verdade)
      até você desativar a simulação no painel.

.EXAMPLE
    # PowerShell como Administrador, na raiz do repositório:
    powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1
#>
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'NobreakMonitor')
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'NobreakMonitor'
$Root = Split-Path -Parent $PSScriptRoot
$ServiceDir = Join-Path $InstallDir 'Service'
$AppDir = Join-Path $InstallDir 'App'
$ServiceExe = Join-Path $ServiceDir 'NobreakMonitor.Service.exe'
$AppExe = Join-Path $AppDir 'NobreakMonitor.App.exe'

# 1. Para o serviço (se já instalado) para liberar os arquivos.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Parando o serviço existente..."
    Stop-Service -Name $ServiceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

# O painel aberto também trava os arquivos.
Get-Process -Name 'NobreakMonitor.App' -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Publica.
Write-Host "Publicando em $InstallDir ..."
dotnet publish (Join-Path $Root 'src\NobreakMonitor.Service') -c Release -o $ServiceDir --nologo
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o serviço." }
dotnet publish (Join-Path $Root 'src\NobreakMonitor.App') -c Release -o $AppDir --nologo
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o painel." }

# 3. Configuração inicial segura (só se ainda não existir).
$dataDir = Join-Path $env:ProgramData 'NobreakMonitor'
$configPath = Join-Path $dataDir 'config.json'
if (-not (Test-Path $configPath)) {
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    Set-Content -Path $configPath -Encoding UTF8 -Value "{`n  `"simulateShutdown`": true`n}"
    Write-Host "Criado $configPath com o modo simulação LIGADO."
}

# 4. Registra (ou atualiza) o serviço com início automático.
if (-not $existing) {
    New-Service -Name $ServiceName `
        -BinaryPathName "`"$ServiceExe`"" `
        -DisplayName 'Nobreak Monitor' `
        -Description 'Monitora o nobreak (protocolo Megatec/TS Shara) e desliga o computador em falta de energia.' `
        -StartupType Automatic | Out-Null
} else {
    sc.exe config $ServiceName binPath= "`"$ServiceExe`"" start= auto | Out-Null
}

# 5. Recuperação: reinicia após 5 s, 5 s e 30 s; zera o contador após 1 dia.
#    failureflag 1 = também reinicia quando o processo termina com código de erro.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

# 6. Inicia.
Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

# 7. Atalho do painel no Menu Iniciar (para todos os usuários).
$shortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Nobreak Monitor.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $AppExe
$shortcut.WorkingDirectory = $AppDir
$shortcut.Save()

$svc = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
Write-Host ""
Write-Host "Serviço '$ServiceName': $($svc.State), início $($svc.StartMode), conta $($svc.StartName)"
Write-Host "Painel: $AppExe (atalho 'Nobreak Monitor' no Menu Iniciar)"
Write-Host "Dados: $dataDir"
