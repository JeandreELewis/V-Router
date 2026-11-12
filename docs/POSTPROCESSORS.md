# Postprocessor definitions

V-Router posts are declarative data. They cannot run code. A definition declares the controller family, extension, output precision, G20/G21 behavior, arc mode, comment syntax, tool-change behavior, header/footer lines and supported capabilities.

Built-in IDs are `uccnc`, `mach3`, `mach4`, `grbl` and `linuxcnc`. Community JSON definitions load from the user post directory and are always marked **uncertified**. Invalid identifiers, precision, extensions or multiline header/footer commands are rejected.

Output coordinates and feed rates are converted from canonical millimetres only while posting. The safe default is `SeparateByTool`: consecutive operations using the same cutter may share one independently terminated file, but a file never contains different cutters for a manual-change machine. No tool-change pause is inserted into split files.

A multi-cutter combined file is accepted only when the machine profile declares an automatic tool changer, the post declares automatic `M6` capability, and that exact post has reached `Certified` status. The post rejects reused tool numbers assigned to different cutter names. The Save Toolpaths dialog defaults to safe files by cutter; a deliberate one-file request returns an actionable error when any ATC condition is missing.

Preflight and post capability diagnostics are both mandatory. Post generation fails for unsupported arcs/dwell/tool changes, unsafe multi-cutter files, missing or conflicting tool numbers, incompatible controller families or invalid numerical motion.
