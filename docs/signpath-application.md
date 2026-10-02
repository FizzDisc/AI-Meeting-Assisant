# SignPath application preparation

Status: draft; not submitted.

- Project: AI Meeting Assistant
- Repository: https://github.com/FizzDisc/AI-Meeting-Assisant
- Maintainer and proposed signing approver: FizzDisc
- Platform: Windows x64; .NET desktop application, WiX MSI installer
- Purpose: local audio/screen recording and local AI transcription
- Existing release: v1.0.8 (unsigned)
- Policy: docs/code-signing-policy.md
- Privacy: docs/privacy.md
- Build: .github/workflows/release-build.yml (unsigned artifact with source commit)

## Required before submission

1. Project license: MIT, selected by the owner; LICENSE and README notice added.
2. Review licenses/notices for bundled .NET/NuGet dependencies and the separately
   downloaded Python packages, FFmpeg and models; resolve incompatible terms.
3. Commit and push the complete tested source. v1.0.8 was built from a local
   working tree and its tag does not identify all of that source. Use a new
   version and tag for the first verified release; keep v1.0.8 history intact.
4. Run the workflow on GitHub and inspect its artifact and tests.
5. Owner confirms MFA, contact email and agreement to Foundation conditions.
6. Apply at https://signpath.org/apply and await review. Admission is not guaranteed.

After acceptance: install/configure the SignPath GitHub integration, configure
artifact matching and product/version restrictions, store credentials as GitHub
secrets, enable manual release signing approval, verify a signed build, then
publish a new signed release and update the README installer link.

The local Windows certificate-store script cannot use Foundation signing without
this separate integration. No SignPath credentials or organization/project IDs
have been supplied yet.

## Verification on this workstation

Release solution build succeeded without warnings; 47 Core tests, Windows
cleanup/runtime/system-only regressions and 5 model-management tests passed.
The complete Python suite has 48 passing tests and two environment errors
(missing NumPy and FFmpeg); it has not fully passed here.
Bundled ScreenRecorderLib and .NET license notices are included under licenses/.
The separately downloaded runtime/model dependency license review remains open.
