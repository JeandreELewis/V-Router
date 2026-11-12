# V-Router behavior and provenance matrix

The command-by-command implementation status and unlock evidence are maintained in [`CAPABILITY-LEDGER.md`](CAPABILITY-LEDGER.md). The pinned comparison revisions are FreeCAD `f1c522733da73db3e220e450460a2ac32cdee3f4` and FreeCAD documentation `0499378a238ce4c77c643b9cc4a03d0947381e45`.

V-Router is an independent implementation. External CAD/CAM applications may be used to observe expected manufacturing behavior and to compare neutral output, but their source code, artwork, UI copy, terminology, fixtures, and packaged assets must not enter this repository.

| Area | Required behavior | Independent implementation evidence | Test gate |
|---|---|---|---|
| Job and stock | Explicit size, XY origin, top/bed Z zero, metric or imperial display | `JobSettings`; millimetres remain canonical | project migration and coordinate-transform tests |
| Tools and presets | Immutable operation snapshots and machine/material presets | `ToolSnapshot`, `ToolCuttingPreset`, JSON/CSV exchange | library and snapshot tests |
| Profile | compensation, manual start, leads, ramp/plunge, tabs, onion skin, finish pass | typed `ProfileOperationDefinition` and calculator | finite/depth/tab/determinism tests |
| Pocket | offset/raster, islands, direction, plunge/ramp/helix, rough/finish allowance | typed `PocketOperationDefinition`, even-odd center regions and cutter-envelope containment | island, entry, finish, helix-containment and deterministic-motion tests |
| Drill | simple/peck, dwell and deterministic nearest ordering | expanded machine-independent moves | drill ordering and depth tests |
| Engrave | open/closed paths, depth passes and nearest ordering | independent path flattener and calculator | open/closed ordering tests |
| V-carve | variable depth, maximum-depth cap and same-bit flat clearing | independent sampled-boundary Delaunay/Voronoi medial-axis engine with even-odd containment and clearance-derived depth | medial-axis, boundary clearance, island, cap, flat-clear, determinism and headless unlock tests |
| Surface | spoilboard/stock facing with raster strategies | `SurfaceOperationDefinition` and calculator | extent, depth and finite-coordinate tests |
| Vector preparation | join/duplicate/open/overlap diagnostics, world-unit safe repairs and original corner relief | Core repair and router-preparation services; ambiguous topology is diagnosed without mutation | repair, metadata, transform and relief geometry tests |
| Simulation | deterministic stock removal and machine time | Simulation assembly consumes neutral moves only | stock-depth and timing tests |
| Posting | declarative definitions, unit conversion, capability diagnostics and tool splitting | constrained schema; no executable post scripts | five-post metric/imperial tests |
| Tool-change file safety | one cutter per runnable file by default; multi-cutter files only for a certified automatic tool changer and matching post | independent validation at both workflow and post boundaries, consecutive same-cutter grouping, unique cutter-number checks | unsafe-combination rejection, split-file and certified-ATC tests |
| Safety | current operations, valid tools, machine envelope, Z limits and fixtures | mandatory preflight before preview/export | blocking preflight tests |

Every new behavior requires a repository test or a documented physical validation record. A contributor who used an external implementation as a behavioral reference must describe only the observed behavior in the pull request; copied implementation details are prohibited.
