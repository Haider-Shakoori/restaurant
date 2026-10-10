$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Runs only on a disposable GitHub-hosted Windows runner. This exercises actual
# Windows file installation, the installed production executable startup and
# silent uninstall, without bypassing the real installer activation requirement.
if ($env:GITHUB_ACTIONS -ne "true" -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw "CI installation smoke tests may run only inside GitHub Actions."
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source }

$publishedExe = Join-Path (Get-Location).Path "artifacts\publish\BusinessOS.Restaurant.Desktop.exe"
$ciInstallerScript = Join-Path (Get-Location).Path "installer\BusinessOS.Restaurant.InstallSmoke.iss"
$ciOutputDir = Join-Path (Get-Location).Path "installer\ci-output"
$installDir = Join-Path $env:RUNNER_TEMP "BusinessOS-Restaurant-Installation-Smoke"
$installedExe = Join-Path $installDir "BusinessOS.Restaurant.Desktop.exe"

if (!(Test-Path $publishedExe)) { throw "Published production EXE was not found." }
if (Test-Path $installDir) { throw "Refusing to use existing CI installation directory." }
if (Test-Path $ciOutputDir) { Remove-Item $ciOutputDir -Recurse -Force }

Write-Host "Compiling non-distributable CI installer from the exact production publish output."
& $iscc $ciInstallerScript
if ($LASTEXITCODE -ne 0) { throw "CI installer compilation failed." }

$setup = Get-ChildItem $ciOutputDir -Filter "BusinessOS-Restaurant-CI-Install-Smoke-DO-NOT-DISTRIBUTE.exe" |
    Select-Object -First 1
if (-not $setup) { throw "CI-only install executable is missing." }

$didInstall = $false
$didUninstall = $false
$appProcess = $null
try {
    Write-Host "Installing exact published Restaurant binaries to an isolated runner directory."
    $args = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/DIR=$installDir")
    $installer = Start-Process -FilePath $setup.FullName -ArgumentList $args -PassThru -Wait
    if ($installer.ExitCode -ne 0) { throw "CI installer exited with code $($installer.ExitCode)." }
    $didInstall = $true

    if (!(Test-Path $installedExe)) { throw "BusinessOS executable not installed." }
    if (!(Test-Path (Join-Path $installDir "unins000.exe"))) {
        throw "Uninstaller not registered inside installed directory."
    }

    $expected = (Get-FileHash $publishedExe -Algorithm SHA256).Hash
    $actual = (Get-FileHash $installedExe -Algorithm SHA256).Hash
    if ($expected -ne $actual) {
        throw "Installed EXE is different from the CI-tested published executable."
    }
    Write-Host "PASS: installed EXE SHA256 matches build: $actual"

    # No real subscription, activation secrets or test production account needed.
    # The installed desktop must open its normal activation-first screen and
    # remain responsive instead of crashing. The license gate stays enabled.
    $logRoot = Join-Path $env:LOCALAPPDATA "BusinessOS\Restaurant\logs"
    if (Test-Path $logRoot) {
        Get-ChildItem $logRoot -Filter "desktop-*.log" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    $appProcess = Start-Process -FilePath $installedExe -PassThru
    Start-Sleep -Seconds 8
    $appProcess.Refresh()
    if ($appProcess.HasExited) {
        if (Test-Path $logRoot) {
            Get-ChildItem $logRoot -Filter "desktop-*.log" |
                Get-Content -Tail 80 | Write-Host
        }
        throw "Installed Restaurant app exited during activation-screen startup."
    }
    if (Test-Path $logRoot) {
        $failure = Get-ChildItem $logRoot -Filter "desktop-*.log" |
            ForEach-Object { Get-Content $_.FullName -Raw } |
            Where-Object { $_ -match "Main window initialization|(?m)^\[[^\]]+\] ui$" }
        if ($failure) { throw "Installed app reported a fatal UI/startup error." }
    }
    Write-Host "PASS: installed application launched and remained running for 8 seconds."
} finally {
    if ($null -ne $appProcess) {
        try {
            $appProcess.Refresh()
            if (!$appProcess.HasExited) { Stop-Process -Id $appProcess.Id -Force }
        } catch { Write-Host "Application cleanup: $($_.Exception.Message)" }
    }
    if ($didInstall) {
        $uninstallExe = Join-Path $installDir "unins000.exe"
        if (Test-Path $uninstallExe) {
            $uninstaller = Start-Process -FilePath $uninstallExe -ArgumentList @(
                "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"
            ) -PassThru -Wait
            if ($uninstaller.ExitCode -eq 0) { $didUninstall = $true }
            Write-Host "Uninstaller exit code: $($uninstaller.ExitCode)"
        }
    }
    # The temporary CI installer itself is intentionally not uploaded.
    if (Test-Path $ciOutputDir) { Remove-Item $ciOutputDir -Recurse -Force }
}

if (!$didInstall) { throw "Install did not complete." }
if (!$didUninstall) { throw "Uninstall did not complete cleanly." }
if (Test-Path $installedExe) { throw "Uninstall left the Restaurant app binary behind." }
Write-Host "PASS: installation, packaged EXE startup, SHA256 equivalence and uninstall."
