# V-Router

V-Router is an open-source .NET 10/Avalonia desktop CAD/CAM application for CNC router workflows. It supports project-v9 metric/imperial jobs, surface/profile/pocket/drill/engraving/V-carve operations, square or ramped tabs, deterministic multi-sheet nesting, router-specific vector preparation, stock-removal preview, reusable libraries, mandatory safety preflight, and declarative output for UCCNC, Mach3, Mach4, GRBL and LinuxCNC.

V-Router generates machine code; it does not directly control a CNC router. Preview and preflight reduce risk but cannot guarantee safe machining. Review generated code and use an air cut or other controlled validation process before cutting material.

## Status

V-Router is a Production 1.0 candidate, not a certified release. The core CAD/CAM and five built-in post definitions are under automated test. Controller air-cut evidence, post certification and signed Windows installer validation remain mandatory release gates; current Linux and macOS builds are previews.

Tagged production builds create a signed, self-contained `V-Router-Setup.exe` for Windows x64. It is the single file intended for normal installation and sharing; no separate .NET installation is required.

## Development

```sh
dotnet restore
dotnet test CncCad.slnx
dotnet run --project src/CncCad.App
```

See the [Production 1.0 gates](docs/PRODUCTION-1.0-GATES.md), [behavior/provenance matrix](docs/BEHAVIOR-PROVENANCE.md), [postprocessor contract](docs/POSTPROCESSORS.md), and [privacy policy](docs/PRIVACY.md).

## License

V-Router is available under the [MIT License](LICENSE). Third-party components and acknowledgements are listed in [docs/THIRD_PARTY_NOTICES.md](docs/THIRD_PARTY_NOTICES.md).
