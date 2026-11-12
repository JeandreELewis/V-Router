# V-Router capability ledger

This ledger is the release-control inventory for functionality already visible in V-Router. New commands remain frozen until the unsafe and incomplete items below pass their individual unlock gates.

## Reference and provenance

- Behavioral reference source revision: FreeCAD `f1c522733da73db3e220e450460a2ac32cdee3f4`.
- Behavioral reference documentation revision: FreeCAD documentation `0499378a238ce4c77c643b9cc4a03d0947381e45`.
- Official comparison areas: Sketcher and Draft for vector behavior; CAM Job and CAM operations for job, operation, simulation and post behavior.
- Comparison means observing inputs, outcomes and failure behavior. V-Router does not copy FreeCAD source, artwork, icons, fixtures, UI layout, terminology or packaged assets.
- V-Router's mandatory preflight, immutable operation snapshots, non-executable post definitions and diagnostic no-op trim are intentional independent safety choices.

Status meanings:

- **Verified**: enabled and covered by focused automated tests.
- **Incomplete**: useful behavior exists but the full gate is not covered.
- **Unsafe**: visible but disabled, or blocked at the execution boundary.
- **Intentional difference**: deliberately differs from the reference behavior.
- **Out of scope**: explicitly deferred beyond this stabilization phase.

## Project, selection and shared document behavior

| Function | Status | Accepted input and expected result | Failure, history and CAM behavior | Reference comparison / unlock evidence |
|---|---|---|---|---|
| New/open/save `.cnccad` | Verified | New version-9 jobs; versions 1–8 migrate in memory; atomic save | Source is unchanged until save; malformed data is rejected | CAM Job document lifecycle; migration/recovery tests |
| Selection, marquee and overlap hit testing | Verified | Separate figures are hit-tested independently; marquee selects fully contained editable entities; repeated clicks cycle top-to-bottom overlaps deterministically | No phantom connectors between disconnected figures; Core and headless pointer-cycling regressions pass |
| Copy, paste, duplicate, delete | Incomplete | Graph-aware cloning preserves layer, sheet, colour, visibility, lock and transforms while assigning independent entity/group identities and remapping copied text-on-path guide links | Undo/redo restores the same clones; referenced CAM is stale or invalid | Draft modification behavior; full native-entity and headless shortcut matrix still required |
| Undo/redo transactions | Incomplete | Geometry replacement and layer commands | Completed mutation is one history entry; dependent CAM is invalidated | Sketcher transaction outcome; node/repair transaction unification remains |
| Groups | Incomplete | Selected entities share a group identity | Ungroup preserves entity identity | Draft grouping; pointer and persistence matrix remains |
| Layers | Verified | Add/delete/reorder/activate/show/lock/move entities and edit name/colour | All visible mutations use undoable commands | Draft layer behavior; layer command and ViewModel tests |
| Finite-number policy | Verified | Geometry, operation inputs, generated moves, machines and posts | Structured diagnostic; no NaN/infinity reaches generated motion | Independent safety policy; numeric rejection tests |
| Tolerance policy | Incomplete | Millimetres are canonical; explicit tolerances used for topology | Degenerate and self-intersecting contours rejected | Sketcher geometry validity; one shared configurable policy remains |

## Drawing and editing

| Function | Status | Current behavior | Intentional difference / remaining gate |
|---|---|---|---|
| Line, rectangle, circle, drill point | Verified | Interactive and numeric construction; finite native entities; undoable | Full pointer/keyboard cancellation matrix remains for UI gate |
| Polyline | Incomplete | Interactive/numeric open or closed line chain | Native arc/Bézier segments require VectorPath; complete cancellation and node tests remain |
| Three-point arc | Incomplete | Native arc from three non-collinear points | Transformed and near-collinear property tests remain |
| Slot, rounded rectangle, polygon, star | Incomplete | Parametric creation is available | Per-shape pointer/numeric/history tests remain |
| Move and exact position | Incomplete | World-space movement and lower-left positioning | Full transformed mixed-selection test matrix remains |
| Rotate, scale and mirror | Incomplete | Transform-based edits preserve native geometry | Pivot/property tests and cancellation tests remain |
| Align and distribute | Incomplete | Multi-entity alignment and distribution | Stable ordering and mixed-transform property tests remain |
| Trim | Verified | Removes only the clicked segment between nearest intersections; periodic curves wrap | **Intentional difference:** no intersection is a diagnostic no-op rather than deleting the whole object |
| Join | Verified | Order-independent endpoint chaining grows from either end | Rejects gaps beyond tolerance; metadata/history tests cover the operation |
| Chamfer | Verified | Exact world-millimetre setback under transformed geometry; metadata/history preserved | Rejects endpoint, collapsed, oversized and non-finite corner edits with diagnostics |
| Fillet | Verified | Exact tangent circular arc with a world-millimetre radius; metadata/history preserved | Exact arc paths remain protected from generic node dragging so the circle invariant cannot be silently broken |
| Closed-vector offset | Verified | Rectangle, circle, closed polyline and single-figure vector paths offset in world millimetres with deterministic Clipper cleanup, six-decimal precision, metadata preservation and explicit collapse/non-finite diagnostics | Concave, rotated and non-uniformly scaled regression fixtures prove finite simple output and source-edge distance |
| Open-vector offset | Verified | Positive/negative signed left/right offsets with butt endpoints, deterministic miter/bevel/round joins and loop cleanup | 180-degree reversals are rejected rather than generating ambiguous geometry |
| Node move/insert/delete/start node | Incomplete | Pointer selection is non-mutating; polyline move/mid-segment insertion/delete/start changes and VectorPath Bézier-handle moves are undoable with truthful button eligibility | Native VectorPath segment insertion/deletion/start rotation remains gated rather than silently flattening curves |
| Vector repair | Incomplete | World-millimetre duplicate, tiny/redundant segment, endpoint-gap and near-closure repair; cyclic/reversed duplicates and collinear overlaps are detected; previews expire on document mutation | Ambiguous self-intersections and overlaps are diagnosed and deliberately withheld from automatic mutation until topology-choice tests exist |
| Dogbone/T-bone preparation | Incomplete | Original corner-relief generation and diagnostics exist | Transformed concave/property and undo/CAM invalidation gates remain |
| Split, extend, persistent constraints | Out of scope | Not present | Post-stabilization backlog |

## Text and imports

| Function | Status | Current behavior | Remaining gate |
|---|---|---|---|
| Outline text | Incomplete | Font style, per-line alignment, multiline layout and vector outlines | Glyph-group transforms and full text-on-path conversion tests remain |
| Hershey/single-line text | Incomplete | Multiline layout/alignment; missing glyphs produce warning and visible placeholder | Full supported-glyph fixture and path-layout tests remain |
| Text on path | Incomplete | Position, reverse, flip, fit and upright settings exist | Preserve glyph grouping/transforms through conversion and edits |
| SVG import | Incomplete | Units/viewBox, nested transforms, closure, smooth curves and supported primitives; curve approximations are explicitly classified | Full units and supported-element fixture set remains |
| DXF import | Incomplete | Unit conversion, layers/basic entities, native circular bulge arcs, rotated ellipses and nested BLOCK/INSERT transforms; spline approximations are explicit | Native spline/NURBS fidelity and the complete fixture suite remain |
| Import diagnostics | Verified | Confirmation shows detected units, applied conversion, approximations, warnings, unsupported elements and available source locations before committing | Unsupported content never disappears silently; cancellation leaves the document unchanged |

## CAM operations

| Operation / option | Status | Current behavior | Safety and unlock gate |
|---|---|---|---|
| Profile | Incomplete | Cutter compensation, direction, arc-length manual start, leads, exact-length cyclic ramps, tabs, onion skin and allowances; finish passes feed laterally from the full-depth rough contour instead of plunging into allowance | All transformed/native-curve combinations still need independent fixtures |
| Pocket offset/raster and islands | Incomplete | Even-odd island clearing, direction and allowances; generated clearing paths are cutter-envelope checked and finish contours use a contained full-depth link from material already cleared by roughing | Expand the concave/nested property-fixture matrix |
| Pocket plunge/linear ramp | Verified | Plunges occur on envelope-validated clearing paths; deterministic linear ramps traverse or reverse along that path for the complete requested length and retrace it instead of taking an unsafe chord | Multi-depth, long-ramp, raster, offset, island and finish-entry regressions pass |
| Pocket helix entry | Verified | Deterministic helical descent with the full cutter envelope and every linking segment proven inside the even-odd machinable region | Returns `helix-containment` with remediation when the requested diameter cannot fit |
| Drill simple/peck/dwell | Verified | Cut depth is explicitly tool-tip referenced; selection and deterministic nearest ordering preserve transformed centers; pecks and bottom dwell expand into portable rapid/feed/dwell moves | Calculator and five-post tests cover depth, ordering, transformation, invalid input and absence of controller canned-cycle dependencies |
| Engrave | Verified | Open/closed paths, deterministic nearest-end ordering, forward/reverse direction and depth passes; circular entities and circular vector segments remain native arcs through similarity transforms, while Bézier, ellipse and non-circular affine results use the shared 0.02 mm bounded flattening tolerance | Native/reversed arcs, circles, non-uniform fallback, transformed geometry and deterministic ordering fixtures pass |
| V-carve | Verified | Deterministic sampled-boundary Voronoi medial-axis motion, clearance-derived variable depth, maximum-depth limiting, even-odd islands and same-bit flat-region clearing | Create control unlocks only for closed outlines on one sheet with a valid V-bit; determinism, boundary-clearance, island, cap and flat-clear tests pass |
| Surface | Incomplete | Bounded raster/offset facing with depth passes, validated cutter-radius overtravel, full edge coverage and vertical-first safe linking | Arbitrary non-rectangular facing regions remain outside the current visible operation |

## Preview, safety and export

| Function | Status | Current behavior | Remaining gate |
|---|---|---|---|
| Canonical motion evaluation | Verified | Linear, rapid, arc, dwell, spindle and tool events produce a deterministic timed stream | Preflight/post internals must finish consuming only this event stream |
| Playback and time estimate | Verified | Both use the canonical evaluator and selected machine rapid/tool-change timing | UI playback interaction matrix remains |
| CPU stock simulation | Incomplete | Uses canonical interpolation for cutter motion | Shank/holder and fixture-volume simulation remain |
| Safety preflight | Incomplete | Blocks stale/scope-mismatched/non-finite/invalid/envelope-violating programs; straight and circular motion use exact swept cutter/shank/holder intervals against expanded fixture bounds, including sub-sample thin fixtures | Broader physical fixture proof records remain |
| UCCNC, Mach3, Mach4, GRBL, LinuxCNC posts | Unsafe | Declarative preview definitions with controller-specific extensions, safe startup blocks, comments, capabilities and tool-change policies | All remain `CertificationPending`; controller-specific parser/simulator evidence and air-cuts are required per controller |
| Runnable G-code saving | Unsafe | Preview generation exists; save commands require a `Certified` selected post | No built-in post is certified, so saving is visibly disabled in menu, palette and native macOS menu |
| Multi-cutter file isolation | Verified | Safe export defaults to independent files by cutter; combined multi-cutter output requires a machine ATC plus a matching `Certified` automatic-tool-change post; conflicting tool numbers are rejected | Postprocessor and ViewModel regressions cover manual, unsupported, pending-certification and certified-ATC paths |
| Parking | Verified | No park output by default; explicit enabled park uses valid `G53` machine coordinates | Controller golden coverage remains part of certification |
| Atomic export batch | Verified | Entire batch is staged and validated before commit; rollback on failure | Duplicate normalized/sanitized names are rejected |

## Libraries, machine setup and nesting

| Function | Status | Current behavior | Remaining gate |
|---|---|---|---|
| Tool/material/machine/preset libraries | Verified | Searchable CRUD for all four library types; every tool-assembly and machine-safety dimension is exposed; matching presets seed immutable operation snapshots; complete JSON and per-type lossless CSV import/export reject dangling references and unsafe deletion | Service, ViewModel and minimum-size headless UI fixtures pass |
| SQLite library storage | Verified | Database-backed persistence with explicit ordered schema versions | Legacy missing-column migration is covered by a real SQLite fixture |
| Machine and fixtures | Verified | UI exposes travel, rapid/tool-change timing, spindle, holder, spoilboard, park and complete fixture bounds/clearance; finite validation feeds safety | Physical values remain the operator's responsibility |
| Nesting | Incomplete | Enforces quantity, spacing, margins, finite dimensions, grain, permitted rotation, all-parts-placed, deterministic replacement and undo/redo restoration | Pointer-driven workflow coverage remains |

## Release blockers

Production 1.0 is blocked while any **Unsafe** item remains, any controller lacks certification evidence, the proof jobs do not pass all required fixtures, or platform packaging/signing gates in `PRODUCTION-1.0-GATES.md` remain incomplete. A green unit-test suite alone is not a release claim.
