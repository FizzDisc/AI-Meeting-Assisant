[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [ValidateSet('CurrentUser','LocalMachine')][string]$CertificateStore = 'CurrentUser',
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath
)
$ErrorActionPreference = 'Stop'
$file = (Resolve-Path -LiteralPath $Path).Path
$certificate = Get-Item -LiteralPath "Cert:\$CertificateStore\My\$CertificateThumbprint" -ErrorAction Stop
if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
    throw 'The signing certificate must have an accessible private key and be currently valid.'
}
$usage = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
if (-not ($usage.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })) {
    throw 'The selected certificate is not a code-signing certificate.'
}
if (-not $SignToolPath) {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $SignToolPath = $command.Source }
    else {
        $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $SignToolPath = Get-ChildItem -Path "$sdk\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath)) { throw 'Install the Windows SDK signing tools or supply -SignToolPath.' }
$arguments = @('sign','/sha1',$CertificateThumbprint,'/s','My','/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256')
if ($CertificateStore -eq 'LocalMachine') { $arguments += '/sm' }
& $SignToolPath @arguments $file
if ($LASTEXITCODE) { throw "Signing failed: $file" }
& $SignToolPath verify /pa /all /tw $file
if ($LASTEXITCODE) { throw "Signature verification failed: $file" }
$signature = Get-AuthenticodeSignature -LiteralPath $file
if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
    throw "A valid, timestamped signature from the selected certificate is required: $file"
}
