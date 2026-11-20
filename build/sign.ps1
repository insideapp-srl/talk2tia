<#
.SYNOPSIS
  Signs files with the InsideApp code-signing certificate (Certum, cloud HSM via SimplySign).

.DESCRIPTION
  The private key never leaves Certum's cloud HSM. SimplySign Desktop must be running and logged in
  (email + OTP from the SimplySign mobile app) on the machine that signs: it exposes the certificate
  in the Windows certificate store, where signtool selects it by SHA-1 thumbprint.

  If signtool is not installed (Windows SDK), the script downloads it from the official
  Microsoft.Windows.SDK.BuildTools NuGet package into a local cache.

.PARAMETER Path
  Files to sign.
.PARAMETER Thumbprint
  SHA-1 thumbprint of the certificate. Default: $env:SIGNING_CERT_SHA1, then the InsideApp certificate.
.PARAMETER Check
  Only verify that the certificate (with its private key) and signtool are available.

.EXAMPLE
  ./build/sign.ps1 -Check
  ./build/sign.ps1 -Path artifacts/publish/talk2tia.exe, artifacts/publish/Talk2Tia.Core.dll
#>
[CmdletBinding()]
param(
    [string[]] $Path,
    [string] $Thumbprint = $(if ($env:SIGNING_CERT_SHA1) { $env:SIGNING_CERT_SHA1 } else { '53D5FBADE305B0A3A7FB4D74CBBFCFCDC2735676' }),
    [string] $TimestampUrl = 'http://time.certum.pl',
    [string] $Description = 'Talk2Tia',
    [string] $DescriptionUrl = 'https://github.com/insideapp-srl/talk2tia',
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
$Thumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant()

function Find-Certificate {
    foreach ($store in 'Cert:\CurrentUser\My', 'Cert:\LocalMachine\My') {
        $cert = Get-ChildItem $store -ErrorAction SilentlyContinue | Where-Object Thumbprint -eq $Thumbprint | Select-Object -First 1
        if ($cert) { return $cert }
    }
    return $null
}

function Get-SignTool {
    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $sdkRoot) {
        $sdk = Get-ChildItem $sdkRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object FullName -match '\\x64\\' | Sort-Object FullName -Descending | Select-Object -First 1
        if ($sdk) { return $sdk.FullName }
    }

    $cache = Join-Path $env:LOCALAPPDATA 'Talk2Tia\tools\sdk-buildtools'
    $cached = Get-ChildItem $cache -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object FullName -match '\\x64\\' | Select-Object -First 1
    if ($cached) { return $cached.FullName }

    Write-Host 'signtool not found: downloading Microsoft.Windows.SDK.BuildTools from nuget.org...'
    $index = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/index.json' -UseBasicParsing
    $version = $index.versions | Where-Object { $_ -notmatch '-' } | Select-Object -Last 1
    $package = Join-Path $env:TEMP "sdk-buildtools-$version.zip"
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$version/microsoft.windows.sdk.buildtools.$version.nupkg" -OutFile $package -UseBasicParsing
    New-Item -ItemType Directory -Force $cache | Out-Null
    Expand-Archive $package -DestinationPath $cache -Force
    Remove-Item $package -Force

    $tool = Get-ChildItem $cache -Recurse -Filter signtool.exe | Where-Object FullName -match '\\x64\\' | Select-Object -First 1
    if (-not $tool) { throw 'signtool.exe not found in Microsoft.Windows.SDK.BuildTools.' }
    $signature = Get-AuthenticodeSignature $tool.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw "Downloaded signtool.exe is not validly signed by Microsoft ($($signature.Status))."
    }
    return $tool.FullName
}

$cert = Find-Certificate
if (-not $cert) {
    throw "Code-signing certificate $Thumbprint not found. Start SimplySign Desktop on this machine and log in " +
          "with your SimplySign e-mail and the OTP from the mobile app, then run again."
}
if (-not $cert.HasPrivateKey) {
    throw "Certificate $Thumbprint is present but its private key is not available. Is SimplySign Desktop connected?"
}
if ($cert.NotAfter -lt (Get-Date)) {
    throw "Certificate $Thumbprint expired on $($cert.NotAfter)."
}

$signtool = Get-SignTool
Write-Host "Certificate: $($cert.Subject) (valid until $($cert.NotAfter.ToString('yyyy-MM-dd')))"
Write-Host "signtool:    $signtool"

if ($Check) {
    Write-Host 'Signing prerequisites OK.'
    return
}

if (-not $Path -or $Path.Count -eq 0) { throw 'Nothing to sign: pass -Path.' }

foreach ($file in $Path) {
    $full = (Resolve-Path $file).Path
    $signed = $false
    # The timestamp server can be briefly unavailable: retry a few times.
    for ($attempt = 1; $attempt -le 3 -and -not $signed; $attempt++) {
        & $signtool sign /sha1 $Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 /d $Description /du $DescriptionUrl $full
        if ($LASTEXITCODE -eq 0) {
            $signed = $true
        }
        elseif ($attempt -lt 3) {
            Write-Warning "signtool failed for $full (exit $LASTEXITCODE), retrying in 10 s..."
            Start-Sleep -Seconds 10
        }
    }
    if (-not $signed) { throw "signtool failed for $full after 3 attempts." }

    & $signtool verify /pa /q $full
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed for $full." }
    Write-Host "Signed: $full"
}
