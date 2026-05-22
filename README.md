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

## Local Verification

```bash
dotnet test
```

Run the Avalonia app locally:

```bash
dotnet run --project src/DupePhotos.App/DupePhotos.App.csproj
```
