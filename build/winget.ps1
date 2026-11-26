<#
.SYNOPSIS
  Writes the winget manifests for a Talk2Tia release and optionally submits them to microsoft/winget-pkgs.

.DESCRIPTION
  The MSI is dual-purpose, so the manifest declares two installers with the same file: user scope
  (ALLUSERS=2 MSIINSTALLPERUSER=1, no admin rights) and machine scope (ALLUSERS=1). Users then run
    winget install InsideApp.Talk2Tia [--scope user|machine]
    winget upgrade InsideApp.Talk2Tia

  -Submit uses wingetcreate (downloaded from Microsoft and signature-checked) with a GitHub token that can fork
  microsoft/winget-pkgs and open the pull request (classic PAT with public_repo, in WINGET_TOKEN).

.EXAMPLE
  ./build/winget.ps1 -Version 0.14.0 -Msi installer/bin/x64/Release/Talk2Tia-0.14.0-x64.msi
  ./build/winget.ps1 -Version 0.14.0 -Msi dist/Talk2Tia-0.14.0-x64.msi -Submit
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Msi,
    [string] $OutDir = 'dist/winget',
    [switch] $Submit
)

$ErrorActionPreference = 'Stop'
$id = 'InsideApp.Talk2Tia'
$schema = '1.6.0'
$url = "https://github.com/insideapp-srl/talk2tia/releases/download/v$Version/Talk2Tia-$Version-x64.msi"
$sha = (Get-FileHash $Msi -Algorithm SHA256).Hash.ToUpperInvariant()

$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase((Resolve-Path $Msi).Path, 0)
$view = $db.OpenView("SELECT Value FROM Property WHERE Property = 'ProductCode'")
$view.Execute()
$productCode = $view.Fetch().StringData(1)
$view.Close()
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)

$dir = Join-Path $OutDir "manifests/i/InsideApp/Talk2Tia/$Version"
New-Item -ItemType Directory -Force $dir | Out-Null

function Write-Manifest([string] $name, [string] $type, [string] $body) {
    $header = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$type.$schema.schema.json`n"
    # UTF-8 without BOM, as in microsoft/winget-pkgs.
    [IO.File]::WriteAllText((Join-Path (Resolve-Path $dir) $name), $header + $body.Trim() + "`n", (New-Object Text.UTF8Encoding $false))
}

Write-Manifest "$id.yaml" 'version' @"
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

Write-Manifest "$id.installer.yaml" 'installer' @"
PackageIdentifier: $id
PackageVersion: $Version
Platform:
- Windows.Desktop
MinimumOSVersion: 10.0.17763.0
InstallerType: msi
UpgradeBehavior: install
ProductCode: '$productCode'
Installers:
- Architecture: x64
  Scope: user
  InstallerUrl: $url
  InstallerSha256: $sha
  InstallerSwitches:
    Custom: ALLUSERS=2 MSIINSTALLPERUSER=1
- Architecture: x64
  Scope: machine
  InstallerUrl: $url
  InstallerSha256: $sha
  ElevationRequirement: elevatesSelf
  InstallerSwitches:
    Custom: ALLUSERS=1
ManifestType: installer
ManifestVersion: $schema
"@

Write-Manifest "$id.locale.en-US.yaml" 'defaultLocale' @"
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: InsideApp S.r.l.
PublisherUrl: https://www.insideapp.it
PublisherSupportUrl: https://github.com/insideapp-srl/talk2tia/issues
PackageName: Talk2Tia
PackageUrl: https://talk2tia.insideapp.it
License: MIT
LicenseUrl: https://github.com/insideapp-srl/talk2tia/blob/main/LICENSE
Copyright: Copyright (c) 2026 InsideApp S.r.l.
ShortDescription: MCP server that lets AI assistants such as Claude work with Siemens TIA Portal V21 through Openness.
Description: |-
  Talk2Tia is an open-source Model Context Protocol server for Siemens TIA Portal V21. AI assistants such as Claude
  read and edit SCL and LAD/FBD blocks, data types, tags and libraries, compile, compare and read diagnostics in the
  project open in TIA Portal. Requires TIA Portal V21 with the Openness option.
Moniker: talk2tia
Tags:
- tia-portal
- siemens
- plc
- mcp
- claude
- automation
ReleaseNotesUrl: https://github.com/insideapp-srl/talk2tia/releases/tag/v$Version
Documentations:
- DocumentLabel: Getting started
  DocumentUrl: https://talk2tia.insideapp.it/docs/
ManifestType: defaultLocale
ManifestVersion: $schema
"@

Write-Host "winget manifests: $dir"

if ($Submit) {
    if (-not $env:WINGET_TOKEN) { throw 'WINGET_TOKEN is not set.' }
    $tool = Join-Path $env:TEMP 'wingetcreate.exe'
    Invoke-WebRequest 'https://aka.ms/wingetcreate/latest' -OutFile $tool -UseBasicParsing
    $signature = Get-AuthenticodeSignature $tool
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw "Downloaded wingetcreate.exe is not validly signed by Microsoft ($($signature.Status))."
    }
    & $tool submit --token $env:WINGET_TOKEN --prtitle "New version: $id version $Version" $dir
    if ($LASTEXITCODE -ne 0) { throw "wingetcreate submit failed (exit $LASTEXITCODE)." }
}
