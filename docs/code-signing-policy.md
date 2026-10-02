# Code signing policy

## Current status

Releases are currently unsigned. SignPath Foundation support is planned, but no
application has been accepted and no SignPath signing integration is active.
The existing v1.0.8 installer must not be represented as signed.

## Responsibilities

The repository owner [FizzDisc](https://github.com/FizzDisc) is the designated
committer, reviewer and release/signing approver. External contributions require
review. Signing requires explicit approval of each release by the maintainer.
Multi-factor authentication on GitHub and SignPath must be enabled before use;
its current account status has not been verified.

## Release requirements

Build the complete committed source using the GitHub Actions release workflow.
The release tag must match Directory.Build.props. Keep the source commit, workflow
run and final package SHA-256 hashes with each release. Do not replace historical
release assets or move an existing release tag to disguise a source mismatch.

Sign only the application-owned executables and assemblies and the final MSI.
Do not re-sign third-party binaries. Check signatures and timestamps before
publication; calculate final checksums after signing. Failed signing or
verification must prevent publication of a supposedly signed release.

[Privacy and downloads](privacy.md) describes local processing and third-party
network services. Third-party libraries and models retain their own licenses.

## SignPath activation

Before activation, finish the project license and dependency-license review,
synchronize the complete source, confirm MFA, apply, and configure the approved
SignPath project and GitHub integration. The local certificate-store signing
script is a separate mechanism and does not establish SignPath integration.

After acceptance and successful integration, add the following attribution to
this policy and the release/download pages as an active-service statement:

> Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Until then this quotation is only the planned attribution, not a claim of service.

References: [Foundation conditions](https://signpath.org/terms.html),
[GitHub integration](https://docs.signpath.io/trusted-build-systems/github).
