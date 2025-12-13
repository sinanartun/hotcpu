$ErrorActionPreference = "Stop"

Write-Host "Cleaning..." -ForegroundColor Cyan
dotnet clean

Write-Host "Restoring..." -ForegroundColor Cyan
dotnet restore

Write-Host "Publishing Release (win-x64)..." -ForegroundColor Cyan
# Publishes as a single file executable for easier testing
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "Output: bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\HotCPU.exe" -ForegroundColor Yellow
