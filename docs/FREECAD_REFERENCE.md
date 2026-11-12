# FreeCAD Reference Policy

FreeCAD is a reference implementation, not a dependency. V-Router remains a C#/.NET 10/Avalonia application focused on approachable 2D CAD/CAM for CNC routers.

## Concepts to study

- CAM job and stock lifecycle
- Tool and tool-controller separation
- Profile, pocket, drill and engraving operation parameters
- Operation ordering and setup sheets
- Tabs, ramps, leads and boundaries as transformations around base operations
- Machine state, sanity checking, simulation and regression tests
- Postprocessor separation

Relevant upstream areas are `src/Mod/CAM/Path/Main`, `Path/Op`, `Path/Dressup`, `post`, and `CAMTests` in the FreeCAD repository.

For the 2D CAD foundation milestone, FreeCAD's separation between persistent document geometry and transient interactive commands was reviewed conceptually. V-Router implements its own small C# drawing sessions, immutable point/vector primitives, command history and snap engine; no upstream implementation or API shape was copied.

For the persistent CAM milestone, the concepts reviewed were durable operation parameters, tool snapshots/controllers, ordered jobs, recalculation after document changes, validation before postprocessing and source-to-result correlation. V-Router expresses these independently through its C# operation definitions, entity-ID invalidation contract, private Clipper adapter and machine-independent moves. No FreeCAD source, API, file format or runtime component was copied or introduced.

The guided workspace uses the common router-software progression of job setup, vector design, toolpath creation, preview and export.

## Explicitly out of scope

- Linking to or embedding FreeCAD
- OpenCASCADE as the main 2D engine
- Qt, Coin3D or Python runtime integration
- FreeCAD's document-object, workbench or plugin systems
- Part Design, assemblies, parametric solid modelling and general 3D CAD
- FreeCAD file compatibility
- Copying source implementations

Clipper2 remains the preferred polygon offset and boolean engine. FreeCAD may influence domain vocabulary and test cases, but V-Router implementations must fit our independent geometry, CAM and postprocessor contracts.

## Licensing

FreeCAD is primarily LGPL 2.1, while individual files or dependencies may carry additional terms. Architectural study is permitted; copying or linking code requires a file-level license review and is not part of the current plan. Any future reuse decision must be documented before code is introduced.
