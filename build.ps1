# Build Cooldown (native .NET 8).
# Output:
#   dist\Cooldown\                  portable folder
#   dist\Cooldown-<ver>-win-x64.zip
#   dist\CooldownSetup-<ver>.exe    if Inno Setup 6 is installed

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

Get-Process Cooldown, "Cooldown.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$csproj = Get-Content "src\Cooldown\Cooldown.csproj" -Raw
if ($env:RELEASE_VERSION) {
    $version = $env:RELEASE_VERSION.TrimStart("v")
} elseif ($csproj -match "<Version>([^<]+)</Version>") {
    $version = $Matches[1]
} else {
    $version = "0.0.0"
}

$out = Join-Path $PSScriptRoot "dist\Cooldown"
Write-Host "Publishing Cooldown UI $version..." -ForegroundColor Cyan
dotnet publish src\Cooldown\Cooldown.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:Version=$version -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing background agent..." -ForegroundColor Cyan
dotnet publish src\Cooldown.Agent\Cooldown.Agent.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true -p:Version=$version -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $out "Cooldown.exe"
$agent = Join-Path $out "Cooldown.Agent.exe"
if (-not ((Test-Path $exe) -and (Test-Path $agent))) {
    Write-Host "Publish failed" -ForegroundColor Red
    exit 1
}

$total = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Published $total MB to $out" -ForegroundColor Green

$zip = Join-Path $PSScriptRoot "dist\Cooldown-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Zip: $zip" -ForegroundColor Green

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "Compiling installer..." -ForegroundColor Cyan
    & $iscc /DAppVersion=$version "installer\cooldown.iss"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $setup = Join-Path $PSScriptRoot "dist\CooldownSetup-$version.exe"
    Write-Host "Installer: $setup" -ForegroundColor Green
} else {
    Write-Host "Inno Setup 6 not found - skipped installer. Install from https://jrsoftware.org/isinfo.php then re-run." -ForegroundColor Yellow
}
