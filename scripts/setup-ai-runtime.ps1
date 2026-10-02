[CmdletBinding()]
param([string]$PythonCommand = 'python')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workerDirectory = Join-Path $repositoryRoot 'worker'
$virtualEnvironment = Join-Path $workerDirectory '.venv'
$runtimePython = Join-Path $virtualEnvironment 'Scripts\python.exe'

Write-Host "Creating isolated AI runtime with $PythonCommand..."
& $PythonCommand -m venv $virtualEnvironment
if ($LASTEXITCODE -ne 0) { throw 'Creating the AI virtual environment failed.' }

& $runtimePython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'Updating pip failed.' }

& $runtimePython -m pip install "torch==2.8.0+cpu" "torchaudio==2.8.0+cpu" "torchvision==0.23.0+cpu" --index-url https://download.pytorch.org/whl/cpu
if ($LASTEXITCODE -ne 0) { throw 'Installing the CPU engine failed.' }
& $runtimePython -m pip install -r (Join-Path $workerDirectory 'runtime-requirements-win-x64.txt')
if ($LASTEXITCODE -ne 0) { throw 'Installing the local AI runtime failed.' }

Write-Host "AI runtime installed at $virtualEnvironment"
Write-Host 'No speech or diarization model weights were downloaded.'
