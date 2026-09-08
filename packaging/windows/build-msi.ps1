<#
    Dung file .msi cai Cowork tren Windows.

        pwsh packaging/windows/build-msi.ps1

    Ket qua: artifacts/release/Cowork-<ver>-win-x64.msi

    Ban publish la SELF-CONTAINED (~163 MB, nen lai con ~65 MB trong msi). Doi lai la may
    dich khong can cai san .NET Desktop Runtime: winget xu ly PackageDependencies khong
    dong deu giua cac phien ban client, ma thieu runtime thi app chet bang mot hop thoai
    kho hieu -- sai kieu loi cho mot lenh cai "chay la xong".
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$publishDir = Join-Path $root "artifacts\msi-publish"
$outputDir = Join-Path $root 'artifacts\release'

# Mot nguon su that duy nhat cho so phien ban: Directory.Build.props.
$version = (dotnet msbuild (Join-Path $root 'src\Cowork.App\Cowork.App.csproj') -getProperty:Version -nologo).Trim()
if (-not ($version -match '^\d+\.\d+\.\d+')) { throw "Khong doc duoc Version: '$version'" }

Write-Host "Cowork $version -> $Runtime" -ForegroundColor Cyan

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

dotnet publish (Join-Path $root 'src\Cowork.App\Cowork.App.csproj') `
    -c $Configuration -r $Runtime --self-contained true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish that bai' }

# WiX v5 la dotnet tool: `dotnet tool install --global wix`.
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw 'Chua co WiX: dotnet tool install --global wix'
}

# Ext Util can cho <util:CloseApplication> -- dong Cowork dang chay truoc khi nang cap.
#
# Phai ghim dung phien ban cua chinh wix: `wix extension add` khong ghim thi no keo ban
# moi nhat (v6), va v6 khong nap duoc trong v5 -- loi WIX6101 "Could not find expected
# package root folder wixext5".
$wixVersion = (wix --version).Split('+')[0].Trim()
$hasUtil = (wix extension list --global 2>&1) -match "WixToolset.Util.wixext $([regex]::Escape($wixVersion))"
if (-not $hasUtil) {
    Write-Host "Them WixToolset.Util.wixext/$wixVersion..." -ForegroundColor DarkGray
    wix extension add --global "WixToolset.Util.wixext/$wixVersion"
    if ($LASTEXITCODE -ne 0) { throw "khong them duoc WixToolset.Util.wixext/$wixVersion" }
}

$msi = Join-Path $outputDir "Cowork-$version-$Runtime.msi"

wix build (Join-Path $PSScriptRoot 'Cowork.wxs') `
    -ext WixToolset.Util.wixext `
    -arch x64 `
    -define "Version=$version" `
    -bindpath "Publish=$publishDir" `
    -out $msi
if ($LASTEXITCODE -ne 0) { throw 'wix build that bai' }

$size = [math]::Round((Get-Item $msi).Length / 1MB, 1)
$sha = (Get-FileHash $msi -Algorithm SHA256).Hash

Write-Host ''
Write-Host "msi    : $msi ($size MB)" -ForegroundColor Green
Write-Host "sha256 : $sha" -ForegroundColor Green
Write-Host ''
Write-Host 'Thu cai: msiexec /i "' -NoNewline; Write-Host $msi -NoNewline; Write-Host '" /qn'
Write-Host 'Go     : msiexec /x "' -NoNewline; Write-Host $msi -NoNewline; Write-Host '" /qn'
