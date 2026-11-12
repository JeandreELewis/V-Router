# Postprocessor definitions

V-Router posts are declarative data. They cannot run code. A definition declares the controller family, extension, output precision, G20/G21 behavior, arc mode, comment syntax, tool-change behavior, header/footer lines and supported capabilities.

Built-in IDs are `uccnc`, `mach3`, `mach4`, `grbl` and `linuxcnc`. Community JSON definitions load from the user post directory and are always marked **uncertified**. Invalid identifiers, precision, extensions or multiline header/footer commands are rejected.

Output coordinates and feed rates are converted from canonical millimetres only while posting. The safe default is `SeparateByTool`: consecutive operations using the same cutter may share one independently terminated file, but a runnable file never contains different cutters. No tool-change pause is inserted into split files.

A multi-cutter combined file is always rejected, including for automatic-tool-change machine profiles. The post also rejects reused tool numbers assigned to different cutter identities or names. The Save Toolpaths dialog defaults to safe files by cutter; a deliberate one-file request succeeds only when every operation uses the same cutter.

Preflight and post capability diagnostics are both mandatory. Post generation fails for unsupported arcs/dwell/tool changes, unsafe multi-cutter files, missing or conflicting tool numbers, incompatible controller families or invalid numerical motion.
