namespace Snail.MCP.Figma.Application.Editor;

/// <summary>Command to the live editor; only the adapter knows how it is packed on the wire.</summary>
public readonly record struct EditorCommand(string Name)
{
    /// <summary>Wire name of the command, the message type the plugin dispatches on.</summary>
    public override string ToString() => Name;
}

/// <summary>Every command the editor extension understands.</summary>
public static class EditorCommands
{
    /// <summary>Runs arbitrary JavaScript against the Plugin API inside the editor and returns its result.</summary>
    public static readonly EditorCommand ExecuteCode = new("EXECUTE_CODE");
    /// <summary>Moves a node to a new position on the canvas.</summary>
    public static readonly EditorCommand MoveNode = new("MOVE_NODE");
    /// <summary>Resizes a node to the given width and height.</summary>
    public static readonly EditorCommand ResizeNode = new("RESIZE_NODE");
    /// <summary>Replaces the fills of a node.</summary>
    public static readonly EditorCommand SetNodeFills = new("SET_NODE_FILLS");
    /// <summary>Replaces the strokes of a node.</summary>
    public static readonly EditorCommand SetNodeStrokes = new("SET_NODE_STROKES");
    /// <summary>Replaces the characters of a text node.</summary>
    public static readonly EditorCommand SetTextContent = new("SET_TEXT_CONTENT");
    /// <summary>Duplicates a node next to the original.</summary>
    public static readonly EditorCommand CloneNode = new("CLONE_NODE");
    /// <summary>Removes a node from the document.</summary>
    public static readonly EditorCommand DeleteNode = new("DELETE_NODE");
    /// <summary>Gives a node a new layer name.</summary>
    public static readonly EditorCommand RenameNode = new("RENAME_NODE");
    /// <summary>Creates a new node inside a parent node.</summary>
    public static readonly EditorCommand CreateChildNode = new("CREATE_CHILD_NODE");
    /// <summary>Sets the description shown on a component or style.</summary>
    public static readonly EditorCommand SetNodeDescription = new("SET_NODE_DESCRIPTION");
    /// <summary>Updates the component property values of an instance.</summary>
    public static readonly EditorCommand SetInstanceProperties = new("SET_INSTANCE_PROPERTIES");
    /// <summary>Fills one or more nodes with an image.</summary>
    public static readonly EditorCommand SetImageFill = new("SET_IMAGE_FILL");
    /// <summary>Renders a node to an image through the plugin's export, unsaved edits included.</summary>
    public static readonly EditorCommand CaptureScreenshot = new("CAPTURE_SCREENSHOT");
    /// <summary>Reads a component, component set or instance node.</summary>
    public static readonly EditorCommand GetComponent = new("GET_COMPONENT");
    /// <summary>Reads a component recursively in full, the tree code generation is built from.</summary>
    public static readonly EditorCommand DeepGetComponent = new("DEEP_GET_COMPONENT");
    /// <summary>Lists every local component of the file, the manifest of its design system.</summary>
    public static readonly EditorCommand GetLocalComponents = new("GET_LOCAL_COMPONENTS");
    /// <summary>Returns the variables and collections the plugin cached when it loaded.</summary>
    public static readonly EditorCommand GetVariablesData = new("GET_VARIABLES_DATA");
    /// <summary>Makes the plugin re-read all local variables and collections and send them again.</summary>
    public static readonly EditorCommand RefreshVariables = new("REFRESH_VARIABLES");
    /// <summary>Creates a variable in a collection.</summary>
    public static readonly EditorCommand CreateVariable = new("CREATE_VARIABLE");
    /// <summary>Sets the value of a variable in one mode.</summary>
    public static readonly EditorCommand UpdateVariable = new("UPDATE_VARIABLE");
    /// <summary>Deletes a variable.</summary>
    public static readonly EditorCommand DeleteVariable = new("DELETE_VARIABLE");
    /// <summary>Gives a variable a new name.</summary>
    public static readonly EditorCommand RenameVariable = new("RENAME_VARIABLE");
    /// <summary>Sets the description of a variable.</summary>
    public static readonly EditorCommand SetVariableDescription = new("SET_VARIABLE_DESCRIPTION");
    /// <summary>Creates a variable collection.</summary>
    public static readonly EditorCommand CreateVariableCollection = new("CREATE_VARIABLE_COLLECTION");
    /// <summary>Deletes a variable collection together with its variables.</summary>
    public static readonly EditorCommand DeleteVariableCollection = new("DELETE_VARIABLE_COLLECTION");
    /// <summary>Adds a mode to a variable collection.</summary>
    public static readonly EditorCommand AddMode = new("ADD_MODE");
    /// <summary>Gives a mode of a variable collection a new name.</summary>
    public static readonly EditorCommand RenameMode = new("RENAME_MODE");
    /// <summary>Places an instance of a component, with property overrides.</summary>
    public static readonly EditorCommand InstantiateComponent = new("INSTANTIATE_COMPONENT");
    /// <summary>Builds a component set with its variants in one step.</summary>
    public static readonly EditorCommand CreateComponentSet = new("CREATE_COMPONENT_SET");
    /// <summary>Reads the variant state machine of a component set and the differences between its variants.</summary>
    public static readonly EditorCommand AnalyzeComponentSet = new("ANALYZE_COMPONENT_SET");
    /// <summary>Adds a property to a component.</summary>
    public static readonly EditorCommand AddComponentProperty = new("ADD_COMPONENT_PROPERTY");
    /// <summary>Changes an existing component property.</summary>
    public static readonly EditorCommand EditComponentProperty = new("EDIT_COMPONENT_PROPERTY");
    /// <summary>Removes a property from a component.</summary>
    public static readonly EditorCommand DeleteComponentProperty = new("DELETE_COMPONENT_PROPERTY");
    /// <summary>Creates a slot inside a component.</summary>
    public static readonly EditorCommand CreateSlot = new("CREATE_SLOT");
    /// <summary>Lists the slots of a component or instance.</summary>
    public static readonly EditorCommand GetSlots = new("GET_SLOTS");
    /// <summary>Clones or moves a node into a slot of an instance.</summary>
    public static readonly EditorCommand AppendToSlot = new("APPEND_TO_SLOT");
    /// <summary>Reverts the content of an instance slot to the component default.</summary>
    public static readonly EditorCommand ResetSlot = new("RESET_SLOT");
    /// <summary>Lists all local text styles of the file.</summary>
    public static readonly EditorCommand GetTextStyles = new("GET_TEXT_STYLES");
    /// <summary>Reads the annotations of a node and, optionally, of its children.</summary>
    public static readonly EditorCommand GetAnnotations = new("GET_ANNOTATIONS");
    /// <summary>Writes annotations to a node.</summary>
    public static readonly EditorCommand SetAnnotations = new("SET_ANNOTATIONS");
    /// <summary>Lists the annotation categories available in the file.</summary>
    public static readonly EditorCommand GetAnnotationCategories = new("GET_ANNOTATION_CATEGORIES");
    /// <summary>Runs accessibility and design-quality checks over a node tree.</summary>
    public static readonly EditorCommand LintDesign = new("LINT_DESIGN");
    /// <summary>Runs a deep accessibility audit of a component set.</summary>
    public static readonly EditorCommand AuditComponentAccessibility = new("AUDIT_COMPONENT_ACCESSIBILITY");
    /// <summary>Acknowledges a console clear; the plugin keeps no buffer, the captured console lives on the server.</summary>
    public static readonly EditorCommand ClearConsole = new("CLEAR_CONSOLE");
    /// <summary>Reloads the plugin UI, which re-establishes its WebSocket connection.</summary>
    public static readonly EditorCommand ReloadUi = new("RELOAD_UI");

    /// <summary>Creates a sticky note on a FigJam board.</summary>
    public static readonly EditorCommand CreateSticky = new("CREATE_STICKY");
    /// <summary>Creates several sticky notes on a FigJam board in one step.</summary>
    public static readonly EditorCommand CreateStickies = new("CREATE_STICKIES");
    /// <summary>Connects two nodes of a FigJam board with a connector.</summary>
    public static readonly EditorCommand CreateConnector = new("CREATE_CONNECTOR");
    /// <summary>Creates a labelled shape on a FigJam board.</summary>
    public static readonly EditorCommand CreateShapeWithText = new("CREATE_SHAPE_WITH_TEXT");
    /// <summary>Creates a section on a FigJam board.</summary>
    public static readonly EditorCommand CreateSection = new("CREATE_SECTION");
    /// <summary>Creates a table filled with data on a FigJam board.</summary>
    public static readonly EditorCommand CreateTable = new("CREATE_TABLE");
    /// <summary>Creates a code block on a FigJam board.</summary>
    public static readonly EditorCommand CreateCodeBlock = new("CREATE_CODE_BLOCK");
    /// <summary>Reads every FigJam node on the current page.</summary>
    public static readonly EditorCommand GetBoardContents = new("GET_BOARD_CONTENTS");
    /// <summary>Reads the graph of connectors between the nodes of a FigJam board.</summary>
    public static readonly EditorCommand GetConnections = new("GET_CONNECTIONS");

    /// <summary>Lists all slides of the presentation.</summary>
    public static readonly EditorCommand ListSlides = new("LIST_SLIDES");
    /// <summary>Reads the node tree of a slide.</summary>
    public static readonly EditorCommand GetSlideContent = new("GET_SLIDE_CONTENT");
    /// <summary>Reads the two-dimensional grid the slides are laid out in.</summary>
    public static readonly EditorCommand GetSlideGrid = new("GET_SLIDE_GRID");
    /// <summary>Reads the transition of a slide.</summary>
    public static readonly EditorCommand GetSlideTransition = new("GET_SLIDE_TRANSITION");
    /// <summary>Reports the slide currently focused in the editor.</summary>
    public static readonly EditorCommand GetFocusedSlide = new("GET_FOCUSED_SLIDE");
    /// <summary>Creates a new slide.</summary>
    public static readonly EditorCommand CreateSlide = new("CREATE_SLIDE");
    /// <summary>Deletes a slide.</summary>
    public static readonly EditorCommand DeleteSlide = new("DELETE_SLIDE");
    /// <summary>Clones a slide.</summary>
    public static readonly EditorCommand DuplicateSlide = new("DUPLICATE_SLIDE");
    /// <summary>Rearranges the slides to match a grid of slide ids.</summary>
    public static readonly EditorCommand ReorderSlides = new("REORDER_SLIDES");
    /// <summary>Sets the transition of a slide.</summary>
    public static readonly EditorCommand SetSlideTransition = new("SET_SLIDE_TRANSITION");
    /// <summary>Switches the presentation between grid view and single-slide view.</summary>
    public static readonly EditorCommand SetSlidesViewMode = new("SET_SLIDES_VIEW_MODE");
    /// <summary>Navigates the editor to a slide.</summary>
    public static readonly EditorCommand FocusSlide = new("FOCUS_SLIDE");
    /// <summary>Toggles whether a slide is skipped in the presentation.</summary>
    public static readonly EditorCommand SkipSlide = new("SKIP_SLIDE");
    /// <summary>Sets the background colour of a slide.</summary>
    public static readonly EditorCommand SetSlideBackground = new("SET_SLIDE_BACKGROUND");
    /// <summary>Adds a text node to a slide.</summary>
    public static readonly EditorCommand AddTextToSlide = new("ADD_TEXT_TO_SLIDE");
    /// <summary>Adds a shape to a slide.</summary>
    public static readonly EditorCommand AddShapeToSlide = new("ADD_SHAPE_TO_SLIDE");
}
