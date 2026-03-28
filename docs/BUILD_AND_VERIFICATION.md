# Build and Release Verification

This document explains how every release binary is built, how to reproduce a build, and how to verify a downloaded binary independently.

---

## Automated Build Pipeline

Every release is produced by the [GitHub Actions CI/CD pipeline](https://github.com/DistTopic/assets-and-map-editor/actions/workflows/release.yml). The pipeline:

1. **Triggers** on a `v*` tag push (e.g., `v1.0.0-preview`).
2. **Runs six parallel jobs** — one per target platform/architecture.
3. **Builds** a self-contained, single-file executable for each target using the .NET 10 SDK.
4. **Computes SHA-256 checksums** for all artifacts and publishes `checksums.sha256`.
5. **Creates a GitHub Release** with all artifacts and release notes from `CHANGELOG.md`.

The pipeline definition is fully visible in the repository at [`.github/workflows/release.yml`](../.github/workflows/release.yml).

---

## Verifying a Release Artifact

Each release includes a `checksums.sha256` file. Verify your download before running it.

### Linux / macOS

```bash
sha256sum --check checksums.sha256
```

### Windows (PowerShell)

```powershell
$expected = (Get-Content checksums.sha256 | Where-Object { $_ -match "windows-x64" }) -split "\s+" | Select-Object -First 1
$actual = (Get-FileHash .\disttopic-assets-and-map-editor-windows-x64.zip -Algorithm SHA256).Hash
if ($actual -eq $expected.ToUpper()) { "OK" } else { "MISMATCH — do not run this binary" }
```

If the hashes do not match, the file may have been tampered with or corrupted in transit. Do not run it — re-download from the [official Releases page](https://github.com/DistTopic/assets-and-map-editor/releases).

---

## Reproducing the Build Locally

You can reproduce any release build locally:

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### Steps

```bash
# 1. Clone the repository
git clone https://github.com/DistTopic/assets-and-map-editor.git
cd assets-and-map-editor

# 2. Check out the release tag
git checkout v1.0.0-preview

# 3. Restore dependencies
dotnet restore src/App/AssetsAndMapEditor.App.csproj -r win-x64

# 4. Publish a self-contained single-file executable
dotnet publish src/App/AssetsAndMapEditor.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/
```

Replace `win-x64` with your target: `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`.

> Note: Locally built binaries will not have identical byte-for-byte output to the CI builds due to compiler timestamps and path embedding. This is expected. What matters is that the behavior is identical and the source is auditable.

---

## Windows SmartScreen

Windows Defender SmartScreen may display an "Unknown publisher" or "Windows protected your PC" warning for this application. This is a **reputation-based check**, not a malware detection. It appears for applications that do not yet have a sufficient number of downloads or a commercial code-signing certificate.

**This does not mean the application is malicious.** You can verify it independently:

- Check that your download matches the SHA-256 in `checksums.sha256`.
- Review the [CI build log](https://github.com/DistTopic/assets-and-map-editor/actions) for the release — every step is auditable.
- Build it yourself from tagged source code as described above.

To proceed past the SmartScreen prompt: click **More info → Run anyway**.

We are working toward code signing for future releases to eliminate this friction entirely.

---

## macOS Gatekeeper

macOS Gatekeeper may block the app because it is downloaded from the internet and not signed with an Apple Developer certificate. To allow it to run:

```bash
xattr -dr com.apple.quarantine /path/to/AssetsAndMapEditor
```

Then launch it normally.

---

## NuGet Dependency Transparency

All NuGet dependencies are declared in the `.csproj` files and locked by the `dotnet restore` step. You can audit all third-party packages in use:

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.3.12 | Cross-platform UI framework |
| Avalonia.Controls.DataGrid | 11.3.12 | Data grid control |
| Avalonia.Desktop | 11.3.12 | Desktop platform integration |
| Avalonia.Themes.Fluent | 11.3.12 | Fluent design theme |
| Avalonia.Fonts.Inter | 11.3.12 | Inter font family |
| Avalonia.Diagnostics | 11.3.12 | Dev tools (Debug only) |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM source generators |
| Projektanker.Icons.Avalonia.FontAwesome | 9.6.2 | Font Awesome icons |
| SharpCompress | 0.38.0 | Archive/compression support |
