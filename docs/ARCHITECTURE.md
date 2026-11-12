# Architecture

## Dependency direction

`CncCad.Core` owns documents, geometry, entities, units and coordinate transforms and has no UI dependency. `CncCad.Cam` depends only on Core and converts geometry plus operation parameters into deterministic machine-independent moves through a typed calculator registry. `CncCad.PostProcessors` converts those moves into controller syntax through a constrained declarative definition. `CncCad.Rendering` is the Avalonia adapter for documents and toolpaths. `CncCad.Infrastructure` owns SQLite/EF Core and logging. `CncCad.IO` owns project/import/export formats. `CncCad.Simulation` estimates program time. The App is the composition root.

Project schema version 9 is current. Versions 1–8 are migrated in memory; the original file is not changed until the user saves. Millimetres are canonical inside every assembly. Inch conversion occurs only at input, display and postprocessing boundaries.

## Decisions

- Millimetres are canonical. Display units are document preferences only.
- Job setup persists a nine-point XY material datum plus X/Y offsets and either material-surface or machine-bed Z zero. CAD remains in stable document coordinates; the datum is applied only at the document-to-machine boundary, origin rendering, and postprocessing. Legacy enum values remain numerically compatible.
- Screen, document and machine coordinates have explicit transforms. Zoom and pan only change `ViewportTransform`.
- CAD entities use project-owned primitives, never Avalonia types.
- Toolpaths exist as typed moves before any G-code is produced.
- Tools live in an application SQLite database. Each CAM operation stores an immutable tool snapshot so an existing job remains reproducible when the master library changes.
- The canvas is one custom-rendered control to support large entity counts.
- JSON project files use a version-9 `CncProject` envelope containing the CAD document, persistent `CamJob`, nesting state, material and immutable machine snapshots, postprocessor identity, and job fixtures. Version-1–8 documents migrate in memory and are written as version 9 on their next normal save. Calculated moves, stock simulation, playback, and preflight results remain transient.
- Persistent CAM mutations automatically save an existing project file using a same-directory temporary file and atomic replacement. New jobs without a path retain changes in memory and explicitly request a project save; calculated toolpath moves and playback remain transient and are recalculated after reopening.
- FreeCAD is an architectural and behavioral reference only. It is not linked, embedded, or used as a runtime, and its general-purpose CAD object model will not define this router-focused product. See `FREECAD_REFERENCE.md`.
- Selection and hit testing use document coordinates in Core. Rendering translates pointer locations before invoking them; pixel tolerance is converted to document millimetres using viewport zoom.
- All geometry mutations initiated by editing tools go through `IUndoableCommand`. The current history is session-local and is deliberately not serialized.
- Construction math, cloning, translation, snap candidates and snap priority live in Core. Rendering owns short-lived `DrawingToolSession` state, converts pointer locations to document millimetres, and emits completed entities to the application command history.
- Geometry commands expose affected entity IDs. The application centrally marks referencing CAM operations stale after edits and invalid after deletion; undo/redo passes through the same invalidation path.
- Drawing sessions expose a structured `DrawingMeasurement` rather than UI-ready prose. The canvas owns the cursor-relative HUD layout/rendering, while the ViewModel derives the compact status-bar summary from the same values.
- The drawing palette is data-driven through `ToolPaletteItem`; XAML owns equal tile layout and selected-state styling, and palette selection is the single source of active canvas tool state.
- Contextual exact-construction state lives in the workspace ViewModel. It produces the same Core entities and undoable add commands as pointer drawing; the canvas HUD remains display-only.
- `Matrix2D` is the persistent local-to-world transform. World-geometry helpers are the shared source for bounds, snapping, hit testing, rendering, node inverse mapping and CAM contour extraction. Transform and topology commands expose affected IDs so CAM invalidation follows undo and redo.
- CAD offsets cross the public `ICadOffsetService` contract in CAM, while Clipper2 types remain private. Closed contours use robust polygon offsets; open paths expose a signed left/right vector result. Offset results are new entities and leave sources unchanged.
- The desktop shell uses a permanent Design–Workspace–Toolpaths structure. Derived ViewModel state controls workflow stage and command availability; XAML does not independently infer whether selected geometry is CAM-compatible.
- `WorkspaceFocus` enforces one task panel at a time: Design expands while Toolpaths collapses, or Toolpaths expands while Design collapses. `WorkspaceView` changes the central canvas emphasis between source geometry and transient calculated paths while sharing one document viewport.
- `WorkspaceView` is also a strict rendering boundary: Design never renders transient toolpaths or playback chrome, while Preview owns the 3D camera, trajectories and transport bar. Switching views retains the playback session and independent camera/2D viewport state without exposing one mode's controls in the other.

## CAM operation pipeline

`ProfileOperationDefinition` stores ordered geometry references, the tool snapshot, compensation/direction, cutting parameters, enable state and output order. `ICamOperationCalculator` resolves those references and returns either a transient `ToolpathOperation` bearing the source operation ID or structured validation errors. Preview and every declarative controller post rebuild enabled operations in visible order and refuse invalid output.

`ToolpathPlaybackSession` in Simulation converts machine-independent moves into a transient time line. It owns play/pause/seek/speed state and produces immutable playback frames; Avalonia supplies wall-clock ticks, and Rendering only draws completed/future segments and the current cutter. Playback multipliers never mutate feeds, operation definitions, estimated machine time or exported G-code.

`ISafetyPreflightService` in CAM samples complete linear and arc trajectories and validates them against the job datum, machine envelope, stock/spoilboard, tool assembly and rectangular fixture volumes. Findings carry stable operation/move references and XYZ locations. Errors block preview/export; warnings require fresh export acknowledgement. Reusable machine definitions live in SQLite, while jobs retain snapshots for reproducibility. Safety checking is advisory and cannot guarantee a crash-free physical setup.

`Point3D` and `OrbitCamera` provide a framework-independent orthographic projection with isometric/top/front/right presets, pitch clamping, orbit, pan, zoom and job fitting. The simulation layer derives an immutable CPU heightfield from enabled calculated toolpaths. Rendering samples that field into a closed stock mesh and uses a cancellable CPU depth-buffer renderer for neutral Technical shading or original procedural Hardwood, Softwood, Plywood and MDF appearances. Interactive camera movement uses a reduced render and refines after 150 ms idle. Toolpaths, cutter, fixtures, datum and safety findings remain vector overlays. The camera and rendered bitmap are transient; only visualization appearance settings are persisted, and they do not participate in CAM or export.

Rectangles, closed polylines and exact CAD circles cross a private `ClipperGeometryAdapter` boundary in CAM. Circles are flattened to a 0.02 mm chord-error target only for CAM, while Clipper2 2.0.0 performs cutter-radius and CAD contour offsets. Clipper types do not enter Core, IO contracts, ViewModels or postprocessors. Multiple contours are ordered inside-first by containment depth and nearest-next within a tier; every depth pass is completed before the next contour.

## Current dependency exception

Rendering currently references Avalonia directly. Core, CAM and PostProcessors do not. Clipper2 is confined to the private CAM geometry adapter.

## Native vectors, import, and layers

Project format v4 adds `VectorPathEntity` figures containing polymorphic line, quadratic Bézier, and cubic Bézier segments. Curves remain native for editing, persistence, bounds, hit testing, and display; CAM and tolerance-based analysis consume adaptive flattened copies. `TextEntity` persists editable text metadata and project-owned outline figures so reopening cannot silently change machining geometry when a font is missing.

SVG and DXF implementation details stay in `CncCad.IO`. SVG uses an XML/path parser with unit conversion, viewBox handling, nested transforms, groups, and native Bézier/elliptical-arc conversion. IxMilia.Dxf 0.8.4 is pinned behind the DXF adapter; no IxMilia type crosses the IO boundary. Imports return entities, layers, detected units, bounds, warnings, and unsupported-content summaries before one atomic import command mutates the document.

Layer visibility and locking are document rules. Rendering and snapping exclude hidden content; pointer selection and mutation exclude locked content. Repair uses a non-mutating analyze step and a grouped undoable apply step, which centrally invalidates CAM and preflight state.

## Tabs and material nesting

Project format v5 persists profile tab definitions and nesting inputs/results. Ramped tabs are operation-owned CAM parameters: automatic positions are deterministic, manual positions use normalized contour distance, and calculated Z ramps remain transient toolpath moves. The ordinary playback, preflight, time-estimation, and postprocessor boundaries consume those moves without special controller behavior.

`INestingOptimizer` is framework-independent. It consumes source CAD geometry and persisted part/sheet constraints, uses private Clipper-backed clearance collision checks, and scores deterministic candidates by sheet count, waste, then estimated rapid distance. Accepted layouts create new sheet-owned entity IDs and preserve source design geometry. Sheet-specific operations retain their material-sheet and source-geometry correlation through calculation and export.

## CNC text, appearance, and engraving

Project format v6 persists portable `TextEntity` source glyph geometry, rendered path-fitted geometry, glyph advances, guide linkage, vector color overrides, groups, and polymorphic engraving operations. System-font discovery and outline extraction are private Infrastructure adapters built on SkiaSharp; third-party types never cross the font contracts. A compact built-in centerline adapter provides open-stroke CNC lettering while the complete licensed JHF asset catalog remains a separate content task.

Straight and path-fitted text remain editable until conversion. Path placement is calculated in Core from a persisted guide snapshot and native line/quadratic/cubic glyph figures. Conversion is an undoable replacement with grouped project-owned vector paths. Per-entity `StrokeColor` is an organizational CAD property only and never changes CAM semantics or G-code.

Selection is rendered in screen pixels by redrawing selected geometry with a dark halo and bright yellow inner stroke; the dashed bounds rectangle is secondary guidance. Repeated clicks cycle overlapping editable geometry, while hidden and locked layers are excluded.

`EngraveOperationDefinition` is persistent CAM state. Its calculator accepts open or closed line, polyline, arc, ellipse, native vector-path, and portable text strokes; curve flattening is tolerance controlled, disconnected glyph strokes are intentional, and each stroke completes its configured depth passes before a safe traverse. It uses the existing playback, safety, ordering, persistence, and postprocessor boundaries.

Project format v7 adds per-glyph portable geometry and `VCarveOperationDefinition`. Glyph records preserve cluster, order, advance, source figures, and placed figures, allowing conversion to one grouped vector entity per glyph. Linked-guide deletion detaches lettering and deletes the guide as one undoable composite mutation.

Single-tool V-carving uses the private Clipper adapter to produce deterministic inward offset rings. Ring distance and the snapshotted included angle/tip diameter determine Z; depth is capped by the operation maximum, with optional continued V-bit clearing at the capped depth. V-carving shares operation ordering, sheet ownership, playback, preflight, postprocessing, and stale invalidation with profile and engraving operations. It is a practical offset-envelope strategy, not a medial-axis or two-tool clearing implementation.
## Desktop shell commands and preferences

The desktop shell exposes the same working actions through a native macOS menu, an in-window Windows/Linux menu, the compact toolbar, and keyboard gestures. `ApplicationCommand` is the common command metadata boundary; availability remains derived from document, selection, CAM, and playback state rather than being embedded in menu presentation.

Application preferences are per-user infrastructure data and never enter `CncProject`. They control startup focus, grid and snapping defaults, toolpath visibility, and accent presentation. Job dimensions, machine snapshots, CAM definitions, fixtures, safety inputs, and mandatory safety acknowledgement remain project/workflow concerns and cannot be overridden by preferences.
