[CmdletBinding()]param(
    [switch]$SkipTests,
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [ValidateSet('CurrentUser','LocalMachine')][string]$CertificateStore = 'CurrentUser',
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath
)
if($Sign -and $CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$'){throw 'Signing requires -CertificateThumbprint for a trusted code-signing certificate.'}
if(-not $Sign -and $CertificateThumbprint){throw 'Specify -Sign when supplying a signing certificate.'}
function Sign-ReleaseFile([string]$FilePath) {
    & (Join-Path $PSScriptRoot 'sign-release-file.ps1') -Path $FilePath -CertificateThumbprint $CertificateThumbprint -CertificateStore $CertificateStore -TimestampUrl $TimestampUrl -SignToolPath $SignToolPath
}
$ErrorActionPreference='Stop';$root=Split-Path -Parent $PSScriptRoot;$release=Join-Path $root 'artifacts\release';$publish=Join-Path $release 'publish';$package=Join-Path $release 'package';New-Item -ItemType Directory -Force $release,$publish,$package|Out-Null
[xml]$versionProperties=Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version=[string]$versionProperties.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Directory.Build.props must define a numeric three-part release version.'}
if(-not $SkipTests){dotnet build (Join-Path $root 'AI-Meeting-Assistant.sln') -c Release;if($LASTEXITCODE){throw 'Release build failed.'};dotnet run --project (Join-Path $root 'tests\AiMeetingAssistant.Core.Tests') -c Release;if($LASTEXITCODE){throw 'Core tests failed.'};dotnet run --project (Join-Path $root 'tests\AiMeetingAssistant.Windows.SmokeTests') -c Release;if($LASTEXITCODE){throw 'Windows tests failed.'}}
dotnet publish (Join-Path $root 'src\AiMeetingAssistant.Desktop') -c Release -r win-x64 --self-contained true -p:PublishProfile=win-x64 -o $publish;if($LASTEXITCODE){throw 'Publishing failed.'}
foreach($name in @('.venv','models','.torch-xpu-spike','.openvino-spike')){if(Test-Path (Join-Path $publish "worker\$name")){throw "Forbidden development payload: $name"}}
if($Sign){
    Sign-ReleaseFile (Join-Path $publish 'AiMeetingAssistant.exe')
    Get-ChildItem -LiteralPath $publish -Filter 'AiMeetingAssistant*.dll' -File | ForEach-Object { Sign-ReleaseFile $_.FullName }
}
Get-ChildItem $publish -Recurse -File|ForEach-Object{[PSCustomObject]@{Path=$_.FullName.Substring($publish.Length).TrimStart('\');Bytes=$_.Length;Sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}}|Sort-Object Path|ConvertTo-Json -Depth 3|Set-Content (Join-Path $release 'release-manifest.json') -Encoding utf8
$zip=Join-Path $package "AI-Meeting-Assistant-$version-win-x64.zip";if(Test-Path $zip){Remove-Item -LiteralPath $zip};Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
dotnet build (Join-Path $root 'installer\AiMeetingAssistant.Installer.wixproj') -c Release -p:PublishDir=$publish;if($LASTEXITCODE){throw 'MSI build failed.'};if($Sign){Sign-ReleaseFile (Join-Path $root "installer\bin\Release\AI-Meeting-Assistant-$version-win-x64.msi")};Copy-Item (Join-Path $root "installer\bin\Release\AI-Meeting-Assistant-$version-win-x64.msi") $package -Force;Get-ChildItem $package|Select Name,Length
