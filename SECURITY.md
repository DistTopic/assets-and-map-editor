# Security Policy

## Supported Versions

| Version          | Supported          |
|------------------|--------------------|
| Latest release   | :white_check_mark: |
| Older releases   | :x:                |

Only the most recent release receives security updates. We recommend always running the latest version.

## Reporting a Vulnerability

**Do not open a public issue for security vulnerabilities.**

If you discover a security vulnerability in this project, please report it responsibly via [GitHub Security Advisories](https://github.com/DistTopic/assets-and-map-editor/security/advisories/new).

Your report should include:

- A clear description of the vulnerability.
- Precise steps to reproduce the issue.
- The potential impact or attack scenario.
- Any suggested mitigation or fix, if applicable.

## Response Process

| Stage | Target |
|-------|--------|
| Acknowledgement | Within 72 hours |
| Initial assessment | Within 7 days |
| Fix or mitigation | Within 30 days of confirmation |
| Public disclosure | After fix is released, with reporter credit (unless anonymity is requested) |

## Scope

This policy covers the **Assets And Map Editor** source code and its official release binaries.

It does **not** cover:

- Third-party NuGet dependencies — report those to their respective maintainers.
- Game server software, custom Tibia clients, or community modifications.
- User-generated configuration files or game assets.

## Verifying Release Integrity

Every official release published on the [Releases page](https://github.com/DistTopic/assets-and-map-editor/releases) includes a `checksums.sha256` file containing SHA-256 hashes for all build artifacts.

**Verify on Linux / macOS:**
```bash
sha256sum --check checksums.sha256
```

**Verify on Windows (PowerShell):**
```powershell
Get-FileHash disttopic-assets-and-map-editor-windows-x64.zip -Algorithm SHA256
```

Compare the output hash against the corresponding entry in `checksums.sha256`. Do **not** run a binary whose hash does not match.

## Windows SmartScreen Warning

Because our releases are not yet commercially code-signed, Windows Defender SmartScreen may show an "Unknown publisher" or "unrecognized app" warning. This is a reputation-based check, not a detection of malicious code.

To verify authenticity independently:

1. Download **only** from the [official GitHub Releases](https://github.com/DistTopic/assets-and-map-editor/releases) page.
2. Verify the SHA-256 checksum as described above.
3. Review the [CI/CD pipeline](https://github.com/DistTopic/assets-and-map-editor/actions) — every release binary is built directly from source in a transparent, auditable GitHub Actions workflow.
4. Optionally, build from source yourself following the [build instructions](../README.md#building-from-source).

We are actively working toward code signing for future releases to eliminate this friction.

## Best Practices for Users

- Download releases only from the [official GitHub Releases](https://github.com/DistTopic/assets-and-map-editor/releases) page.
- Always verify the SHA-256 checksum before running a downloaded binary.
- Keep your installation up to date.
- Do not run the application with elevated privileges (it does not require them).

---

Thank you for helping keep this project secure.
