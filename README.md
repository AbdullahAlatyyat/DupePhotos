# DupePhotos

DupePhotos is a WinUI 3 Windows app for finding duplicate images in a selected folder and safely moving selected duplicates to the Recycle Bin.

## Projects

- `src/DupePhotos.Core`: testable duplicate detection engine.
- `src/DupePhotos.App`: WinUI 3 app packaged as MSIX for Microsoft Store deployment.
- `tests/DupePhotos.Core.Tests`: unit tests for scanning and duplicate detection.

## Duplicate Detection

The scanner uses a staged local-only pipeline:

1. Group by file size for cheap exact-match candidates.
2. SHA-256 file hash for byte-identical duplicates.
3. Normalized decoded-pixel hash for same image data with different file bytes.
4. Perceptual dHash for resized or recompressed visual duplicates.
5. Grayscale verification for perceptual matches to reduce false positives.

## Local Verification

The core library and tests can run cross-platform:

```bash
dotnet test
```

The WinUI project requires Windows because the Windows App SDK invokes a Windows-only XAML compiler.

## Windows Development

On a Windows machine with Visual Studio and the Windows App SDK tooling installed:

```powershell
dotnet restore
dotnet build .\src\DupePhotos.App\DupePhotos.App.csproj -c Release -p:Platform=x64
```

To create a Store-ready MSIX package, open `src/DupePhotos.App` in Visual Studio and use:

`Project > Publish > Create App Packages`

Use the Microsoft Store-associated publisher identity before final submission. The current manifest identity is a development placeholder.
