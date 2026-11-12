using Avalonia;
using Avalonia.Controls;

namespace CncCad.App;

public sealed record CommandDescriptor(
    string Id,
    string Title,
    string Summary,
    string HowTo,
    string Requirement = "",
    string Shortcut = "",
    string Result = "")
{
    public string Tooltip(bool available = true)
    {
        var lines = new List<string> { Title, Summary };
        if (!available && !string.IsNullOrWhiteSpace(Requirement)) lines.Add($"Unavailable: {Requirement}");
        if (!string.IsNullOrWhiteSpace(Shortcut)) lines.Add($"Shortcut: {Shortcut}");
        lines.Add("Right-click or press F1 for detailed help.");
        return string.Join("\n", lines);
    }
}

public static class CommandHelpCatalog
{
    private static readonly IReadOnlyDictionary<string, CommandDescriptor> Items = new CommandDescriptor[]
    {
        D("file.new", "New job", "Create a new CNC job and define its material and work zero.", "Enter the job name, material dimensions, XY datum, Z zero and clearance, then choose Create Job.", shortcut:"Ctrl+N", result:"Starts a clean, unsaved project."),
        D("file.open", "Open project", "Open an existing .cnccad project.", "Choose a V-Router project. Versioned projects are migrated when necessary without changing the source file until you save.", shortcut:"Ctrl+O"),
        D("file.save", "Save project", "Atomically save the current project.", "Saves geometry, layers, editable toolpath definitions, machine/material snapshots and job settings. Toolpath simulation is recalculated and is not stored.", "Create or open a job first.", "Ctrl+S"),
        D("edit.undo", "Undo", "Undo the most recent completed CAD action.", "Each completed Apply or direct edit is one undo step. Toolpath preview and view changes are not CAD history entries.", "Complete an editable action first.", "Ctrl+Z"),
        D("edit.redo", "Redo", "Restore the most recently undone CAD action.", "Redo is available until a different edit creates a new history branch.", "Undo an action first.", "Ctrl+Shift+Z"),
        D("help.mode", "Command help mode", "Click any command to learn what it does without running it.", "Turn this on, then click a toolbar, palette, task or viewport command. Press Escape to leave help mode.", shortcut:"F1 opens help for the focused command."),
        D("draw.select", "Select and move", "Select, marquee and reposition editable geometry.", "Click an object, drag a selection box, or hold Ctrl/Shift to extend the selection. Drag selected geometry to move it.", shortcut:"Space"),
        D("draw.line", "Line", "Draw a straight line between two points.", "Click the start and end points on the canvas, or enter exact coordinates in Numeric Construction.", result:"Creates one open line vector and one undo entry."),
        D("draw.polyline", "Polyline", "Draw a connected sequence of line segments.", "Click each node. Press Enter to finish or C to close the contour.", result:"Creates one open or closed polyline."),
        D("draw.rectangle", "Rectangle", "Draw a closed rectangular vector.", "Drag corner-to-corner on the canvas or enter an origin, width and height.", result:"Creates a profile- and pocket-ready closed contour."),
        D("draw.circle", "Circle", "Draw a circle from its centre and radius.", "Click a centre and radius point, or enter an exact radius or diameter.", result:"Creates a closed circle that can also supply a drilling centre."),
        D("draw.arc", "Three-point arc", "Draw an arc through a start, through-point and end.", "Click the three points in order. Non-collinear points are required.", result:"Creates an open arc vector."),
        D("draw.drill", "Drill point", "Place an explicit drilling location.", "Click once for each hole centre. Select the points before creating a Drill operation.", result:"Creates persisted drill-point geometry."),
        D("draw.text", "Shapes and text", "Create parametric shapes, editable text and text on a selected guide.", "Choose a shape or enter text settings. When one circle, arc or open path is selected, new text can follow that guide."),
        D("edit.position", "Position", "Move selected geometry to an exact lower-left X/Y position.", "Select geometry, enter X and Y, then Apply; or choose Center to place it on the material.", "Select one or more editable vectors."),
        D("edit.transform", "Transform and align", "Scale, rotate, mirror, align or distribute selected vectors.", "Set the pivot and transform values. Alignment needs two or more objects; distribution needs three or more.", "Select editable vectors."),
        D("edit.copy", "Copy", "Copy selected geometry to the internal clipboard.", "Select one or more editable vectors, then copy. The source is not changed.", "Select one or more vectors.", "Ctrl+C"),
        D("edit.paste", "Paste", "Paste a copy of the internal clipboard geometry.", "Copied geometry is offset so it remains visible and becomes the current selection.", "Copy geometry first.", "Ctrl+V"),
        D("edit.duplicate", "Duplicate", "Create an offset duplicate of the current selection.", "Select geometry and run Duplicate. Repeat to create additional offset copies.", "Select one or more vectors.", "Ctrl+D"),
        D("edit.delete", "Delete", "Remove the selected geometry.", "Referenced toolpaths become invalid and must be edited or recalculated after deleting their geometry.", "Select one or more vectors.", "Delete"),
        D("edit.nodes", "Edit vectors and nodes", "Join, open, close, trim, fillet, chamfer, group or edit nodes.", "Open the task page, choose the required topology action and provide any tolerance or node settings.", "Select compatible editable geometry."),
        D("edit.trim", "Scissors trim", "Remove the portion of a vector bounded by intersections.", "Activate Trim, hover a crossing-bounded segment, then click the highlighted removable portion.", "Create intersecting geometry first."),
        D("edit.repair", "Vector repair", "Find and repair duplicate, tiny or nearly coincident vector defects.", "Set tolerance and minimum segment length, Analyze first, review the issues, then Apply Repairs.", "Create or import geometry first."),
        D("edit.offset", "Offset and layout", "Create offsets, centre geometry and distribute selections.", "Select closed geometry for offsets. Signed distance controls inward/outward direction.", "Select compatible vectors."),
        D("context.layers", "Layers", "Organize geometry by named, coloured, lockable layers.", "Create or select a layer, then activate, reorder, hide, lock, select its contents or move the current selection into it."),
        D("context.appearance", "Vector appearance", "Change vector colour without changing machining geometry.", "Choose a palette or custom colour, then apply it to the selection or make it the colour for new vectors.", "Select vectors to recolour."),
        D("context.snapping", "Snapping", "Control grid, endpoint, midpoint and centre snapping.", "Enable the desired snap targets and set a positive grid spacing. Changes apply immediately to the 2D canvas."),
        D("context.nesting", "Material nesting", "Arrange repeated closed parts on one or more sheets.", "Select closed outer contours, add parts and quantities, optimize, review, then Accept to create placed instances."),
        D("context.job", "Job setup", "Edit stock dimensions, work zero and safe clearance.", "Changes can make existing toolpaths stale; recalculate and rerun preflight afterward.", "Create or open a job first."),
        D("context.import", "Import SVG or DXF", "Import supported vector geometry with scale, offset and layer options.", "Choose a file, review detected units and warnings, set placement options, then Import.", "Create or open a job first."),
        D("toolpath.profile", "Profile toolpath", "Machine on, inside or outside closed contours.", "Select closed contours, then set depths, tool, side, direction, allowance, start point, entry, tabs, feeds and name before Calculate.", "Select one or more closed vectors."),
        D("toolpath.pocket", "Pocket toolpath", "Clear material inside closed boundaries while preserving nested islands.", "Select the outer boundary and any nested islands, then set depths, stepover, allowance, direction and entry strategy.", "Select one or more closed vectors."),
        D("toolpath.drill", "Drill toolpath", "Drill circle centres or explicit drill points.", "Select circles or drill points, choose simple or peck cycle and ordering, then set depth, retract, dwell, plunge and RPM.", "Select circles or drill points."),
        D("toolpath.engrave", "Engraving toolpath", "Follow open or closed vector centre-lines at a shallow depth.", "Select vectors or centre-line text, choose direction and depths, then apply the engraving tool and cutting values.", "Select machinable vectors or text."),
        D("toolpath.vcarve", "V-carve toolpath", "Cut closed outlines with a V-bit to a controlled maximum depth.", "Select closed outline text or vectors, apply a V-bit, then set maximum depth, stepover and flat-clear behaviour.", "Select closed vectors and choose a V-bit."),
        D("toolpath.preflight", "Safety preflight", "Validate machine, tool, motion and setup data before export.", "Resolve every error before preview or export. Warnings should be reviewed before machining.", "Create at least one toolpath."),
        D("toolpath.preview", "Preview toolpaths", "Simulate 2.5D stock removal for selected or visible operations.", "Recalculate the operations, run preflight, then preview. Use playback speed, reset and cutter visibility controls.", "Create a valid toolpath."),
        D("toolpath.export", "Save toolpaths", "Generate validated controller programs using atomic file writes.", "Choose selected, combined or separate output; review generated text and preflight, then choose the destination.", "Create valid operations and pass preflight."),
        D("view.fit", "Fit job", "Zoom the active view to the material or 3D stock.", "Use this after panning or zooming away from the job.", shortcut:"F"),
        D("view.grid", "Grid", "Show or hide the 2D drawing grid.", "The grid spacing is configured in the Snapping task page."),
        D("view.snap", "Snapping", "Enable or disable all configured snap targets.", "Individual grid, endpoint, midpoint and centre targets are configured in the Snapping task page."),
        D("view.appearance", "Preview appearance", "Switch the 3D stock between a technical shaded surface and a procedural material preview.", "Click to toggle Technical and Material. Open the adjacent appearance menu to choose Hardwood, Softwood, Plywood or MDF, set the grain axis, apply a tint, or reset the appearance. This never changes toolpaths or G-code.", "Create or open a job and show a 3D viewport.", result:"Stores the visual appearance with the project."),
    }.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

    private static CommandDescriptor D(string id, string title, string summary, string howTo, string requirement = "", string shortcut = "", string result = "") => new(id, title, summary, howTo, requirement, shortcut, result);
    public static IReadOnlyCollection<CommandDescriptor> All => Items.Values.ToArray();
    public static bool TryGet(string? id, out CommandDescriptor descriptor) => Items.TryGetValue(id ?? "", out descriptor!);
    public static CommandDescriptor GetRequired(string id) => Items.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException($"Unknown command help key '{id}'.");
}

public static class CommandHelp
{
    public static readonly AttachedProperty<string?> KeyProperty = AvaloniaProperty.RegisterAttached<Control, Control, string?>("Key");

    static CommandHelp() => KeyProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));

    public static string? GetKey(Control control) => control.GetValue(KeyProperty);
    public static void SetKey(Control control, string? value) => control.SetValue(KeyProperty, value);

    public static CommandDescriptor? DescriptorFor(Control control)
    {
        if (CommandHelpCatalog.TryGet(GetKey(control), out var descriptor)) return descriptor;
        var tip = ToolTip.GetTip(control)?.ToString();
        var title = control switch
        {
            Button button when button.Content is string content => content,
            MenuItem menu when menu.Header is string header => header.Replace("_", ""),
            _ => tip
        };
        return string.IsNullOrWhiteSpace(title) ? null : new("ui.generic", title!, tip ?? $"Use the {title} command.", tip ?? $"Choose {title} to perform this action.");
    }

    public static void Refresh(Control control)
    {
        if (DescriptorFor(control) is not { } descriptor) return;
        ToolTip.SetTip(control, descriptor.Tooltip(control.IsEffectivelyEnabled));
        control.SetValue(ToolTip.ShowOnDisabledProperty, true);
        control.SetValue(ToolTip.ShowDelayProperty, 450);
    }
}
