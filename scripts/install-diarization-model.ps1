$ErrorActionPreference='Stop';$root=Split-Path -Parent $PSScriptRoot;$python=Join-Path $root 'worker\.venv\Scripts\python.exe'
if(-not(Test-Path -LiteralPath $python)){throw 'AI runtime missing. Run scripts/setup-ai-runtime.ps1 first.'}
$target=Join-Path $root 'worker\models\speaker-diarization-community-1'
$secureToken=Read-Host 'Hugging Face read token' -AsSecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
try{$token=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer);if($token-notmatch '^hf_[A-Za-z0-9]{20,}$'){throw 'No complete Hugging Face token was entered. Create a new Read token and copy the full value shown once at creation.'};$token | & $python (Join-Path $root 'worker\download_diarization_model.py') --token-stdin --target $target}
finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer);$token=$null}
if($LASTEXITCODE-ne 0){throw 'Installation failed. Verify license acceptance and token access.'}
Write-Host "Offline diarization model installed at $target"
