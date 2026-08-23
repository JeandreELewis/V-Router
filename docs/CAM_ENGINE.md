# CAM Engine

The implemented pipeline is `RectangleEntity → ProfileParameters + CncTool → ToolpathOperation → ToolpathProgram`.

Outside and inside rectangular profiles offset the cutter centreline by half the tool diameter. Pass depths are generated from start depth to exact final cut depth, so a 0–18 mm cut at 3 mm per pass produces 3, 6, 9, 12, 15 and 18 mm. Operations include tool/spindle state, safe-Z rapids, plunges and linear cuts.

Next: a Core-to-Clipper2 adapter, arbitrary closed polylines, pocket offsets, drill cycles, engraving, tabs, ramps and lead moves. These calculations remain outside ViewModels and controls.
