# CncCad

A .NET 10/Avalonia desktop CNC CAD/CAM application for router workflows. It supports persistent jobs and profile operations, automatic/manual ramped cutout tabs, deterministic multi-sheet nesting, precision vector editing, orbitable true-depth playback, reusable machine/tool libraries, fixture-aware safety preflight, and UCCNC `.nc`, `.txt`, and `.tap` export.

```sh
dotnet restore
dotnet test CncCad.slnx
dotnet run --project src/CncCad.App
```

See [the development plan](docs/DEVELOPMENT_PLAN.md) for current status and the next implementation phases.
