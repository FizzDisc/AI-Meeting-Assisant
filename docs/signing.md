# Windows release signing

The build supports an Authenticode code-signing certificate with an accessible
private key in the Windows CurrentUser/My or LocalMachine/My store. Obtain a
publicly trusted certificate from a suitable provider and complete its identity
verification first. A self-signed certificate does not establish public trust.
Cloud signing services may require their own signing integration instead.

```powershell
.\scripts\build-release.ps1 -Sign -CertificateThumbprint '<40-character thumbprint>'
```

Optional parameters: `-CertificateStore LocalMachine`, `-SignToolPath` for the
Windows SDK SignTool executable, and `-TimestampUrl` for an RFC 3161 timestamp
service. No private keys or certificate passwords belong in this repository.

The build signs the application executable and application-owned assemblies
before generating the manifest, ZIP and MSI. It then signs the completed MSI
before copying it to the release folder. SHA-256 signatures and timestamps are
required; verification failures stop the build. Vendor binaries are not re-signed.
Without `-Sign`, the existing unsigned development build remains available.

Signing makes the publisher verifiable but does not guarantee immediate
SmartScreen reputation. Publish signed builds under a new version rather than
silently replacing an existing release's installer or checksum.

References:
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
