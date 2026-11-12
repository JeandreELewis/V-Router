# Third-party notices

The release audit currently reports only permissive dependencies: MIT, Apache-2.0 and BSL-1.0. The tagged-build license inventory and CycloneDX SBOM remain the authoritative version-specific records.

| Dependency family | License |
|---|---|
| Avalonia, CommunityToolkit.Mvvm, IxMilia.Dxf, Microsoft.EntityFrameworkCore.Sqlite, Microsoft.NET.Test.Sdk, SkiaSharp | MIT |
| SQLitePCLRaw, Serilog, xUnit | Apache-2.0 |
| Clipper2 | BSL-1.0 |

## SkiaSharp and HarfBuzzSharp

The desktop font adapter uses SkiaSharp and SkiaSharp.HarfBuzz 4.148.0 for installed-font discovery, metrics, shaping support, and native outline extraction. These packages are distributed under their respective MIT-style notices. Package source and license metadata are available from the pinned NuGet packages and their upstream repositories.

## System fonts

System fonts are discovered at runtime and are not copied into this repository or embedded in project files. Projects persist generated geometry so their appearance remains stable if a system font later becomes unavailable.

## Bundled CNC font assets

This source tree does not yet redistribute the proposed OFL font binaries or the complete Hershey/JHF corpus. The current centerline fallback is project-owned geometry code. Before adding any external font asset, add its exact version, source URL, checksum, copyright statement, and complete license text here and in an asset manifest. OFL fonts must retain the required OFL notice; Hershey-derived data must retain its accompanying attribution terms.

Planned audited sources are pinned for the subsequent binary-content import: Google Fonts `ec626514f79f831f1ab848a82114a0ce7e2d6372`, Hershey `6118f267f7758025bb81f8d711ca2d27d8bdf00b`, and Liberation Fonts tag `2.1.5`. These pins do not constitute bundled assets; no family is advertised as bundled until its file, checksum, and notice are present and verified.

## Machine verification warning

Font compatibility findings, calculated cutter width, safety preflight, and 3D V-carve preview are validation aids. They do not guarantee a crash-free cut or replace checking the physical tool, collet, workholding, datum, material, and controller setup.
