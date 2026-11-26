<#
.SYNOPSIS
  Builds the MCP Bundle (talk2tia-<version>.mcpb) for one-click install in Claude Desktop, and the matching server.json
  for the official MCP Registry.

.DESCRIPTION
  An .mcpb is a zip with manifest.json at the root (mcpb/manifest.json, version filled in), the icon and the published
  server in server/. Entries use '/' separators, as the format expects. The bundle sets TALK2TIA_UPDATES=off: Claude
  Desktop updates extensions itself, the MSI self-updater is only for MSI installs.

.EXAMPLE
  ./build/mcpb.ps1 -Version 1.1.0 -PublishDir artifacts/publish -OutDir dist
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $PublishDir = 'artifacts/publish',
    [string] $OutDir = 'dist'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = Split-Path $PSScriptRoot -Parent
$publish = (Resolve-Path $PublishDir).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$out = Join-Path (Resolve-Path $OutDir).Path "talk2tia-$Version.mcpb"
if (Test-Path $out) { Remove-Item $out -Force }

$manifest = Get-Content (Join-Path $root 'mcpb/manifest.json') -Raw | ConvertFrom-Json
$manifest.version = $Version
$manifestJson = $manifest | ConvertTo-Json -Depth 10

$zip = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    function Add-Text([string] $name, [string] $text) {
        $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
        $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding $false))
        try { $writer.Write($text) } finally { $writer.Dispose() }
    }

    Add-Text 'manifest.json' $manifestJson
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root 'plugins/talk2tia/.claude-plugin/icon.png'), 'icon.png')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root 'LICENSE'), 'LICENSE')
    Get-ChildItem $publish -Recurse -File | Where-Object Extension -ne '.pdb' | ForEach-Object {
        $relative = $_.FullName.Substring($publish.Length).TrimStart('\', '/').Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, "server/$relative", [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $zip.Dispose()
}

$sha = (Get-FileHash $out -Algorithm SHA256).Hash.ToLowerInvariant()
$server = (Get-Content (Join-Path $root 'mcpb/server.json') -Raw).Replace('${VERSION}', $Version).Replace('${SHA256}', $sha)
[IO.File]::WriteAllText((Join-Path (Split-Path $out) 'server.json'), $server, (New-Object System.Text.UTF8Encoding $false))
Write-Host "MCP bundle: $out ($sha)"
