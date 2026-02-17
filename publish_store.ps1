$ErrorActionPreference = "Stop"

$msbuild = (Get-Command msbuild -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
if (-not $msbuild) {
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\17\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $msbuild) {
    throw "MSBuild.exe not found. Install Visual Studio/Build Tools with Desktop development + MSIX packaging."
}

function Invoke-ExternalCommand {
    param (
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $false)][string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

Write-Host "Using MSBuild: $msbuild" -ForegroundColor DarkGray
Write-Host "Cleaning Store package project..." -ForegroundColor Cyan
Invoke-ExternalCommand -FilePath $msbuild -Arguments @(
    "HotCPU.Package\hotcpuPublish.wapproj",
    "/t:Clean",
    "/p:Configuration=Release",
    "/p:Platform=x64"
)

Write-Host "Publishing Store Package (MSIX Upload)..." -ForegroundColor Cyan
Invoke-ExternalCommand -FilePath $msbuild -Arguments @(
    "HotCPU.Package\hotcpuPublish.wapproj",
    "/restore",
    "/t:Build",
    "/p:Configuration=Release",
    "/p:Platform=x64",
    "/p:GenerateAppxPackageOnBuild=true",
    "/p:UapAppxPackageBuildMode=StoreUpload",
    "/p:AppxPackageSigningEnabled=false",
    "/p:AppxBundle=Always"
)

Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "Location: HotCPU.Package\AppPackages\" -ForegroundColor Yellow
