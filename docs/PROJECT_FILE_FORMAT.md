# V-Router Project File Format

Extension: `.cnccad`. Encoding: UTF-8 JSON. Current version: `9`.

Version 9 adds controller family, output unit, arc support and tool-change mode to immutable machine snapshots; it also adds surfacing and the production profile/pocket strategy fields. It retains all version-8 job, layer, entity, text, nesting, CAM, tool, machine and fixture data. Entity and CAM objects use `type` discriminators; no Avalonia UI objects are serialized. Versions 1–8 migrate in memory and are written as version 9 only on their next normal save. Loading rejects invalid files and versions newer than the application supports.

The optional `previewSettings` object stores the visualization-only 3D style, procedural material preset, stock-axis grain direction, and optional tint. Its absence defaults to the legacy Technical appearance, so existing v8 projects remain valid. These settings never alter CAM calculations or postprocessor output.

Calculated toolpath moves are intentionally not serialized. They are deterministic derived data and are recalculated from the saved geometry and CAM definitions when a project opens. Version 1 geometry-only files remain supported and open with safety setup marked incomplete. Millimetres remain canonical in the project; display and post output units are boundary choices.

The desktop writes projects through a temporary file in the destination directory and only replaces the target after serialization and a disk flush complete. Project filenames are normalized to the `.cnccad` extension.

Recovery snapshots use the same versioned serializer. They are stored in per-user application data after 30 seconds of dirty state, retain the latest three copies per job, and are removed after a successful normal save or explicit discard. Stock-removal heightfields remain transient and are never serialized.
