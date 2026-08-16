# Build Cooldown (native .NET 8).
# Output: dist\Cooldown\Cooldown.exe + Cooldown.Agent.exe

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

$out = Join-Path $PSScriptRoot "dist\Cooldown"
Write-Host "Publishing Cooldown UI..." -ForegroundColor Cyan
dotnet publish src\Cooldown\Cooldown.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing background agent..." -ForegroundColor Cyan
dotnet publish src\Cooldown.Agent\Cooldown.Agent.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $out "Cooldown.exe"
$agent = Join-Path $out "Cooldown.Agent.exe"
if ((Test-Path $exe) -and (Test-Path $agent)) {
    $total = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Write-Host ""
    Write-Host "Done: $exe" -ForegroundColor Green
    Write-Host "Agent: $agent" -ForegroundColor Green
    Write-Host "($total MB in dist\Cooldown\)" -ForegroundColor Green
} else {
    Write-Host "Publish failed" -ForegroundColor Red
    exit 1
}
