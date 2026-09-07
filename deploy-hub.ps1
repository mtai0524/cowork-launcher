# Publish Cowork.Hub len MonsterASP qua WebDeploy.
#
# Config that (mat khau web + token cac may) nam o artifacts/hub-config/appsettings.json,
# khong commit vao git. Phai chep de len SAU khi publish, vi `dotnet publish` luon ghi
# ban appsettings.json mac dinh trong source ra thu muc output.

$ErrorActionPreference = 'Stop'

$root      = $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\hub-publish'
$configFile = Join-Path $root 'artifacts\hub-config\appsettings.json'
$msdeploy   = 'C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe'

if (-not (Test-Path $configFile)) { throw "Thieu $configFile" }
if (-not (Test-Path $msdeploy))   { throw "Chua cai Web Deploy: winget install --id Microsoft.WebDeploy" }

dotnet publish (Join-Path $root 'src\Cowork.Hub\Cowork.Hub.csproj') -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'publish that bai' }

Copy-Item $configFile (Join-Path $publishDir 'appsettings.json') -Force

# -skip App_Data: danh sach may cap tren web nam o do. `-verb:sync` xoa moi thu khong co
# trong thu muc nguon, nen khong skip thi moi lan deploy se xoa sach may da cap token.
& $msdeploy -verb:sync `
  -source:contentPath=$publishDir `
  -dest:contentPath='site89923',computerName='https://site89923.siteasp.net:8172/msdeploy.axd?site=site89923',userName='site89923',password=$env:COWORK_HUB_PASSWORD,authType='Basic' `
  -skip:Directory="App_Data" `
  -enableRule:AppOffline -allowUntrusted -retryAttempts:3
if ($LASTEXITCODE -ne 0) { throw 'deploy that bai' }

Write-Output 'Xong: http://cowork-hub.runasp.net/'
