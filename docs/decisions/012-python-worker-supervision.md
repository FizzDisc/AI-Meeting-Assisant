# ADR 012: Python worker supervision

- Status: Accepted for Sprint 2.1
- Date: 2026-08-18

## Decision

Run the local AI worker as one hidden child process supervised by the .NET desktop app. Package its Python entry script with desktop build output. Communicate with newline-delimited JSON over redirected stdin/stdout using protocol 1.0 and unique request IDs.

Serialize requests initially, apply a ten-second response timeout, validate protocol/request correlation and keep a bounded tail of stderr diagnostics. Graceful app shutdown closes worker stdin and waits briefly before killing the process tree.

## Runtime policy

WhisperX/pyannote support Python 3.11 or 3.12 for this product baseline. Health negotiation includes Python and worker versions, a runtime-support flag and explicit capabilities. An unsupported Python may prove the transport but must not start ML jobs.

## Deferred

Sprint 2.1 does not install Python, create a virtual environment, download models or import torch/WhisperX. Environment provisioning and transcription are separate slices so installation failures cannot be confused with protocol/process failures.
