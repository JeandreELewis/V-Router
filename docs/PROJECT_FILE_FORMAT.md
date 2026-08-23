# CncCad Project File Format

Extension: `.cnccad`. Encoding: UTF-8 JSON. Current version: `1`.

Version 1 serializes job settings, layers and typed CAD entities. Entity objects use a `type` discriminator such as `rectangle`, `line` or `circle`; no Avalonia objects are serialized. Loading rejects versions newer than the application supports.

The desktop UI can create and save jobs through Save and Save As. Version 1 currently persists the complete job setup, layers and geometry. Toolpath serialization and embedded tool snapshots are planned for version 2; recalculable toolpaths are therefore not yet restored from a saved project.
