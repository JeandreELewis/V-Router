# Production 1.0 release gates

The application reports built-in controller definitions as **certification pending** until parser/simulator evidence and representative air-cut records exist. Being bundled is not the same as being certified.

## Automated gates

- Release builds and tests pass on Windows, Linux and macOS.
- Dependency vulnerability audit has no vulnerable transitive package.
- License inventory is reviewed and compatible with MIT distribution.
- A CycloneDX JSON SBOM is attached to the tagged build.
- Versions 1–8 open in memory as version 9, preserve editable geometry/CAM data, and save/reopen deterministically.
- Cabinet, sign and nested-sheet proof projects pass preflight and deterministic export for all five built-in posts in metric and imperial modes.
- Exact controller golden files for all five posts pass in metric and imperial modes, including arcs, dwell, comments and their supported automatic/manual/separate-file cutter handling.
- Every generated machine file contains exactly one cutter identity, independent of `.nc`, `.txt`, `.tap`, `.gcode` or `.ngc` filename extension.
- The V-Router logo is present in the startup workspace, window chrome and About view, and is embedded in Windows executable/installer/shortcut metadata, the macOS app bundle and Linux desktop packaging.
- There are no unresolved critical or high defects.

## Controller certification evidence

For each of UCCNC, Mach3, Mach4, GRBL and LinuxCNC, attach:

1. controller/parser version and machine profile;
2. metric and imperial parser/simulator results;
3. arc, dwell and tool-change coverage;
4. dated air-cut checklist and operator sign-off;
5. hashes of the exact post definition and proof-job output.

Only after this evidence is reviewed may `PostCertificationPolicy.Certify(...)` promote that immutable definition to `Certified`. The policy rejects mismatched post IDs, missing metric/imperial reports, absent arc/dwell/tool-change coverage, future or missing air-cut dates, missing operator sign-off, malformed SHA-256 values, or missing cabinet/sign/nested proof-output hashes in either unit. `CertificationStatus` cannot be assigned by consumer code.

## Windows production artifacts

The portable Windows x64 directory produced by CI is an unsigned release candidate. Production 1.0 additionally requires:

- a Windows x64 MSI built from the same tested publish directory;
- a single-file `V-Router-Setup.exe` bundle containing that MSI;
- Authenticode signing of the application, MSI, Burn elevation engine and outer installer with the release certificate;
- signature verification and clean-install/uninstall testing on a supported Windows x64 machine;
- SHA-256 hashes for the installer EXE, MSI and portable ZIP.

Linux x64 and macOS self-contained artifacts are labelled previews for 1.0. A tag must not be promoted to Production 1.0 while any evidence item above is absent.

macOS preview tags publish ad-hoc-signed `.app` ZIPs for both Apple Silicon (`osx-arm64`) and Intel (`osx-x64`). They are integrity-signed previews, not Developer ID notarized production releases.
