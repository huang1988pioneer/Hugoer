#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version = "1.8.0",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller,
    [switch]$InstallTools,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

function Resolve-ProjectRoot {
    $candidate = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    if (Test-Path (Join-Path $candidate "Hugoer.csproj")) {
        return $candidate
    }

    $fallback = [IO.Path]::GetFullPath((Get-Location).Path)
    if (Test-Path (Join-Path $fallback "Hugoer.csproj")) {
        return $fallback
    }

    throw "找不到 Hugoer.csproj；請從專案根目錄或 scripts 資料夾執行。"
}

function Assert-SafeOutputPath([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒絕清理專案外的路徑：$full"
    }
    return $full
}

function Reset-OutputDirectory([string]$Path, [string]$Root) {
    $safePath = Assert-SafeOutputPath $Path $Root
    if (Test-Path -LiteralPath $safePath) {
        Write-Host "==> 清理 $safePath" -ForegroundColor DarkGray
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $safePath -Force | Out-Null
    return $safePath
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version 必須是語意版本（例如 1.8.0）：$Version"
}
if ([string]::IsNullOrWhiteSpace($Runtime)) {
    throw "Runtime 不可為空。"
}

$Root = Resolve-ProjectRoot
$DistRoot = Assert-SafeOutputPath (Join-Path $Root "dist") $Root
$PublishRoot = Join-Path $DistRoot "publish"
$PublishDir = Join-Path $PublishRoot $Runtime
$SingleDir = Join-Path $DistRoot "single"
$ReleaseDir = Join-Path $DistRoot "releases"
$Project = Join-Path $Root "Hugoer.csproj"

Write-Host "==> Hugoer Windows release v$Version ($Runtime)" -ForegroundColor Cyan
Write-Host "    Root: $Root"

# A release is reproducible by default: no executable from an older version
# remains in the generated output directories. All paths are checked to stay
# below the repository before they are removed.
Reset-OutputDirectory $PublishRoot $Root | Out-Null
Reset-OutputDirectory $SingleDir $Root | Out-Null
Reset-OutputDirectory $ReleaseDir $Root | Out-Null
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

Write-Host "==> dotnet publish (self-contained single-file) ..." -ForegroundColor Cyan
$publishArgs = @(
    "publish", $Project,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-p:InformationalVersion=$Version",
    "-p:ContinuousIntegrationBuild=true",
    "-p:DebugType=embedded",
    "-o", $PublishDir
)
if ($NoRestore) { $publishArgs += "--no-restore" }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $PublishDir "Hugoer.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Hugoer.exe not found at $exe" }

$singleExe = Join-Path $SingleDir "Hugoer.exe"
Copy-Item -LiteralPath $exe -Destination $singleExe -Force
$fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($singleExe)
$reportedVersion = $fileInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($reportedVersion)) {
    throw "無法讀取產物版本資訊：$singleExe"
}

$archive = Join-Path $ReleaseDir ("Hugoer-{0}-{1}-portable.zip" -f $Version, $Runtime)
Compress-Archive -LiteralPath $singleExe -DestinationPath $archive -Force
Write-Host ("    EXE: {0} ({1:N1} MB, file version {2})" -f $singleExe, ((Get-Item $singleExe).Length / 1MB), $reportedVersion) -ForegroundColor Green
Write-Host "    ZIP: $archive" -ForegroundColor Green

if (-not $SkipInstaller) {
    # Velopack is optional. It is never installed implicitly unless the caller
    # opts into -InstallTools, keeping a release run offline-friendly.
    $vpk = Get-Command vpk -ErrorAction SilentlyContinue
    if (-not $vpk -and $InstallTools) {
        Write-Host "==> Installing Velopack CLI ..." -ForegroundColor Cyan
        dotnet tool update -g vpk
        if ($LASTEXITCODE -ne 0) { dotnet tool install -g vpk }
        $vpk = Get-Command vpk -ErrorAction SilentlyContinue
    }

    if ($vpk) {
        Write-Host "==> Velopack pack ..." -ForegroundColor Cyan
        $veloOut = Join-Path $ReleaseDir "velopack"
        New-Item -ItemType Directory -Path $veloOut -Force | Out-Null
        & $vpk.Source pack `
            --packId Hugoer `
            --packVersion $Version `
            --packDir $PublishDir `
            --mainExe Hugoer.exe `
            --packTitle "Hugoer" `
            --outputDir $veloOut
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Velopack pack failed (exit $LASTEXITCODE); portable artifacts remain valid."
        }
    } else {
        Write-Host "==> vpk not found; skip Velopack installer (use -InstallTools to install)." -ForegroundColor Yellow
    }

    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if ($iscc) {
        Write-Host "==> Inno Setup ..." -ForegroundColor Cyan
        $iss = Join-Path $Root "installer\hugoer.iss"
        & $iscc $iss "/DMyAppVersion=$Version" "/DMyPublishDir=$PublishDir"
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Inno Setup failed (exit $LASTEXITCODE); portable artifacts remain valid."
        }
    } else {
        Write-Host "==> Inno Setup not found; skip classic installer." -ForegroundColor DarkGray
    }
} else {
    Write-Host "==> SkipInstaller set; portable artifacts only." -ForegroundColor Yellow
}

# Produce checksums and a machine-readable manifest after optional installers
# have finished. Paths in both files are relative to dist\releases.
$releaseFiles = @(Get-ChildItem -LiteralPath $ReleaseDir -File -Recurse |
    Where-Object { $_.Name -notin @("SHA256SUMS.txt", "release-manifest.json") })
$fileRecords = foreach ($file in $releaseFiles) {
    $relative = [IO.Path]::GetRelativePath($ReleaseDir, $file.FullName).Replace('\', '/')
    [pscustomobject]@{
        path = $relative
        size = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$checksumPath = Join-Path $ReleaseDir "SHA256SUMS.txt"
$checksumLines = $fileRecords | ForEach-Object { "{0}  {1}" -f $_.sha256, $_.path }
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ASCII

$manifest = [pscustomobject]@{
    product = "Hugoer"
    version = $Version
    runtime = $Runtime
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    executable = [IO.Path]::GetRelativePath($Root, $singleExe).Replace('\', '/')
    fileVersion = $reportedVersion
    files = @($fileRecords)
}
$manifestPath = Join-Path $ReleaseDir "release-manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "==> Release metadata:" -ForegroundColor Cyan
Write-Host "    Checksums: $checksumPath"
Write-Host "    Manifest : $manifestPath"
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Portable single EXE : $singleExe"
Write-Host "  Full publish folder : $PublishDir"
Write-Host "  Release artifacts    : $ReleaseDir"
