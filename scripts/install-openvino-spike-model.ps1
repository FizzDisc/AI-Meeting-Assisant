[CmdletBinding()]
param([string]$Repository = 'OpenVINO/whisper-small-fp16-ov')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$python = Join-Path $root 'worker\.venv\Scripts\python.exe'
$target = Join-Path $root 'worker\models\openvino-whisper-small-fp16'
if (-not (Test-Path -LiteralPath $python)) { throw 'AI runtime missing. Run scripts/setup-ai-runtime.ps1 first.' }
& $python (Join-Path $root 'worker\download_openvino_spike_model.py') --repository $Repository --target $target
if ($LASTEXITCODE -ne 0) { throw 'OpenVINO spike model download failed.' }
Write-Host "Experimental OpenVINO model installed at $target"
