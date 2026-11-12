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
| Copy, paste, duplicate, delete | Verified | Graph-aware cloning preserves every native entity type, layer, sheet, colour, visibility, lock, exact curve segments and transforms while assigning independent entity/group identities and remapping copied text-on-path guide links | Undo/redo restores the same clones; deletion detaches surviving linked text as part of the same transaction and marks its CAM stale while deleted references become invalid; locked selections are rejected | Full native-entity and headless Copy/Paste/Duplicate/Delete/Undo/Redo shortcut matrices pass |
| Undo/redo transactions | Verified | Shared command history for geometry, nodes, repairs, layers and job setup | Completed mutation is one history entry; stock edits stale every CAM operation and nesting result | Deliberate transaction boundary with deterministic undo/redo and broader router-safety invalidation |
| Groups | Verified | Selected unlocked entities share a group identity; clicking one member selects the complete editable group | Group/ungroup are single undoable transactions, preserve entity identity and invalidate dependent CAM; locked selections are rejected | Pointer selection, undo/redo and version-9 persistence fixtures pass |
| Layers | Verified | Add/delete/reorder/activate/show/lock/move entities and edit name/colour | All visible mutations use undoable commands | Draft layer behavior; layer command and ViewModel tests |
| Finite-number policy | Verified | Geometry, operation inputs, generated moves, machines and posts | Structured diagnostic; no NaN/infinity reaches generated motion | Independent safety policy; numeric rejection tests |
| Tolerance policy | Verified | Millimetres are canonical; one immutable production policy defines endpoint equality, minimum length/area, interaction, curve-flattening, import and boolean precision tolerances while keeping dimensionless numerical guards explicit | Degenerate and self-intersecting contours are rejected; alternate policy records validate before use | Shared-policy tests plus geometry, topology, import and CAM property fixtures |

## Drawing and editing

| Function | Status | Current behavior | Intentional difference / remaining gate |
|---|---|---|---|
| Line, rectangle, circle, drill point | Verified | Interactive and numeric construction creates finite non-collapsed native entities and one undoable mutation | Core pointer/keyboard completion, invalid-input and Escape cancellation fixtures pass |
| Polyline | Verified | Interactive/numeric open chains require two nodes; closed chains require three; duplicate/non-finite nodes cannot produce geometry | Enter/C completion, Escape and explicit numeric Cancel preserve the document and history; native arc/Bézier segments remain VectorPath geometry |
| Three-point arc | Verified | Stable translated three-point circumcircle construction rejects non-finite, duplicate, collinear and scale-relative near-collinear inputs | Semicircle, large-coordinate degeneracy, interactive completion and cancellation fixtures pass |
| Slot, rounded rectangle, polygon, star | Verified | Finite positive dimensions, bounded point counts and undoable closed vectors; zero-radius rounded rectangles contain four real corners without collapsed duplicates | Per-shape success/undo and invalid/non-finite fixtures pass |
| Move and exact position | Verified | Finite world-space movement and exact lower-left positioning preserve native entities and metadata | Command/ViewModel tests cover invalid input, undo/redo and dependent CAM invalidation |
| Rotate, scale and mirror | Verified | Finite invertible pivot transforms preserve native lines/arcs/shapes rather than flattening them; identity transforms are diagnostic no-ops | World-bound, pivot, horizontal/vertical mirror, invalid-input, undo/redo and CAM-staleness fixtures pass |
| Align and distribute | Verified | Six explicit align modes and deterministic ID-tiebroken ordering; distribution produces equal edge-to-edge gaps for mixed-size selections while keeping the two extremes fixed | Unknown modes and no-op layouts do not mutate history; mixed-size horizontal and undo fixtures pass |
| Trim | Verified | Removes only the clicked segment between nearest intersections; periodic curves wrap | **Intentional difference:** no intersection is a diagnostic no-op rather than deleting the whole object |
| Join | Verified | Order-independent endpoint chaining grows from either end | Rejects gaps beyond tolerance; metadata/history tests cover the operation |
| Chamfer | Verified | Exact world-millimetre setback under transformed geometry; metadata/history preserved | Rejects endpoint, collapsed, oversized and non-finite corner edits with diagnostics |
| Fillet | Verified | Exact tangent circular arc with a world-millimetre radius; metadata/history preserved | Exact arc paths remain protected from generic node dragging so the circle invariant cannot be silently broken |
| Closed-vector offset | Verified | Rectangle, circle, closed polyline and single-figure vector paths offset in world millimetres with deterministic Clipper cleanup, six-decimal precision, metadata preservation and explicit collapse/non-finite diagnostics | Concave, rotated and non-uniformly scaled regression fixtures prove finite simple output and source-edge distance |
| Open-vector offset | Verified | Positive/negative signed left/right offsets with butt endpoints, deterministic miter/bevel/round joins and loop cleanup | 180-degree reversals are rejected rather than generating ambiguous geometry |
| Node move/insert/delete/start node | Verified | Pointer selection is non-mutating; polyline and native VectorPath edits are undoable and invalidate dependent CAM; line, quadratic, cubic and circular-arc insertion uses exact segment splitting; closed-path start rotation preserves native segment order; deletion preserves unaffected segments | Exact-arc anchors are selectable and splittable but intentionally non-draggable because moving one endpoint would invalidate the circle; numeric and pointer movement controls report that eligibility truthfully |
| Vector repair | Verified | World-millimetre duplicate, tiny/redundant segment, endpoint-gap and near-closure repair; cyclic/reversed duplicates are recognized; closed crossings are polygonized into deterministic bounded simple contours; duplicate internal edges are removed; connected single-segment overlap chains merge into one span; metadata and stable identities are preserved | Branched open networks remain explicit non-mutating diagnostics because no unique contour exists without a user topology choice; finite rejection, transformed property sweeps, preview expiry, undo/redo, CAM invalidation and minimum-size headless workflow tests pass |
| Dogbone/T-bone preparation | Verified | Cutter-radius relief is generated in world millimetres only at valid inside corners; outside, straight, degenerate and non-finite selections are rejected without mutation; source identity, grouping, layer, sheet and colour metadata are preserved | Reversed, reflected, rotated and non-uniformly transformed fixtures prove deterministic finite output; both styles are accepted by profile calculation and UI tests cover eligibility, undo and dependent CAM invalidation |
| Split, extend, persistent constraints | Out of scope | Not present | Post-stabilization backlog |

## Text and imports

| Function | Status | Current behavior | Remaining gate |
|---|---|---|---|
| Outline text | Verified | Font style, per-line alignment, multiline layout and native vector outlines | Glyph contours use one rigid path transform and remain grouped through vector conversion |
| Hershey/single-line text | Verified | Deterministic A–Z, 0–9, space and common router-label punctuation; multiline layout/alignment; unsupported glyphs produce a warning and visible placeholder | Full declared coverage fixture and grouped path-layout tests pass |
| Text on path | Verified | Centre-position, reverse, flip-side, fit-spacing and upright settings preserve each multi-contour glyph as one rigid group | Guide edits regenerate glyph placement from portable source geometry; conversion preserves grouping, metadata and native segments |
| SVG import | Verified | Physical/CSS units, `viewBox` meet/none/slice alignment, nested transforms, closure, smooth curves, line/polyline/polygon/rect/circle/ellipse/path and local `<use>`; unequal rounded-rectangle radii are preserved as cubic corners | Unreferenced definitions/symbols and hidden content never cut; malformed or unknown path commands, collapsed geometry and unsupported elements carry source diagnostics instead of being reinterpreted |
| DXF import | Verified | Unit conversion, optional layer preservation, line/circle/arc/polyline, native bulge arcs, full rotated ellipses, nested BLOCK/INSERT and deterministic spline/NURBS evaluation | Partial ellipse arcs and splines are explicit bounded approximations; nonzero Z is explicitly reported as XY projection; unsupported/collapsed entities never disappear silently |
| Import diagnostics | Verified | Confirmation shows detected units, applied conversion, approximations, warnings, unsupported elements and available source locations before committing | Unsupported content never disappears silently; cancellation leaves the document unchanged |

## CAM operations

All six public operation calculators apply the shared finite-number policy at their direct entry point and validate every generated machine move before returning a toolpath; callers do not need to route through the registry to receive these guarantees.

| Operation / option | Status | Current behavior | Safety and unlock gate |
|---|---|---|---|
| Profile | Verified | Cutter compensation, direction, arc-length manual start, leads, exact-length cyclic ramps, tabs, onion skin and allowances; finish passes feed laterally from the full-depth rough contour instead of plunging into allowance; the 0.02 mm curve-flattening bound is converted through affine scale before compensation | Independent fixtures cover rectangles, closed polylines, circles and exact-arc vector paths under rotation, reflection and scale for inside, outside and on-vector cuts, including finite deterministic motion and direction |
| Pocket offset/raster and islands | Verified | Even-odd island clearing, direction and allowances; generated clearing paths are cutter-envelope checked and finish contours use a contained full-depth link from material already cleared by roughing | A deterministic matrix covers concave three-level nested regions, offset/raster, climb/conventional, rotation/reflection, two depth passes, ramp links and canonical-stream swept-cutter sampling |
| Pocket plunge/linear ramp | Verified | Plunges occur on envelope-validated clearing paths; deterministic linear ramps traverse or reverse along that path for the complete requested length and retrace it instead of taking an unsafe chord | Multi-depth, long-ramp, raster, offset, island and finish-entry regressions pass |
| Pocket helix entry | Verified | Deterministic helical descent with the full cutter envelope and every linking segment proven inside the even-odd machinable region | Returns `helix-containment` with remediation when the requested diameter cannot fit |
| Drill simple/peck/dwell | Verified | Cut depth is explicitly tool-tip referenced; selection and deterministic nearest ordering preserve transformed centers; pecks and bottom dwell expand into portable rapid/feed/dwell moves | Calculator and five-post tests cover depth, ordering, transformation, invalid input and absence of controller canned-cycle dependencies |
| Engrave | Verified | Open/closed paths, deterministic nearest-end ordering, forward/reverse direction and depth passes; circular entities and circular vector segments remain native arcs through similarity transforms, while Bézier, ellipse and non-circular affine results use the shared 0.02 mm bounded flattening tolerance | Native/reversed arcs, circles, non-uniform fallback, transformed geometry and deterministic ordering fixtures pass |
| V-carve | Verified | Deterministic sampled-boundary Voronoi medial-axis motion, clearance-derived variable depth, maximum-depth limiting, even-odd islands and same-bit flat-region clearing | Create control unlocks only for closed outlines on one sheet with a valid V-bit; determinism, boundary-clearance, island, cap and flat-clear tests pass |
| Surface | Verified | Whole-stock or one selected simple closed region; clipped raster and inward-offset strategies, depth passes, cutter-radius overtravel, edge coverage and vertical-first safe linking | Concave/transformed region, missing/open/multiple-boundary diagnostics, determinism and stock-default fixtures pass |

## Preview, safety and export

| Function | Status | Current behavior | Remaining gate |
|---|---|---|---|
| Canonical motion evaluation | Verified | Linear, rapid, arc, dwell, spindle and tool events produce one deterministic timed stream consumed by playback, timing, stock simulation, preflight and posting | Cross-consumer event/G-code fixtures prove one arc start, end, centre, direction and operation order |
| Playback and time estimate | Verified | Both use the canonical evaluator and selected machine rapid/tool-change timing | UI playback interaction matrix remains |
| CPU stock simulation | Incomplete | Uses canonical interpolation for cutter motion | Shank/holder and fixture-volume simulation remain |
| Safety preflight | Incomplete | Blocks stale/scope-mismatched/non-finite/invalid/envelope-violating programs; straight and circular motion use exact swept cutter/shank/holder intervals against expanded fixture bounds, including sub-sample thin fixtures | Broader physical fixture proof records remain |
| UCCNC, Mach3, Mach4, GRBL, LinuxCNC posts | Unsafe | Declarative preview definitions with controller-specific extensions, safe startup blocks, comments, capabilities and tool-change policies | All remain `CertificationPending`; controller-specific parser/simulator evidence and air-cuts are required per controller |
| Runnable G-code saving | Unsafe | Preview generation exists; save commands require a `Certified` selected post | No built-in post is certified, so saving is visibly disabled in menu, palette and native macOS menu |
| Multi-cutter file isolation | Verified | Every runnable file contains one cutter only, regardless of filename extension; safe export groups consecutive operations only when they use the same cutter; combined multi-cutter output, missing tool selections, internal cutter changes and conflicting cutter identities/tool numbers are rejected | Postprocessor and ViewModel regressions prove isolation for manual and automatic machine profiles and verify independent split files |
| Parking | Verified | No park output by default; explicit enabled park uses valid `G53` machine coordinates | Controller golden coverage remains part of certification |
| Atomic export batch | Verified | Entire batch is staged and validated before commit; rollback on failure | Duplicate normalized/sanitized names are rejected |

## Libraries, machine setup and nesting

| Function | Status | Current behavior | Remaining gate |
|---|---|---|---|
| Tool/material/machine/preset libraries | Verified | Searchable CRUD for all four library types; every tool-assembly and machine-safety dimension is exposed; matching presets seed immutable operation snapshots; complete JSON and per-type lossless CSV import/export reject dangling references and unsafe deletion | Service, ViewModel and minimum-size headless UI fixtures pass |
| SQLite library storage | Verified | Database-backed persistence with explicit ordered schema versions | Legacy missing-column migration is covered by a real SQLite fixture |
| Machine and fixtures | Verified | UI exposes travel, rapid/tool-change timing, spindle, holder, spoilboard, park and complete fixture bounds/clearance; finite validation feeds safety | Physical values remain the operator's responsibility |
| Nesting | Verified | Enforces quantity, spacing, margins, finite dimensions, grain, permitted rotation, all-parts-placed, deterministic replacement and undo/redo restoration | Engine fixtures plus minimum-size pointer-select → define → optimize → accept → undo workflow pass |

## Release blockers

Production 1.0 is blocked while any **Unsafe** item remains, any controller lacks certification evidence, the proof jobs do not pass all required fixtures, or platform packaging/signing gates in `PRODUCTION-1.0-GATES.md` remain incomplete. A green unit-test suite alone is not a release claim.
