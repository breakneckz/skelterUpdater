<#
.SYNOPSIS
    Публикует новую версию Skelter Arena: пакует сборку, строит патчи с прошлых версий,
    заливает релиз на GitHub и обновляет version.json, по которому лаунчер видит обновление.

.EXAMPLE
    .\tools\publish-release.ps1 -Version 1.0.34 -BuildDir "C:\Users\Dan\Desktop\Skelter" -Notes "Balance fixes" -NotesRu "Починил баланс"

.EXAMPLE
    # Проверить, сколько весят патчи, ничего не заливая
    .\tools\publish-release.ps1 -Version 1.0.34 -BuildDir "C:\Users\Dan\Desktop\Skelter" -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$BuildDir,

    # Что изменилось — показывается в лаунчере после обновления. Notes — английский (по умолчанию),
    # NotesRu — для игроков с русским интерфейсом. Если NotesRu пуст, им тоже покажется Notes.
    [string]$Notes = "",
    [string]$NotesRu = "",

    # Лаунчер снесёт содержимое папки игры перед распаковкой полного архива.
    # Патчам это не нужно: удалённые из сборки файлы они убирают сами.
    [switch]$CleanInstall,

    # Со скольких предыдущих версий строить патчи. Игрок, пропустивший больше, скачает полный архив.
    [ValidateRange(0, 20)]
    [int]$PatchDepth = 3,

    # Не прикладывать лаунчер к релизу. По умолчанию он собирается и заливается всегда,
    # чтобы ссылка /releases/latest/download/SkelterLauncher.exe никогда не отдавала 404.
    [switch]$NoLauncher,

    # Спаковать, построить патчи и посчитать хеши, но ничего не заливать.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = 'breakneckz/skelterUpdater'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DistDir = Join-Path $RepoRoot 'dist'
$ZipName = "Skelter-Arena-$Version.zip"
$ZipPath = Join-Path $DistDir $ZipName
$Tag = "v$Version"
$Exe = 'Skelter Arena.exe'
$ReleaseUrl = "https://github.com/$Repo/releases/download/$Tag"
$HDiffZ = Join-Path $PSScriptRoot 'hdiffpatch\hdiffz.exe'

# Что не должно попадать в сборку для игроков. Лаунчер, его installed.json и кеш updates
# оказываются в папке сборки, если запускать лаунчер прямо из неё.
$RootExcludes = @('SkelterLauncher*.exe', 'installed.json', 'updates', '.update-staging')
# Отладочная информация Burst, которую Unity помечает как DoNotShip, — на любой глубине.
$AnyExcludes = @('*_BurstDebugInformation_DoNotShip')

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

function Get-SevenZipExcludes {
    $RootExcludes | ForEach-Object { "-x!$_" }
    $AnyExcludes | ForEach-Object { "-xr!$_" }
}

function Test-Excluded([string]$Relative) {
    $parts = $Relative -split '[\\/]'
    foreach ($pattern in $RootExcludes) {
        if ($parts[0] -like $pattern) { return $true }
    }
    foreach ($part in $parts) {
        foreach ($pattern in $AnyExcludes) {
            if ($part -like $pattern) { return $true }
        }
    }
    return $false
}

# Таблица файлов папки: относительный путь (через /) -> размер и SHA-256.
function Get-FileTable([string]$Root, [switch]$IncludeExcluded) {
    $table = @{}
    $prefix = $Root.TrimEnd('\') + '\'

    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
        $relative = $file.FullName.Substring($prefix.Length)
        if (-not $IncludeExcluded -and (Test-Excluded $relative)) { continue }

        $table[$relative.Replace('\', '/')] = [pscustomobject]@{
            Size   = $file.Length
            Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLower()
        }
    }

    return $table
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

$junk = @(Get-ChildItem -LiteralPath $BuildDir -Force | Where-Object { $name = $_.Name; @($RootExcludes | Where-Object { $name -like $_ }).Count -gt 0 } | ForEach-Object { $_.Name })
if ($junk.Count -gt 0) {
    Write-Warning "В папке сборки лежит лишнее, в архив оно не попадёт: $($junk -join ', ')"
}

# ---------------------------------------------------------------- упаковка

Write-Step "Пакуем $BuildDir -> $ZipName"

New-Item -ItemType Directory -Force $DistDir | Out-Null
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

if ($null -ne $sevenZip) {
    & $sevenZip a -tzip -mx=7 -mmt=on $ZipPath "$BuildDir\*" @(Get-SevenZipExcludes) | Out-Null
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

$assets = @($ZipPath)

# ---------------------------------------------------------------- патчи

# Патч A -> B содержит только изменившиеся и новые файлы B, но дифф считается относительно
# всей папки A: Unity пересобирает resources.assets и т.п. целиком, а байты внутри в основном старые.
# Удалённые из сборки файлы перечисляются в манифесте — их лаунчер удалит сам.

$patches = New-Object System.Collections.Generic.List[object]

$previous = @(
    Get-ChildItem -LiteralPath $DistDir -Filter 'Skelter-Arena-*.zip' |
        ForEach-Object {
            if ($_.Name -match '^Skelter-Arena-(\d+\.\d+\.\d+)\.zip$' -and $Matches[1] -ne $Version) {
                [pscustomobject]@{ Version = [version]$Matches[1]; Path = $_.FullName }
            }
        } |
        Sort-Object Version -Descending |
        Select-Object -First $PatchDepth
)

if ($PatchDepth -eq 0) {
    Write-Step "Патчи отключены (-PatchDepth 0)"
} elseif ($null -eq $sevenZip -or -not (Test-Path $HDiffZ)) {
    Write-Warning "Нужны 7-Zip и $HDiffZ — без них патчи не строятся, игроки будут качать полный архив."
} elseif ($previous.Count -eq 0) {
    Write-Step "В dist нет архивов прошлых версий — патчи строить не с чего"
} else {
    Write-Step "Строим патчи с версий: $(($previous | ForEach-Object { $_.Version }) -join ', ')"

    Write-Host "   хешируем новую сборку…"
    $newFiles = Get-FileTable $BuildDir

    $work = Join-Path ([System.IO.Path]::GetTempPath()) "skelter-patch-$([guid]::NewGuid().ToString('N'))"

    try {
        foreach ($base in $previous) {
            $from = $base.Version.ToString()
            $baseDir = Join-Path $work "base-$from"
            $subsetDir = Join-Path $work "subset-$from"

            # Распаковываем как есть: в старых архивах бывал мусор (копия лаунчера, installed.json),
            # и у игроков он лежит в папке игры — патч его удалит.
            & $sevenZip x -y "-o$baseDir" $base.Path | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "7-Zip не распаковал $($base.Path): код $LASTEXITCODE" }

            $baseAll = Get-FileTable $baseDir -IncludeExcluded

            $changed = @($newFiles.Keys | Where-Object { -not $baseAll.ContainsKey($_) -or $baseAll[$_].Sha256 -ne $newFiles[$_].Sha256 } | Sort-Object)
            # SkelterLauncher.exe в папке игры — копия, на которую смотрит ярлык. Её не трогаем никогда.
            $removed = @($baseAll.Keys | Where-Object { -not $newFiles.ContainsKey($_) -and $_ -ne 'SkelterLauncher.exe' } | Sort-Object)

            $entry = [ordered]@{
                from   = $from
                url    = ""
                sha256 = ""
                size   = 0
                files  = @()
                remove = $removed
            }

            if ($changed.Count -gt 0) {
                # Мусор из старой версии в качестве источника не используем: у игрока его могло и не быть.
                foreach ($relative in @($baseAll.Keys)) {
                    if (Test-Excluded $relative) { Remove-Item -LiteralPath (Join-Path $baseDir $relative) -Force }
                }

                foreach ($relative in $changed) {
                    $target = Join-Path $subsetDir $relative
                    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
                    Copy-Item -LiteralPath (Join-Path $BuildDir $relative) -Destination $target
                }

                $patchName = "Skelter-Arena-$from-to-$Version.hdiff"
                $patchPath = Join-Path $DistDir $patchName
                if (Test-Path $patchPath) { Remove-Item $patchPath -Force }

                # hdiffz сам накладывает готовый патч на проверку и падает, если результат не сошёлся.
                & $HDiffZ -m-6 -SD -c-zstd-21-24 -p-8 $baseDir $subsetDir $patchPath | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "hdiffz ($from -> $Version) вернул код $LASTEXITCODE" }

                $patchInfo = Get-Item $patchPath

                # Патч почти с полный архив — смысла нет, пусть качают архив.
                if ($patchInfo.Length -ge $zipInfo.Length * 0.8) {
                    Write-Host ("   {0} -> {1}: патч {2:N1} МБ — не выгоднее полного архива, пропускаем" -f $from, $Version, ($patchInfo.Length / 1MB))
                    Remove-Item $patchPath -Force
                    continue
                }

                $entry.url = "$ReleaseUrl/$patchName"
                $entry.sha256 = (Get-FileHash $patchPath -Algorithm SHA256).Hash.ToLower()
                $entry.size = $patchInfo.Length
                $entry.files = @($changed | ForEach-Object {
                    [ordered]@{ path = $_; sha256 = $newFiles[$_].Sha256; size = $newFiles[$_].Size }
                })

                $assets += $patchPath
            }

            Write-Host ("   {0} -> {1}: {2} файлов изменено, {3} удалено, патч {4:N1} МБ" -f $from, $Version, $changed.Count, $removed.Count, ($entry.size / 1MB))
            $patches.Add($entry)

            Remove-Item -LiteralPath $baseDir, $subsetDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- лаунчер

$manifestPath = Join-Path $RepoRoot 'version.json'
$launcherEntry = $null

if ($NoLauncher) {
    Write-Warning "Лаунчер не прикладывается: ссылка /releases/latest/download/SkelterLauncher.exe после этого релиза отдаст 404."

    # Самообновление продолжает смотреть на лаунчер из прошлого релиза.
    if (Test-Path $manifestPath) {
        $old = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($old.PSObject.Properties.Name -contains 'launcher') { $launcherEntry = $old.launcher }
    }
} else {
    Write-Step "Собираем SkelterLauncher.exe"

    $dotnet = Resolve-Tool 'dotnet' @("C:\Program Files\dotnet\dotnet.exe")
    if ($null -eq $dotnet) { throw "Не найден dotnet. Установи: winget install Microsoft.DotNet.SDK.8" }

    $csproj = Join-Path $RepoRoot 'launcher\SkelterLauncher.csproj'
    $publishDir = Join-Path $DistDir 'launcher'
    & $dotnet publish $csproj -c Release -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "Сборка лаунчера упала с кодом $LASTEXITCODE" }

    $launcherExe = Join-Path $publishDir 'SkelterLauncher.exe'
    if (-not (Test-Path $launcherExe)) { throw "Лаунчер не собрался: нет $launcherExe" }

    $launcherVersion = (Select-Xml -Path $csproj -XPath '//Version').Node.InnerText
    $launcherFile = Get-Item $launcherExe

    $launcherEntry = [ordered]@{
        version = $launcherVersion
        url     = "$ReleaseUrl/SkelterLauncher.exe"
        sha256  = (Get-FileHash $launcherExe -Algorithm SHA256).Hash.ToLower()
        size    = $launcherFile.Length
    }

    Write-Host "   версия лаунчера: $launcherVersion"
    $assets += $launcherExe
}

# ---------------------------------------------------------------- version.json

Write-Step "Обновляем version.json"

$manifest = [ordered]@{
    version      = $Version
    releaseDate  = (Get-Date -Format 'yyyy-MM-dd')
    url          = "$ReleaseUrl/$ZipName"
    sha256       = $sha
    size         = $zipInfo.Length
    executable   = $Exe
    cleanInstall = [bool]$CleanInstall
    notes        = $Notes
    notesRu      = $NotesRu
    patches      = $patches.ToArray()
}

if ($null -ne $launcherEntry) { $manifest.launcher = $launcherEntry }

$json = $manifest | ConvertTo-Json -Depth 6

# Строго без BOM: Set-Content -Encoding utf8 в Windows PowerShell 5.1 его добавляет,
# а BOM в начале JSON ломает разбор на стороне лаунчера 1.0.
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Write-Host ("   полный архив: {0:N1} МБ, патчей: {1}" -f ($zipInfo.Length / 1MB), $patches.Count)

if ($DryRun) {
    Write-Step "DryRun: архив и патчи собраны, version.json обновлён локально. Ничего не залито."
    return
}

# ---------------------------------------------------------------- релиз

Write-Step "Заливаем релиз $Tag на GitHub"

$releaseNotes = if ([string]::IsNullOrWhiteSpace($Notes)) { "Skelter Arena $Version" } else { $Notes }

# В Windows PowerShell 5.1 при 'Stop' любой вывод нативной команды в stderr
# (gh "release not found", прогресс git push) превращается в фатальную ошибку.
# Дальше полагаемся только на $LASTEXITCODE.
$ErrorActionPreference = 'Continue'

& $gh release view $Tag --repo $Repo 2>$null | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host "   релиз $Tag уже есть — перезаливаем ассеты"
    & $gh release upload $Tag @assets --repo $Repo --clobber
} else {
    & $gh release create $Tag @assets --repo $Repo --title "Skelter Arena $Version" --notes $releaseNotes
}

if ($LASTEXITCODE -ne 0) { throw "gh вернул код $LASTEXITCODE" }

# ---------------------------------------------------------------- push манифеста

# version.json пушим ПОСЛЕ релиза: иначе лаунчер увидит новую версию раньше, чем появятся файлы.
Write-Step "Пушим version.json"

git -C $RepoRoot add version.json
if ($LASTEXITCODE -ne 0) { throw "git add вернул код $LASTEXITCODE" }
git -C $RepoRoot commit -m "release: $Version"
if ($LASTEXITCODE -ne 0) { throw "git commit вернул код $LASTEXITCODE" }
git -C $RepoRoot push
if ($LASTEXITCODE -ne 0) { throw "git push вернул код $LASTEXITCODE" }

Write-Step "Готово. Лаунчеры игроков увидят $Version в течение пары минут."
