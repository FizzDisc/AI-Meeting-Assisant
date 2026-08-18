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

& $runtimePython -m pip install "whisperx==3.8.6" "truststore==0.10.4"
if ($LASTEXITCODE -ne 0) { throw 'Installing the WhisperX runtime failed.' }

Write-Host "AI runtime installed at $virtualEnvironment"
Write-Host 'No speech or diarization model weights were downloaded.'
