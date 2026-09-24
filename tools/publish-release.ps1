<#
.SYNOPSIS
    Публикует новую версию Skelter Arena: пакует сборку, заливает релиз на GitHub
    и обновляет version.json, по которому лаунчер видит обновление.

.EXAMPLE
    .\tools\publish-release.ps1 -Version 1.0.1 -BuildDir "C:\Users\Dan\Desktop\Skelter" -Notes "Починил баланс"

.EXAMPLE
    # Из сборки выпилены старые файлы — пусть лаунчер очистит папку перед распаковкой
    .\tools\publish-release.ps1 -Version 2.0.0 -BuildDir "C:\Builds\Skelter" -CleanInstall
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$BuildDir,

    [string]$Notes = "",

    # Лаунчер снесёт содержимое папки игры перед распаковкой.
    [switch]$CleanInstall,

    # Не прикладывать лаунчер к релизу. По умолчанию он собирается и заливается всегда,
    # чтобы ссылка /releases/latest/download/SkelterLauncher.exe никогда не отдавала 404.
    [switch]$NoLauncher,

    # Спаковать и посчитать хеш, но ничего не заливать.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DistDir = Join-Path $RepoRoot 'dist'
$ZipName = "Skelter-Arena-$Version.zip"
$ZipPath = Join-Path $DistDir $ZipName
$Tag = "v$Version"
$Exe = 'Skelter Arena.exe'

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host ">> $Message" -ForegroundColor Cyan
}

function Resolve-Tool([string]$Name, [string[]]$Candidates) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $cmd) { return $cmd.Source }

    foreach ($path in $Candidates) {
        if (Test-Path $path) { return $path }
    }

    return $null
}

# ---------------------------------------------------------------- проверки

if (-not (Test-Path $BuildDir)) {
    throw "Папка сборки не найдена: $BuildDir"
}

$BuildDir = (Resolve-Path $BuildDir).Path

if (-not (Test-Path (Join-Path $BuildDir $Exe))) {
    throw "В $BuildDir нет '$Exe'. Укажи папку, в которой лежит сам .exe игры."
}

$gh = Resolve-Tool 'gh' @("C:\Program Files\GitHub CLI\gh.exe")
if (-not $DryRun -and $null -eq $gh) {
    throw "Не найден GitHub CLI. Установи: winget install GitHub.cli, затем gh auth login"
}

$sevenZip = Resolve-Tool '7z' @("C:\Program Files\7-Zip\7z.exe", "C:\Program Files (x86)\7-Zip\7z.exe")

# ---------------------------------------------------------------- упаковка

Write-Step "Пакуем $BuildDir -> $ZipName"

New-Item -ItemType Directory -Force $DistDir | Out-Null
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

if ($null -ne $sevenZip) {
    # -xr! выкидывает отладочную информацию Burst, которую Unity помечает как DoNotShip
    & $sevenZip a -tzip -mx=7 -mmt=on $ZipPath "$BuildDir\*" "-xr!*_BurstDebugInformation_DoNotShip" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7-Zip вернул код $LASTEXITCODE" }
} else {
    Write-Warning "7-Zip не найден, используем Compress-Archive (медленнее и без исключений)"
    Compress-Archive -Path (Join-Path $BuildDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
}

$zipInfo = Get-Item $ZipPath
$sha = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$sizeMb = [math]::Round($zipInfo.Length / 1MB, 1)

Write-Host "   размер : $sizeMb МБ ($($zipInfo.Length) байт)"
Write-Host "   sha256 : $sha"

if ($zipInfo.Length -gt 2GB) {
    throw "Архив больше 2 ГБ — GitHub Releases такой файл не примет."
}

# ---------------------------------------------------------------- лаунчер

$assets = @($ZipPath)

if ($NoLauncher) {
    Write-Warning "Лаунчер не прикладывается: ссылка /releases/latest/download/SkelterLauncher.exe после этого релиза отдаст 404."
} else {
    Write-Step "Собираем SkelterLauncher.exe"

    $dotnet = Resolve-Tool 'dotnet' @("C:\Program Files\dotnet\dotnet.exe")
    if ($null -eq $dotnet) { throw "Не найден dotnet. Установи: winget install Microsoft.DotNet.SDK.8" }

    $publishDir = Join-Path $DistDir 'launcher'
    & $dotnet publish (Join-Path $RepoRoot 'launcher\SkelterLauncher.csproj') -c Release -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "Сборка лаунчера упала с кодом $LASTEXITCODE" }

    $launcherExe = Join-Path $publishDir 'SkelterLauncher.exe'
    if (-not (Test-Path $launcherExe)) { throw "Лаунчер не собрался: нет $launcherExe" }

    $assets += $launcherExe
}

# ---------------------------------------------------------------- version.json

Write-Step "Обновляем version.json"

$manifest = [ordered]@{
    version      = $Version
    releaseDate  = (Get-Date -Format 'yyyy-MM-dd')
    url          = "https://github.com/breakneckz/skelterUpdater/releases/download/$Tag/$ZipName"
    sha256       = $sha
    size         = $zipInfo.Length
    executable   = $Exe
    cleanInstall = [bool]$CleanInstall
    notes        = $Notes
}

$manifestPath = Join-Path $RepoRoot 'version.json'
$json = $manifest | ConvertTo-Json -Depth 3

# Строго без BOM: Set-Content -Encoding utf8 в Windows PowerShell 5.1 его добавляет,
# а BOM в начале JSON ломает разбор на стороне лаунчера.
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Write-Host $json

if ($DryRun) {
    Write-Step "DryRun: архив собран, version.json обновлён локально. Ничего не залито."
    return
}

# ---------------------------------------------------------------- релиз

Write-Step "Заливаем релиз $Tag на GitHub"

$releaseNotes = if ([string]::IsNullOrWhiteSpace($Notes)) { "Skelter Arena $Version" } else { $Notes }
$existing = & $gh release view $Tag --repo breakneckz/skelterUpdater 2>$null

if ($LASTEXITCODE -eq 0) {
    Write-Host "   релиз $Tag уже есть — перезаливаем ассеты"
    & $gh release upload $Tag @assets --repo breakneckz/skelterUpdater --clobber
} else {
    & $gh release create $Tag @assets --repo breakneckz/skelterUpdater --title "Skelter Arena $Version" --notes $releaseNotes
}

if ($LASTEXITCODE -ne 0) { throw "gh вернул код $LASTEXITCODE" }

# ---------------------------------------------------------------- push манифеста

# version.json пушим ПОСЛЕ релиза: иначе лаунчер увидит новую версию раньше, чем появится файл.
Write-Step "Пушим version.json"

git -C $RepoRoot add version.json
git -C $RepoRoot commit -m "release: $Version"
git -C $RepoRoot push

Write-Step "Готово. Лаунчеры игроков увидят $Version в течение пары минут."
