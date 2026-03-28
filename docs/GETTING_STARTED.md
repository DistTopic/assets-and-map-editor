# Getting Started

This guide will walk you through downloading, verifying, and using **Assets And Map Editor** for the first time.

---

## Prerequisites

No runtime installation is required. Each release is a self-contained, single-file executable that bundles the .NET runtime.

> If you prefer to build from source, see the [Build from Source](#building-from-source) section below.

---

## Downloading a Release

1. Go to the [Releases page](https://github.com/DistTopic/assets-and-map-editor/releases).
2. Under the latest release, download the archive for your platform:

| Platform | File |
|----------|------|
| Windows x64 | `disttopic-assets-and-map-editor-windows-x64.zip` |
| Windows ARM64 | `disttopic-assets-and-map-editor-windows-arm64.zip` |
| macOS Apple Silicon | `disttopic-assets-and-map-editor-macos-arm64.zip` |
| macOS Intel | `disttopic-assets-and-map-editor-macos-x64.zip` |
| Linux x64 | `disttopic-assets-and-map-editor-linux-x64.zip` |
| Linux ARM64 | `disttopic-assets-and-map-editor-linux-arm64.zip` |

3. Also download `checksums.sha256` from the same release.

---

## Verifying the Download

Before running the application, verify the integrity of your download.

**Linux / macOS:**
```bash
sha256sum --check checksums.sha256
```

**Windows (PowerShell):**
```powershell
(Get-FileHash .\disttopic-assets-and-map-editor-windows-x64.zip -Algorithm SHA256).Hash
```

Compare the output against the matching line in `checksums.sha256`. If the hashes don't match, do not run the binary — re-download from the official releases page.

---

## Running the Application

### Windows

1. Extract the zip archive.
2. Run `AssetsAndMapEditor.exe`.
3. If Windows SmartScreen shows a warning ("Windows protected your PC"), click **More info → Run anyway**. This warning appears because the binary is not yet commercially code-signed. You can verify it was built from source through the [CI pipeline](https://github.com/DistTopic/assets-and-map-editor/actions). See [SECURITY.md](../SECURITY.md) for details.

### macOS

1. Extract the zip archive.
2. If Gatekeeper blocks it, run the following in Terminal once:
   ```bash
   xattr -dr com.apple.quarantine AssetsAndMapEditor
   ```
3. Launch `AssetsAndMapEditor`.

### Linux

1. Extract the archive.
2. Make it executable:
   ```bash
   chmod +x AssetsAndMapEditor
   ```
3. Run it:
   ```bash
   ./AssetsAndMapEditor
   ```

---

## Opening Your First Files

On first launch, you will see the main window with an empty workspace. To start:

1. **Open an OTB file** — use *File → Open OTB* or drag and drop a `.otb` file.
2. **Open a DAT + SPR pair** — load the Tibia client data files via *File → Open Client Files*.
3. **Open a Map** — load an OTBM file via *File → Open Map*.

You can open multiple sessions simultaneously and use the **Split View** to compare or transfer between them.

---

## Session Persistence

The application saves your session state automatically on close (open file paths, viewport position, window layout). When you reopen the app, your previous session is restored.

---

## Building from Source

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Steps

```bash
git clone https://github.com/DistTopic/assets-and-map-editor.git
cd assets-and-map-editor
dotnet run --project src/App/AssetsAndMapEditor.App.csproj
```

To produce a self-contained release binary:

```bash
dotnet publish src/App/AssetsAndMapEditor.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/
```

Replace `win-x64` with your target runtime identifier: `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, or `linux-arm64`.

---

## Next Steps

- [Architecture](ARCHITECTURE.md) — understand how the project is structured
- [Contributing](../CONTRIBUTING.md) — learn how to contribute
- [Changelog](../CHANGELOG.md) — see what changed in each release
