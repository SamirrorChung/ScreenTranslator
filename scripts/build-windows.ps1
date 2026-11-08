$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$windows = Join-Path $root "Windows"
$out = Join-Path $root "dist\windows"
Push-Location $windows
try {
    dotnet publish "ScreenTranslator.App\ScreenTranslator.App.csproj" -c Release -r win-x64 --self-contained false -o $out
}
finally {
    Pop-Location
}
Write-Host "Built $out"
