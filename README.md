# DupePhotos

DupePhotos is an Avalonia desktop app for finding duplicate images in a selected folder and safely moving selected duplicates to the operating system trash or recycle bin when available.

## Projects

- `src/DupePhotos.Core`: testable duplicate detection engine.
- `src/DupePhotos.App`: cross-platform Avalonia desktop app.
- `tests/DupePhotos.App.Tests`: app ViewModel and file-removal service tests.
- `tests/DupePhotos.Core.Tests`: unit tests for scanning and duplicate detection.

## Duplicate Detection

The scanner uses a staged local-only pipeline:

1. Group by file size for cheap exact-match candidates.
2. SHA-256 file hash for byte-identical duplicates.
3. Normalized decoded-pixel hash for same image data with different file bytes.
4. Perceptual dHash for resized or recompressed visual duplicates.
5. Grayscale verification for perceptual matches to reduce false positives.

## Releases

Tagging a commit with `vX.Y.Z` (e.g. `v0.1.0`) and pushing the tag triggers the
[Release workflow](.github/workflows/release.yml), which runs the test suite,
publishes self-contained single-file builds for Windows, Linux, and macOS
(x64 and arm64), and attaches them to a GitHub release. Prebuilt downloads are
available on the [Releases page](https://github.com/AbdullahAlatyyat/DupePhotos/releases).

## Local Verification

```bash
dotnet test
```

Run the Avalonia app locally:

```bash
dotnet run --project src/DupePhotos.App/DupePhotos.App.csproj
```

## Publish For Windows From Linux

Create a self-contained Windows x64 build:

```bash
dotnet publish src/DupePhotos.App/DupePhotos.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish/win-x64
```

The Windows executable is written to:

```text
artifacts/publish/win-x64/DupePhotos.App.exe
```

To omit debug symbol files from the publish folder:

```bash
dotnet publish src/DupePhotos.App/DupePhotos.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish/win-x64
```
