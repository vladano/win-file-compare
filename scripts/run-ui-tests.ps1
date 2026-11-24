<#
.SYNOPSIS
  Runs the automated UI tests (tests\FileCompare.UiTests) and prints the results by checklist ID (docs\TESTING.md).

.DESCRIPTION
  The tests start File Compare, open File Explorer windows and move the real mouse, so do not use the PC while they
  run (a few minutes). They use their own data folder and a throwaway registry key, so your settings, recent list and
  Explorer menu are not touched. Copies of File Compare you have open are left alone.

.PARAMETER Filter
  Runs only some tests, for example -Filter EM (Explorer menu) or -Filter DD05.

.PARAMETER Installer
  Also runs the installer tests (IN). They build the installer with build-installer.ps1, then install and uninstall
  File Compare for your user several times. They are skipped if File Compare is installed on this PC.

.PARAMETER SkipInstallerBuild
  With -Installer: test the installer already in dist\ instead of building a new one.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\run-ui-tests.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\run-ui-tests.ps1 -Installer -Filter Installer
#>
param(
    [string]$Filter = '',
    [switch]$Installer,
    [switch]$SkipInstallerBuild,
    [switch]$NoCountdown
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root 'TestResults\ui'

Write-Host 'Building...'
dotnet build (Join-Path $root 'FileCompare.sln') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "The build failed ($LASTEXITCODE)" }

if ($Installer -and -not $SkipInstallerBuild) {
    Write-Host 'Building the installer...'
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -SkipLicenseDownload | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "The installer build failed ($LASTEXITCODE)" }
}

if (-not $NoCountdown) {
    Write-Host ''
    Write-Host 'The UI tests now take over the mouse and open windows. Do not use the PC until they finish.' -ForegroundColor Yellow
    for ($i = 5; $i -gt 0; $i--) { Write-Host "  starting in $i..."; Start-Sleep 1 }
}

if (Test-Path $results) { Remove-Item $results -Recurse -Force }
$env:FILECOMPARE_UI_TESTS = '1'
if ($Installer) { $env:FILECOMPARE_INSTALLER_TESTS = '1' }
try {
    $arguments = @('test', (Join-Path $root 'tests\FileCompare.UiTests'), '--no-build', '--nologo',
        '--logger', 'trx;LogFileName=ui-tests.trx', '--results-directory', $results)
    if ($Filter) { $arguments += @('--filter', "FullyQualifiedName~$Filter") }
    & dotnet @arguments | Out-Null
    $testExit = $LASTEXITCODE
}
finally {
    Remove-Item Env:FILECOMPARE_UI_TESTS -ErrorAction SilentlyContinue
    Remove-Item Env:FILECOMPARE_INSTALLER_TESTS -ErrorAction SilentlyContinue
}

# --- Results by checklist ID ---------------------------------------------------------------------
$trx = Join-Path $results 'ui-tests.trx'
if (-not (Test-Path $trx)) { throw "No results were written ($trx). Exit code: $testExit" }
[xml]$xml = Get-Content $trx -Raw
$rows = foreach ($r in $xml.TestRun.Results.UnitTestResult) {
    $name = ($r.testName -split '\.')[-1]
    $ids = ([regex]::Matches($name, '(?<=^|_)(IN|EM|DD)\d\d')).Value -join ', '
    $message = $r.Output.ErrorInfo.Message
    [pscustomobject]@{
        ID      = $ids
        Result  = $r.outcome
        Seconds = [math]::Round(([TimeSpan]$r.duration).TotalSeconds, 1)
        Test    = ($name -replace '^((IN|EM|DD)\d\d_)+', '')
        Problem = if ($message) { ($message -split "`n")[0].Trim() } else { '' }
    }
}
$rows | Sort-Object ID | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host

$passed = @($rows | Where-Object Result -eq 'Passed').Count
$skipped = @($rows | Where-Object Result -eq 'NotExecuted').Count
$failed = @($rows).Count - $passed - $skipped
Write-Host ("{0} passed, {1} failed, {2} skipped. Details: {3}" -f $passed, $failed, $skipped, $trx)
exit $(if ($failed -gt 0) { 1 } else { 0 })
