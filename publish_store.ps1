$ErrorActionPreference = "Stop"

Write-Host "Cleaning..." -ForegroundColor Cyan
dotnet clean

Write-Host "Restoring..." -ForegroundColor Cyan
dotnet restore

Write-Host "Publishing Store Package (MSIX)..." -ForegroundColor Cyan
# This command triggers the MSIX generation due to GenerateAppxPackageOnBuild=true in csproj
# Release configuration with correct RID
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "Check output directory for 'HotCPU.msix' or 'HotCPU.msixbundle'." -ForegroundColor Yellow
Write-Host "Location: bin\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\" -ForegroundColor Yellow
