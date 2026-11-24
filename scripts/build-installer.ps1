<#
.SYNOPSIS
  Builds the File Compare installer: publishes a self-contained single-file FileCompare.exe and compiles
  installer\FileCompare.iss with Inno Setup.

.DESCRIPTION
  Output: dist\FileCompare-Setup-<version>-x64.exe and a .sha256 file next to it.
  The version comes from <Version> in src\FileCompare.App\FileCompare.App.csproj.

  The license texts of the bundled libraries are downloaded once into build\licenses (commit them, so
  later builds need no network). Pass -SkipLicenseDownload to fail instead of downloading.

.PARAMETER Iscc
  Path to ISCC.exe. By default the usual Inno Setup 7 and 6 install folders are searched.

.PARAMETER SkipPublish
  Reuse the existing publish folder instead of running dotnet publish again.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
#>
param(
    [string]$Iscc = '',
    [switch]$SkipPublish,
    [switch]$SkipLicenseDownload
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'src\FileCompare.App\FileCompare.App.csproj'
$publish = Join-Path $root 'publish'
$licenses = Join-Path $root 'build\licenses'
$script = Join-Path $root 'installer\FileCompare.iss'

# --- Version -------------------------------------------------------------------------------------
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $csproj" }
Write-Host "File Compare $version"

# --- Third-party license texts -------------------------------------------------------------------
$licenseFiles = [ordered]@{
    'AvalonEdit-LICENSE.txt' = 'https://raw.githubusercontent.com/icsharpcode/AvalonEdit/master/LICENSE'
    'DiffPlex-LICENSE.txt'   = 'https://raw.githubusercontent.com/mmanela/diffplex/master/License.txt'
}
New-Item -ItemType Directory -Force $licenses | Out-Null
foreach ($name in $licenseFiles.Keys) {
    $target = Join-Path $licenses $name
    if (Test-Path $target) { continue }
    if ($SkipLicenseDownload) { throw "$target is missing (run once without -SkipLicenseDownload)" }
    Write-Host "Downloading $name"
    Invoke-WebRequest -Uri $licenseFiles[$name] -OutFile $target -UseBasicParsing
}

# --- Publish ---------------------------------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Host 'Publishing...'
    dotnet publish $csproj -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false -o $publish --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
$exe = Join-Path $publish 'FileCompare.exe'
if (-not (Test-Path $exe)) { throw "$exe not found" }

# --- Inno Setup ------------------------------------------------------------------------------------
$candidates = @(
    $Iscc,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
) | Where-Object { $_ -and (Test-Path $_) }
$isccPath = $candidates | Select-Object -First 1
if (-not $isccPath) { throw 'ISCC.exe (Inno Setup) not found. Install Inno Setup or pass -Iscc <path>.' }

Write-Host "Compiling the installer with $isccPath"
& $isccPath /Qp "/DAppVersion=$version" "/DPublishDir=$publish" "/DLicensesDir=$licenses" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)" }

# --- Checksum --------------------------------------------------------------------------------------
$setup = Join-Path $root "dist\FileCompare-Setup-$version-x64.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $setup -Leaf)" | Set-Content "$setup.sha256" -Encoding ascii
Write-Host ''
Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
Write-Host "SHA-256:   $hash"
