param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$CertificatePath = $env:WINDOWS_SIGNING_CERT_PATH,
  [string]$CertificatePassword = $env:WINDOWS_SIGNING_CERT_PASSWORD
)
$ErrorActionPreference = "Stop"
$desktop = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $desktop "src\BusinessOS.Restaurant.Desktop\BusinessOS.Restaurant.Desktop.csproj"
$publish = Join-Path $desktop "artifacts\publish"
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $project -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$exe = Join-Path $publish "BusinessOS.Restaurant.Desktop.exe"
if (!(Test-Path $exe)) { throw "Published desktop executable was not produced." }

if ($CertificatePath) {
  $signtool = (Get-Command signtool.exe -ErrorAction Stop).Source
  & $signtool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword /tr "http://timestamp.digicert.com" /td SHA256 $exe
  if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed." }
  & $signtool verify /pa $exe
  if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed." }
  Write-Output "SIGNED_RELEASE=true"
} else {
  Write-Warning "Signing certificate not configured; artifact is development/CI-only and must not be distributed as a production-signed release."
  Write-Output "SIGNED_RELEASE=false"
}
Get-FileHash $exe -Algorithm SHA256
