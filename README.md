# V-Router

V-Router is an open-source .NET 10/Avalonia desktop CAD/CAM application for CNC router workflows. It supports persistent jobs, profile/pocket/drill/engraving/V-carve operations, automatic and manual ramped tabs, deterministic multi-sheet nesting, precision vector editing, stock-removal preview, reusable machine and tool libraries, fixture-aware safety preflight, and UCCNC `.nc`, `.txt`, and `.tap` export.

V-Router generates machine code; it does not directly control a CNC router. Preview and preflight reduce risk but cannot guarantee safe machining. Review generated code and use an air cut or other controlled validation process before cutting material.

## Status

V-Router is public-alpha software. The core CAD/CAM workflow is tested, but installers, broader hardware validation and additional postprocessors are still in development. Production users should independently verify every exported program.

## Development

```sh
dotnet restore
dotnet test CncCad.slnx
dotnet run --project src/CncCad.App
```

See [the development plan](docs/DEVELOPMENT_PLAN.md) for current status and the next implementation phases.

## License

V-Router is available under the [MIT License](LICENSE). Third-party components and acknowledgements are listed in [docs/THIRD_PARTY_NOTICES.md](docs/THIRD_PARTY_NOTICES.md).
