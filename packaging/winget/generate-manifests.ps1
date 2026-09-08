<#
    Sinh ba file manifest winget tu chinh file .msi da dung.

        pwsh packaging/winget/generate-manifests.ps1 -Msi artifacts/release/Cowork-1.0.0-win-x64.msi

    Ket qua: artifacts/winget/manifests/m/mtai0524/Cowork/<ver>/*.yaml

    Sinh ra thay vi viet tay vi hai truong bat buoc phai khop tuyet doi voi file da phat hanh:

      · InstallerSha256 -- winget tu choi cai neu bam khong khop.
      · ProductCode     -- WiX sinh moi cho MOI lan build (Package/@ProductCode mac dinh la '*').
                           Chep tay tu lan build truoc thi winget se khong nhan ra ban da cai,
                           `winget upgrade` va `winget uninstall` deu truot.

    UpgradeCode thi co dinh trong Cowork.wxs nen chep thang duoc.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Msi,
    [string]$Owner = 'mtai0524',
    [string]$Package = 'Cowork',
    [string]$Repo = 'https://github.com/mtai0524/cowork-launcher'
)

$ErrorActionPreference = 'Stop'

$msiPath = (Resolve-Path $Msi).Path
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')

# ---------- doc thuoc tinh trong msi ----------
function Get-MsiProperty {
    param([string]$Path, [string[]]$Names)

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $type = $installer.GetType()
    $db = $type.InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Path, 0))

    try {
        $result = @{}
        foreach ($name in $Names) {
            # SQL cua MSI doi ten cot va ten bang dat trong dau huyen. Thieu chung thi cau
            # lenh khong loi, no chi tra ve rong -- kieu that bai im lang de tuong la msi hong.
            $query = "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$name'"

            $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @($query))

            # [void] la bat buoc: InvokeMember cho mot phuong thuc void van nha $null vao
            # pipeline, va ham se tra ve mot mang [null, null, ..., hashtable] thay vi hashtable.
            [void]$view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
            $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)

            if ($null -ne $record) {
                $result[$name] = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
            }

            [void]$view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
        }
        return $result
    }
    finally {
        if ($null -ne $db) { [Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null }
        [Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
    }
}

$props = Get-MsiProperty -Path $msiPath -Names @('ProductVersion', 'ProductCode', 'UpgradeCode', 'ProductName')

$version = $props['ProductVersion']
$productCode = $props['ProductCode']
$upgradeCode = $props['UpgradeCode']
if (-not $version -or -not $productCode) { throw "Khong doc duoc thuoc tinh tu $msiPath" }

$sha = (Get-FileHash $msiPath -Algorithm SHA256).Hash
$fileName = Split-Path $msiPath -Leaf
$url = "$Repo/releases/download/v$version/$fileName"

$identifier = "$Owner.$Package"
$outDir = Join-Path $root "artifacts\winget\manifests\$($Owner.Substring(0,1).ToLowerInvariant())\$Owner\$Package\$version"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "$identifier $version" -ForegroundColor Cyan
Write-Host "  ProductCode : $productCode"
Write-Host "  Sha256      : $sha"
Write-Host "  Url         : $url"

# ---------- version ----------
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json

PackageIdentifier: $identifier
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $outDir "$identifier.yaml") -Encoding utf8

# ---------- locale ----------
# Ngon ngu mac dinh la en-US, khong phai vi-VN: day la kho goi toan cau, va mo ta tieng
# Viet viet trong chinh file .ps1 nay se bi hong dau. Windows PowerShell 5.1 doc script
# theo bang ma ANSI cua he thong, nen "Quan ly" va "Quan ly" khong con phan biet duoc.
# Muon them ban vi-VN thi de van ban ra file mau .yaml rieng va doc bang -Encoding utf8.
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json

PackageIdentifier: $identifier
PackageVersion: $version
PackageLocale: en-US
Publisher: $Owner
PublisherUrl: https://github.com/$Owner
PublisherSupportUrl: $Repo/issues
Author: $Owner
PackageName: $Package
PackageUrl: $Repo
License: Proprietary
Copyright: Copyright (c) $Owner
ShortDescription: Declare the programs you run every day, then launch them by hand or on a schedule
Description: |-
  Cowork keeps the programs you have to run every day in one window: declare each one
  once with its arguments, working directory and environment, then launch it by hand or
  let Cowork run it on a schedule or on a machine event such as waking from sleep.
  It edits those programs' JSON, INI, .env and XML config files in place with backups
  and diffs, keeps services alive, catches hung ones, retries failed jobs, and writes a
  self-describing log for every run. Several machines can report to one web hub. A
  built-in RSS reader gives you the day's news and trending GitHub repos by topic.
Moniker: cowork
Tags:
- automation
- launcher
- rss
- scheduler
- task-runner
- windows
ReleaseNotesUrl: $Repo/releases/tag/v$version
Documentations:
- DocumentLabel: Documentation
  DocumentUrl: $Repo/blob/main/docs/04-huong-dan-su-dung.md
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $outDir "$identifier.locale.en-US.yaml") -Encoding utf8

# ---------- installer ----------
# Scope: user vi goi cai theo nguoi dung (Cowork.wxs dat Scope="perUser") -- khong hoi UAC.
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

PackageIdentifier: $identifier
PackageVersion: $version
MinimumOSVersion: 10.0.17763.0
InstallerType: wix
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '$productCode'
ReleaseDate: $(Get-Date -Format 'yyyy-MM-dd')
AppsAndFeaturesEntries:
- ProductCode: '$productCode'
  UpgradeCode: '$upgradeCode'
  DisplayVersion: $version
  InstallerType: wix
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $outDir "$identifier.installer.yaml") -Encoding utf8

Write-Host ''
Write-Host "manifest: $outDir" -ForegroundColor Green
Get-ChildItem $outDir | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host ''
Write-Host 'Kiem tra cu phap : winget validate --manifest "' -NoNewline; Write-Host $outDir -NoNewline; Write-Host '"'
Write-Host 'Thu cai tai cho   : winget install --manifest "' -NoNewline; Write-Host $outDir -NoNewline; Write-Host '"'
