$ErrorActionPreference = "Stop"

Write-Host "Cleaning..." -ForegroundColor Cyan
dotnet clean

Write-Host "Restoring..." -ForegroundColor Cyan
dotnet restore

Write-Host "Publishing Store Package (MSIX)..." -ForegroundColor Cyan
# MSIX usually prefers self-contained
dotnet publish -c Release -r win-x64 --self-contained true -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=StoreUpload -p:AppxPackageSigningEnabled=false -p:AppxBundle=Always

Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "Location: bin\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\" -ForegroundColor Yellow
