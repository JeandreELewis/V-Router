# Postprocessors

`IPostProcessor` accepts a machine-independent `ToolpathProgram` and `MachineProfile`. The initial UCCNC implementation emits metric (`G21`), absolute (`G90`) output with `G0`, `G1`, spindle start/stop and `M30`. Numbers always use invariant culture with three decimal places.

Supported export extensions are `.txt`, `.nc` and `.tap`. Arc moves are represented in the domain; controller validation for I/J arc output is pending before the UI creates arcs.
