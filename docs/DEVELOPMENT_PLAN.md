# Development Plan

Last updated: 2026-08-23

## Overall architecture

The application is split into framework-independent Core, CAM and PostProcessor engines, with Avalonia confined to App and Rendering. Infrastructure provides EF Core/SQLite and Serilog. IO owns versioned project formats. See `ARCHITECTURE.md`.

## Current phase

Milestone 7 persistent multi-contour profile CAM — implemented and undergoing hardening.

## Completed

- .NET 10 solution and clean project reference graph
- Dark Avalonia CNC workspace with command bar, tool rail, canvas, inspector and status bar
- Independent geometry primitives/entities, layers, job settings and coordinate transforms
- Custom grid/material/entity/toolpath rendering; pan, zoom, zoom-to-fit, live coordinates and rectangle drawing
- Command history foundation
- SQLite tool model/repository, validation and ten editable sample records
- Machine-independent toolpath moves and rectangular outside/inside/on-vector profile generation
- Exact multiple-depth-pass calculation and basic machine-time estimate
- UCCNC postprocessor, invariant decimals, preview and `.txt`/`.nc`/`.tap` export
- Versioned JSON project serializer foundation
- Dedicated validated New CNC Job dialog for material size, thickness, XY origin, Z zero and safe Z
- Save and Save As `.cnccad` workflow before drawing begins
- Drawing workspace separated from job configuration; right sidebar now focuses on document context and toolpaths
- Open `.cnccad` projects through the native file picker
- Persist and display the ten most recent projects; remove stale entries when selected
- Save, Discard, Cancel protection for New, Open and application close
- FreeCAD reference policy defining the useful CAM concepts and strict dependency/scope boundary
- Framework-independent selection model and document-coordinate entity hit testing
- Click selection, Ctrl/Shift additive selection, marquee selection, selection bounds and corner handles
- Delete selection with command-backed Undo/Redo
- Delete/Backspace, Ctrl/Cmd+Z, Ctrl/Cmd+Shift+Z and Ctrl/Cmd+Y shortcuts
- Rectangle creation now participates in the same undo history as editing actions
- Reusable drawing sessions for line, polyline, rectangle, circle and three-point arc creation
- Live construction previews and length/radius/size feedback
- Core three-point arc construction with collinear-point rejection and exact swept-arc bounds
- Grid, endpoint, midpoint and center snapping with deterministic priority, toggles and visual markers
- Drag moving with geometry ghost preview, bottom-left numeric positioning and 1/10 mm keyboard nudging
- Copy, paste and duplicate with new entity IDs, visible offsets and grouped undo/redo
- Translation, cloning, snap anchors and hit testing for line/polyline/rectangle/circle/arc/ellipse
- Geometry edits invalidate stale calculated toolpaths and G-code previews
- Project round-trip coverage for every supported entity type
- Structured live measurements for line, polyline, rectangle, circle, arc and selection movement
- Cursor-relative measurement HUD with snap type, construction guidance and edge clamping
- Concise status-bar measurements sourced from the same structured values
- Data-driven two-column drawing palette with equal tiles, vector icons, tooltips and accent selected state
- Equal-size two-column edit actions using the same spacing and icon system
- Geometry, CAM and postprocessor automated tests
- Persistent CAM job with polymorphic profile definitions, stable source-operation correlation and immutable tool snapshots
- Version-2 project envelope with automatic version-1 geometry migration
- Private Clipper2 2.0.0 adapter with exact cutter-radius compensation
- Profile cutting for rectangles, closed polylines and CAM-flattened circles
- Outside, inside and on-vector compensation with climb/conventional winding
- Inside-first multi-contour ordering, nearest-next selection and contour-complete depth passes
- Persistent Toolpaths list with create/edit, duplicate, delete, enable, recalculate and reorder actions
- Modal profile editor with cutter, depth, rate, spindle, safe-Z, validation and pass-count fields
- Central stale/invalid operation lifecycle driven by affected geometry command IDs
- Ordered recalculation, preview and deterministic UCCNC export with invalid-operation blocking
- Guided three-region workspace with Design controls on the left, 2D/preview canvas in the centre and persistent CAM controls on the right
- Contextual workflow stage, profile-readiness guidance, mutually exclusive Design/Toolpath panels and explicit empty-workspace prompts
- Permanent view toolbar with fit, selection fit, grid, snapping and toolpath visibility controls
- Undoable center-selection-on-material action and selection-aware edit controls
- Playable selected/all 2D toolpath preview with progressive path, cutter marker, transport controls, seeking and visualization-only speed multipliers
- Orbitable orthographic 3D toolpath preview with true-Z trajectories, material slab, over-depth warnings, camera presets and depth readout
- Explicit UCCNC `.nc`, `.txt` and `.tap` export selection with normalized filenames
- Contextual precision construction for lines, polylines, rectangles, circles and three-point arcs
- Undoable rotate, scale, mirror, align, distribute, open/close, join and offset vector workflows
- Transform-aware world geometry across rendering, bounds, snapping, hit testing, persistence and profile CAM
- Line/polyline node dragging, exact positioning, insertion, deletion and closed-path start-node changes
- Strict Design/Preview state separation with preserved playback, independent view state and context-specific toolbars
- Automatic persistence of CAM definitions with the associated `.cnccad` job and reopen-time recalculation
- Graphical nine-point work datum, X/Y datum offsets and surface/bed Z-zero conversion through UCCNC export
- Version-3 machine-safety data with reusable SQLite machine profiles, immutable job snapshots and rectangular fixture zones
- Deterministic preflight for machine travel, stock/spoilboard depth, rapid clearance, spindle limits, tool length/stick-out and cutter/shank/holder fixture collisions
- Preflight error/warning workflow, 3D fixture/issue overlays, and validated retract-to-clearance plus XY park export ending

## In progress

- Milestone hardening: dedicated tool library CRUD screen, close-path UI automation, full affine-transform support and responsive top command-bar overflow
- Resolve upstream `SQLitePCLRaw.lib.e_sqlite3` security advisory when a patched compatible native bundle is published/selected

## Upcoming

1. Full tool-library CRUD, search, CSV/JSON import/export, XLSX mapping wizard and image storage
2. Pocket, drill and engraving operations
3. SVG/DXF import, trim, curve-node editing, text/shapes, progressive stock-removal simulation, tabs, ramps and leads

## Known issues

- Profile selection accepts closed entities directly; automatic joining of separate lines and arcs is not yet implemented.
- Editing job setup reframes the material automatically.
- The “Add 6mm tool” action is intentionally narrow; full library management is next.
- Project save/open and recent-project workflows are exposed; automatic recovery files are not yet implemented.
- Version 1 project files open as geometry-only jobs and upgrade on their next normal save.
- Entity `Matrix2D` exists for future affine operations, but interactive rotation/scaling and fully transformed hit testing are not implemented.
- At narrow desktop widths, panel focus modes improve working area; a future overflow menu can further simplify the top project bar.
- SQLite native package 2.1.11 reports GHSA-2m69-gcr7-jv3q during restore.

## Testing status

Core/IO/Infrastructure/Rendering sessions: distance, bounds, rotation, viewport round-trip, machine origin, selection hit testing, marquee containment, three-point arcs, cloning/translation, snapping priority, structured tool measurements, HUD edge clamping, palette completeness, drawing-tool completion, grouped add/move/delete undo/redo, all-entity project round-trip, legacy migration, version-3 safety persistence, and bounded/deduplicated recent-project persistence. CAM: cutter-radius offsets, profile generation, snapshots/lifecycle, machine-envelope, fixture, spoilboard, spindle and tool-assembly preflight. Simulation: rapid/linear/arc duration and interpolation, stable move indexing, playback state, seeking, speed independence and selected/all ordering. Postprocessor: coordinates, units, safe Z, park position, spindle, invariant formatting and termination.

## Supported formats

- Project: `.cnccad` JSON save/open UI
- G-code export: `.txt`, `.nc`, `.tap`
- SVG/DXF: planned
- Tool import/export: planned

## Supported toolpaths

- Multi-contour profile: rectangle, closed polyline and circle; outside, inside and on-vector; climb and conventional
- Pocket, drill and engrave: planned

## Supported postprocessors

- UCCNC

## Vector import and advanced drawing milestone

- Implemented project-v4 native line/quadratic/cubic vector paths, portable text-outline persistence, SVG/DXF import boundaries, import review, polygon/star/slot/rounded-rectangle creation, repair preview/apply, layer routing/visibility/locking/color/order, Bézier handle movement, and initial trim/fillet/chamfer services.
- Next hardening pass: full nested DXF INSERT expansion and rational splines; exact trim/split/extend/weld across arcs, ellipses, and all Bézier combinations; platform font-outline adapters; per-repair-category selection and canvas hover previews.
- Implemented project-v5 automatic/manual ramped profile tabs, direct canvas tab placement, deterministic grain-aware multi-sheet nesting, accepted sheet-owned copies, sheet-specific CAM/preflight/playback, and numbered batch export. Common-line cutting and arbitrary-angle rotation remain deferred.
