[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$python = Join-Path $root 'worker\.venv\Scripts\python.exe'
$target = Join-Path $root 'worker\.openvino-spike'
if (-not (Test-Path -LiteralPath $python)) { throw 'AI runtime missing. Run scripts/setup-ai-runtime.ps1 first.' }
& $python -m pip install --target $target --no-deps 'openvino==2026.3.0' 'openvino-tokenizers==2026.3.0.0' 'openvino-genai==2026.3.0' 'optimum==2.3.0' 'optimum-intel==2.1.0' 'nncf==3.3.0' 'tabulate==0.10.0' 'pydot==3.0.4' 'openvino-telemetry==2025.2.0' 'ninja==1.13.0' 'psutil==7.2.2'
if ($LASTEXITCODE -ne 0) { throw 'OpenVINO spike runtime installation failed.' }
& $python -c "import sys;sys.path.insert(0,r'$target');import openvino as ov;c=ov.Core();print('OpenVINO devices:', ', '.join(c.available_devices))"
if ($LASTEXITCODE -ne 0) { throw 'OpenVINO device discovery failed.' }
Write-Host 'Experimental runtime installed separately; the production worker was not modified.'
