[CmdletBinding()]
param([Parameter(Mandatory = $true)][string[]]$Path)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$certificatePath = $env:AIPET_SIGN_CERT_PATH
$timestampUrl = if ([string]::IsNullOrWhiteSpace($env:AIPET_SIGN_TIMESTAMP_URL)) { 'http://timestamp.digicert.com' } else { $env:AIPET_SIGN_TIMESTAMP_URL }
if ([string]::IsNullOrWhiteSpace($certificatePath)) {
    Write-Output '[SKIP] AIPET_SIGN_CERT_PATH is not configured; artifacts remain unsigned.'
    exit 2
}
if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) { throw 'The configured signing certificate was not found.' }

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) { throw 'signtool.exe was not found. Install the Windows SDK or add it to PATH.' }
$arguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $timestampUrl, '/f', $certificatePath)
if (-not [string]::IsNullOrWhiteSpace($env:AIPET_SIGN_CERT_PASSWORD)) { $arguments += @('/p', $env:AIPET_SIGN_CERT_PASSWORD) }
$arguments += $Path
& $signtool.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
foreach ($target in $Path) {
    & $signtool.Source verify /pa /all $target
    if ($LASTEXITCODE -ne 0) { throw "signature verification failed: $target" }
}
