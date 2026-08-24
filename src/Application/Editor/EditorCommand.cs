namespace Snail.MCP.Figma.Application.Editor;

/// <summary>Command to the live editor; only the adapter knows how it is packed on the wire.</summary>
public readonly record struct EditorCommand(string Name)
{
    public override string ToString() => Name;
}

/// <summary>Every command the editor extension understands.</summary>
public static class EditorCommands
{
    public static readonly EditorCommand ExecuteCode = new("EXECUTE_CODE");
    public static readonly EditorCommand MoveNode = new("MOVE_NODE");
    public static readonly EditorCommand ResizeNode = new("RESIZE_NODE");
    public static readonly EditorCommand SetNodeFills = new("SET_NODE_FILLS");
    public static readonly EditorCommand SetNodeStrokes = new("SET_NODE_STROKES");
    public static readonly EditorCommand SetNodeOpacity = new("SET_NODE_OPACITY");
    public static readonly EditorCommand SetNodeCornerRadius = new("SET_NODE_CORNER_RADIUS");
    public static readonly EditorCommand SetTextContent = new("SET_TEXT_CONTENT");
    public static readonly EditorCommand CloneNode = new("CLONE_NODE");
    public static readonly EditorCommand DeleteNode = new("DELETE_NODE");
    public static readonly EditorCommand RenameNode = new("RENAME_NODE");
    public static readonly EditorCommand CreateChildNode = new("CREATE_CHILD_NODE");
    public static readonly EditorCommand SetNodeDescription = new("SET_NODE_DESCRIPTION");
    public static readonly EditorCommand SetInstanceProperties = new("SET_INSTANCE_PROPERTIES");
    public static readonly EditorCommand SetImageFill = new("SET_IMAGE_FILL");
    public static readonly EditorCommand CaptureScreenshot = new("CAPTURE_SCREENSHOT");
    public static readonly EditorCommand GetComponent = new("GET_COMPONENT");
    public static readonly EditorCommand DeepGetComponent = new("DEEP_GET_COMPONENT");
    public static readonly EditorCommand GetFileInfo = new("GET_FILE_INFO");
    public static readonly EditorCommand GetLocalComponents = new("GET_LOCAL_COMPONENTS");
    public static readonly EditorCommand GetVariablesData = new("GET_VARIABLES_DATA");
    public static readonly EditorCommand RefreshVariables = new("REFRESH_VARIABLES");
    public static readonly EditorCommand CreateVariable = new("CREATE_VARIABLE");
    public static readonly EditorCommand UpdateVariable = new("UPDATE_VARIABLE");
    public static readonly EditorCommand DeleteVariable = new("DELETE_VARIABLE");
    public static readonly EditorCommand RenameVariable = new("RENAME_VARIABLE");
    public static readonly EditorCommand SetVariableDescription = new("SET_VARIABLE_DESCRIPTION");
    public static readonly EditorCommand CreateVariableCollection = new("CREATE_VARIABLE_COLLECTION");
    public static readonly EditorCommand DeleteVariableCollection = new("DELETE_VARIABLE_COLLECTION");
    public static readonly EditorCommand AddMode = new("ADD_MODE");
    public static readonly EditorCommand RenameMode = new("RENAME_MODE");
    public static readonly EditorCommand InstantiateComponent = new("INSTANTIATE_COMPONENT");
    public static readonly EditorCommand CreateComponentSet = new("CREATE_COMPONENT_SET");
    public static readonly EditorCommand AnalyzeComponentSet = new("ANALYZE_COMPONENT_SET");
    public static readonly EditorCommand AddComponentProperty = new("ADD_COMPONENT_PROPERTY");
    public static readonly EditorCommand EditComponentProperty = new("EDIT_COMPONENT_PROPERTY");
    public static readonly EditorCommand DeleteComponentProperty = new("DELETE_COMPONENT_PROPERTY");
    public static readonly EditorCommand CreateSlot = new("CREATE_SLOT");
    public static readonly EditorCommand GetSlots = new("GET_SLOTS");
    public static readonly EditorCommand AppendToSlot = new("APPEND_TO_SLOT");
    public static readonly EditorCommand ResetSlot = new("RESET_SLOT");
    public static readonly EditorCommand GetTextStyles = new("GET_TEXT_STYLES");
    public static readonly EditorCommand GetAnnotations = new("GET_ANNOTATIONS");
    public static readonly EditorCommand SetAnnotations = new("SET_ANNOTATIONS");
    public static readonly EditorCommand GetAnnotationCategories = new("GET_ANNOTATION_CATEGORIES");
    public static readonly EditorCommand LintDesign = new("LINT_DESIGN");
    public static readonly EditorCommand AuditComponentAccessibility = new("AUDIT_COMPONENT_ACCESSIBILITY");
    public static readonly EditorCommand ClearConsole = new("CLEAR_CONSOLE");
    public static readonly EditorCommand ReloadUi = new("RELOAD_UI");

    public static readonly EditorCommand CreateSticky = new("CREATE_STICKY");
    public static readonly EditorCommand CreateStickies = new("CREATE_STICKIES");
    public static readonly EditorCommand CreateConnector = new("CREATE_CONNECTOR");
    public static readonly EditorCommand CreateShapeWithText = new("CREATE_SHAPE_WITH_TEXT");
    public static readonly EditorCommand CreateSection = new("CREATE_SECTION");
    public static readonly EditorCommand CreateTable = new("CREATE_TABLE");
    public static readonly EditorCommand CreateCodeBlock = new("CREATE_CODE_BLOCK");
    public static readonly EditorCommand GetBoardContents = new("GET_BOARD_CONTENTS");
    public static readonly EditorCommand GetConnections = new("GET_CONNECTIONS");

    public static readonly EditorCommand ListSlides = new("LIST_SLIDES");
    public static readonly EditorCommand GetSlideContent = new("GET_SLIDE_CONTENT");
    public static readonly EditorCommand GetSlideGrid = new("GET_SLIDE_GRID");
    public static readonly EditorCommand GetSlideTransition = new("GET_SLIDE_TRANSITION");
    public static readonly EditorCommand GetFocusedSlide = new("GET_FOCUSED_SLIDE");
    public static readonly EditorCommand CreateSlide = new("CREATE_SLIDE");
    public static readonly EditorCommand DeleteSlide = new("DELETE_SLIDE");
    public static readonly EditorCommand DuplicateSlide = new("DUPLICATE_SLIDE");
    public static readonly EditorCommand ReorderSlides = new("REORDER_SLIDES");
    public static readonly EditorCommand SetSlideTransition = new("SET_SLIDE_TRANSITION");
    public static readonly EditorCommand SetSlidesViewMode = new("SET_SLIDES_VIEW_MODE");
    public static readonly EditorCommand FocusSlide = new("FOCUS_SLIDE");
    public static readonly EditorCommand SkipSlide = new("SKIP_SLIDE");
    public static readonly EditorCommand SetSlideBackground = new("SET_SLIDE_BACKGROUND");
    public static readonly EditorCommand AddTextToSlide = new("ADD_TEXT_TO_SLIDE");
    public static readonly EditorCommand AddShapeToSlide = new("ADD_SHAPE_TO_SLIDE");
}
