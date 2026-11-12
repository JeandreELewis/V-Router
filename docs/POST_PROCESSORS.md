# Postprocessors

`IPostProcessorRegistry` resolves a persisted postprocessor identifier to an `IPostProcessor` and exposes its name, supported extension, compatible machine class, and configurable options. The first production descriptor is `uccnc-mm` for flat-sheet 3-axis routers.

The UCCNC implementation accepts a machine-independent `ToolpathProgram` and `MachineProfile`. It emits metric (`G21`), absolute (`G90`), XY-plane (`G17`), incremental arc-centre (`G91.1`), feed-per-minute (`G94`) output and `G4` dwell moves. It cancels cutter compensation, tool-length compensation and canned cycles before motion, retracts before tool changes, stops the spindle before parking, and terminates with `M30`. Numbers always use invariant culture with three decimal places.

The unified Save Toolpaths workflow writes the UCCNC descriptor's `.nc` extension for a selected operation, all visible operations combined, or visible operations as separate files. Arc moves use I/J offsets from the previous endpoint. Invalid tool numbers, spindle speeds, feeds, RPM, non-finite values, unsupported moves, incomplete machine data and unpositioned arcs are rejected rather than written as unsafe output. Writes are atomic and remain gated by safety preflight.
