$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimePython = Join-Path $repositoryRoot 'worker\.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $runtimePython)) { throw 'AI runtime missing. Run scripts/setup-ai-runtime.ps1 first.' }
& $runtimePython -m pip install "truststore==0.10.4"
if ($LASTEXITCODE -ne 0) { throw 'Installing Windows certificate-store support failed.' }
& $runtimePython (Join-Path $repositoryRoot 'worker\download_development_model.py')
if ($LASTEXITCODE -ne 0) { throw 'Development model download failed.' }
Write-Host 'Development-only tiny model installed. Production model selection remains a Model Manager concern.'
