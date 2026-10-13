namespace Snail.MCP.Figma.Tools.Prompts;

/// <summary>Tool description catalog — what the model reads before calling; a contract with the agent guarded by <c>ToolCatalogConventionTests</c>.</summary>
public static class ToolDescriptions
{
    /// <summary>Parameter descriptions shared by several tools.</summary>
    public static class Parameters
    {
        /// <summary>The <c>source</c> parameter of the READ channel; values are <see cref="ReadSource"/>.</summary>
        public const string Source =
            "Source: auto (the default — the plugin when a live session for the file exists, otherwise REST), " +
            "rest (Figma REST only: the last SAVED state, spends quota), plugin (the plugin only).";

        /// <summary>The <c>deliver</c> parameter of the picture tools; the megabyte ceiling must stay equal to
        /// <see cref="Snail.MCP.Figma.Application.Editor.ImageLimits.MaxInlineMegabytes"/>, which a catalog test checks.</summary>
        public const string Deliver =
            "What comes back: path (the default) saves the picture beside the server and answers with the file path, " +
            "which suits a human working in the same machine; both answers with the path AND the picture itself as image " +
            "content, which is what a caller in another process or container needs, since a path there opens nothing. " +
            "Only PNG and JPG travel as content, and only up to 2 MB — a bigger one still comes back as a path.";

        /// <summary>The <c>filePath</c> parameter of the image tools; the number of megabytes must stay equal to
        /// <see cref="Snail.MCP.Figma.Application.Editor.ImageLimits.MaxFileMegabytes"/>, which a catalog test checks.</summary>
        public const string ImageFilePath =
            "Absolute path to a picture on the machine running the server; ~ is expanded. The server reads the file and " +
            "sends the bytes over the bridge itself, so the picture never travels through the context — this is the way " +
            "to place one at full resolution instead of shrinking it to fit a base64 argument. " +
            "PNG, JPG, GIF and WebP up to 10 MB.";
    }

    /// <summary><c>CommentTools</c>.</summary>
    public static class Comment
    {
        /// <summary><c>figma_get_comments</c></summary>
        public const string GetComments =
            "File comments: discussion threads with the author, the text, the time and the node the comment is " +
            "pinned to. includeResolved=true also shows resolved threads.";

        /// <summary><c>figma_post_comment</c></summary>
        public const string PostComment =
            "Leaves a comment in the file, optionally pinned to a node. Fits after " +
            "figma_check_design_parity, when the code is the source of truth and the designer has to hear about the mismatch. " +
            "replyToCommentId turns the comment into a reply in an existing thread. " +
            "@ mentions are a Figma UI feature: in the text they stay plain words.";

        /// <summary><c>figma_delete_comment</c></summary>
        public const string DeleteComment = "Deletes a comment by id. figma_get_comments provides the ids.";
    }

    /// <summary><c>ComponentTools</c>.</summary>
    public static class Component
    {
        /// <summary><c>figma_search_components</c></summary>
        public const string SearchComponents =
            "Searches components in the open file by a substring of the name. Returns the ids and keys " +
            "needed by figma_instantiate_component. Ids live only inside the current session — " +
            "do not reuse them from past conversations. Requires the plugin.";

        /// <summary><c>figma_instantiate_component</c></summary>
        public const string InstantiateComponent =
            "Places a component instance on the canvas. The component is addressed by key (componentKey) or by node id " +
            "(nodeId). Take keys and ids from figma_search_components in the same session. " +
            "overrides is JSON with the instance property values. Requires the plugin.";

        /// <summary><c>figma_create_component_set</c></summary>
        public const string CreateComponentSet =
            "Builds a component set out of existing components or from a property matrix. " +
            "componentIds is a JSON array of ids; properties is JSON of the form {\"Size\":[\"S\",\"M\"],\"State\":[\"Default\",\"Hover\"]}. " +
            "Large matrices take a while to build — the timeout grows with the number of variants. Requires the plugin.";

        /// <summary><c>figma_analyze_component_set</c></summary>
        public const string AnalyzeComponentSet =
            "Breaks a variant set down: which property axes exist, which combinations are there and which are missing. " +
            "Requires the plugin.";

        /// <summary><c>figma_arrange_component_set</c></summary>
        public const string ArrangeComponentSet =
            "Lays the variants inside a set out in a grid: rows are the first property axis, columns the second. " +
            "Makes a set readable after variants were added in bulk. Requires the plugin.";

        /// <summary><c>figma_add_component_property</c></summary>
        public const string AddComponentProperty =
            "Adds a property to a component. type: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT, SLOT. " +
            "Every type except SLOT needs a default value. Requires the plugin.";

        /// <summary><c>figma_edit_component_property</c></summary>
        public const string EditComponentProperty = "Changes the default value of a component property. Requires the plugin.";

        /// <summary><c>figma_delete_component_property</c></summary>
        public const string DeleteComponentProperty = "Deletes a component property. Every instance loses the matching override. Requires the plugin.";

        /// <summary><c>figma_get_component_details</c></summary>
        public const string GetComponentDetails =
            "Reads a component straight from the plugin instead of REST: the current state of the file is visible, unsaved edits included. " +
            "deep=true returns the tree with variable bindings, reactions and constraints. Requires the plugin.";
    }

    /// <summary><c>DesignCodeTools</c>.</summary>
    public static class DesignCode
    {
        /// <summary><c>figma_get_component_for_development</c></summary>
        public const string GetComponentForDevelopment =
            "Component data for implementing it in code: the tree with variable bindings, sizes, " +
            "auto layout and text properties, plus a ready reconstruction specification. " +
            "Call it on requests like 'build this' or 'make a React component'. " +
            "When the plugin is running the data comes from it — it is fresher than REST. " +
            "For metadata alone figma_get_component is enough, for a picture use figma_get_component_image.";

        /// <summary><c>figma_get_component_for_development_deep</c></summary>
        public const string GetComponentForDevelopmentDeep =
            "The same as figma_get_component_for_development, but with a deep breakdown through the plugin: " +
            "states, reactions, slots, Dev Mode annotations. Slower and heavier — take it when the plain breakdown was not enough. " +
            "Requires the plugin.";

        /// <summary><c>figma_extract_style_values</c></summary>
        public const string ExtractStyleValues =
            "Pulls token-like values out of the DESIGN — colors, typography, spacing, radii — " +
            "and collapses duplicates. Works on any Figma plan, unlike figma_get_variables. " +
            "A good starting point when the file has no variables yet. " +
            "Not to be confused with figma_ds_extract_tokens: that one mines tokens from the CODEBASE.";

        /// <summary><c>figma_check_design_parity</c></summary>
        public const string CheckDesignParity =
            "Compares a component in Figma with what is implemented in code. codeSpec is JSON with what you read " +
            "out of the sources: {\"fills\":[…],\"fontSize\":14,\"paddingLeft\":16,\"variants\":[…]}. " +
            "Returns a conformance score and the list of mismatches on both sides. Read the component code first.";

        /// <summary><c>figma_generate_component_doc</c></summary>
        public const string GenerateComponentDoc =
            "Assembles component documentation in Markdown: contents, variants, colors, typography, spacing, " +
            "the Figma description and the annotations. codeInfo is optional JSON with data from the sources " +
            "(props, CVA variants, file paths); it lands in the code section.";
    }

    /// <summary><c>DesignSystemExtractionTools</c>.</summary>
    public static class DesignSystemExtraction
    {
        /// <summary><c>figma_ds_analyze</c></summary>
        public const string DsAnalyze =
            "The first step of design system extraction: it breaks down the codebase of a production application. " +
            "It detects the framework (React/Next/Astro/Angular/Web Components), the styling approaches " +
            "(Tailwind v3/v4, CSS Modules, SCSS, Emotion, styled-components) and the vendor layers " +
            "(shadcn/ui, Radix, MUI, Chakra and the rest); it collects a component inventory with a classification " +
            "(vendored / wrapped / pure-vendor / bespoke), the props contract, the real usage count " +
            "(the porting rank), the prop values seen at call sites (variant inference) and duplicate detection. " +
            "Several targets means the shared design language of several applications at once. " +
            "The full manifest is written to <outDir>/.extraction/analysis.json, the response carries a summary.";

        /// <summary><c>figma_ds_extract_tokens</c></summary>
        public const string DsExtractTokens =
            "Mines design tokens FROM THE CODEBASE into canonical DTCG (plus, optionally, CSS variables, " +
            "Tailwind, SCSS, TypeScript). It starts with what is declared: the :root and @theme custom properties " +
            "(light and dark themes become modes of one token), SCSS variables, tailwind.config values, " +
            "shadcn HSL triples; then it promotes repeated raw values (colors, spacing, radii, font sizes) " +
            "above the frequency threshold. Every token carries its origin in $extensions: file, line, confidence, frequency. " +
            "The names are structural, as found — semantic renaming is a separate pass with the team. " +
            "The result is immediately usable by figma_import_tokens. Requires a figma_ds_analyze run over the same outDir. " +
            "Values are pulled out of the DESIGN by a different tool — figma_extract_style_values.";

        /// <summary><c>figma_ds_scaffold</c></summary>
        public const string DsScaffold =
            "Assembles the scaffold of a design system package in outDir from a finished analysis and tokens: package.json " +
            "(the application framework goes into peer dependencies), the src/components layout, token files " +
            "through the shared formatter engine, MDX showcases (tokens, typography, icons) and a README with the workflow. " +
            "Storybook itself is NOT installed: after the scaffold run `npm create storybook@latest` in outDir — its CLI " +
            "detects the framework and installs the current version itself. By default it only fills gaps: existing " +
            "files are skipped unless force is passed. Token files are always refreshed.";

        /// <summary><c>figma_ds_setup_storybook</c></summary>
        public const string DsSetupStorybook =
            "Wires a freshly installed Storybook workshop to the extracted design system. Run it AFTER " +
            "`npm create storybook@latest` in outDir. It generates .storybook/preview.css (a Tailwind entry point " +
            "with the mined tokens, the @theme utility map of the source application, its @utility, @layer base and @font-face, " +
            "with a dark variant covering both .dark and [data-theme]), copies self-hosted fonts " +
            "into staticDirs and edits main.js (the tailwind plugin for vite and the automatic JSX runtime — without it stories " +
            "fail on 'React is not defined') and preview.jsx (the preview.css import plus a theme panel with the mined modes). " +
            "Running it again is safe; everything that cannot be fixed safely comes back as a list of manual steps.";

        /// <summary><c>figma_ds_extract_component</c></summary>
        public const string DsExtractComponent =
            "Breaks ONE component of the inventory down deeply for porting: the source (with a size cap), " +
            "the local import closure (relative imports worth following), the props contract, " +
            "the variants seen at call sites, the classification against the vendor, the styling touchpoints " +
            "(classes, CSS module imports, custom properties in use) and a ready " +
            "CSF3 story scaffold with variant stories taken from real usage. " +
            "Next you port the component into <outDir>/src/components/<Name>/ and record the progress with figma_ds_status.";

        /// <summary><c>figma_ds_verify</c></summary>
        public const string DsVerify =
            "Deterministic checks of the extracted design system package — the gate before handover or the trip into Figma. " +
            "It checks: tokens.json parses as DTCG and every alias resolves (importable); the generated " +
            "token files carry no quoted CSS functional expressions (they kill transitions); every var() " +
            "consumed in component and preview CSS resolves in the workshop; every component directory has " +
            "a story file and an index barrel; every portable component of the inventory has a status. " +
            "It also reports the readiness of the return trip into Figma with the exact figma_import_tokens call.";

        /// <summary><c>figma_ds_status</c></summary>
        public const string DsStatus =
            "Reads and updates the progress of design system porting. It lives in <outDir>/.extraction/status.json, " +
            "so long work survives session boundaries. A call with outDir alone returns a summary; " +
            "pass component and status to record the state of a component.";
    }

    /// <summary><c>DesignSystemKitTools</c>.</summary>
    public static class DesignSystemKit
    {
        /// <summary><c>figma_get_design_system_kit</c></summary>
        public const string GetDesignSystemKit =
            "The MAIN tool for capturing a design system: it replaces separate calls to figma_get_styles, " +
            "figma_get_variables and figma_get_component. It returns tokens, components and styles in one response " +
            "with adaptive compression to the size of the system. Inside are the visual specifications of the components " +
            "(exact colors, spacing, typography, layout), token values per mode and resolved style values. " +
            "Take it instead of the separate tools so the context does not overflow. " +
            "Variant specifications are delta-encoded: the base variant carries the full visualSpec, its neighbours only the differences. " +
            "Variables are read through the plugin and work on any plan; the Variables REST API is the fallback and needs Enterprise.";
    }

    /// <summary><c>DiagnosticsTools</c>.</summary>
    public static class Diagnostics
    {
        /// <summary><c>csharp_diagnose</c></summary>
        public const string Diagnose =
            "Self-check of the Snail.MCP.Figma server: the active configuration, the presence of the Snail Bridge JS plugin " +
            "and the state of the cache. Call it when you suspect the server is misconfigured.";
    }

    /// <summary><c>FigJamTools</c>.</summary>
    public static class FigJam
    {
        /// <summary><c>figjam_create_sticky</c></summary>
        public const string CreateSticky =
            "Creates a sticky note on a FigJam board. Colors: YELLOW, BLUE, GREEN, PINK, ORANGE, PURPLE, RED, LIGHT_GRAY, GRAY " +
            "(YELLOW by default). Works in FigJam files only. Requires the plugin.";

        /// <summary><c>figjam_create_stickies</c></summary>
        public const string CreateStickies =
            "Creates a batch of stickies in one go — this is how a board is filled from structured data " +
            "(meeting notes, brainstorm ideas). stickies is a JSON array of the form " +
            "[{\"text\":\"Idea\",\"color\":\"BLUE\",\"x\":0,\"y\":0}], at most 200 items. Requires the plugin.";

        /// <summary><c>figjam_create_connector</c></summary>
        public const string CreateConnector =
            "Connects two nodes with a line: this is how flowcharts, diagrams and relation maps are built. The nodes must " +
            "already be on the board — take the ids from the creation results. Magnets: AUTO (the default), " +
            "TOP, BOTTOM, LEFT, RIGHT — they set the attachment point. Requires the plugin.";

        /// <summary><c>figjam_create_shape_with_text</c></summary>
        public const string CreateShapeWithText =
            "Creates a shape with a label: a flowchart node, a process step, a diagram element. " +
            "Types: ROUNDED_RECTANGLE (the default), DIAMOND, ELLIPSE, TRIANGLE_UP, TRIANGLE_DOWN, " +
            "PARALLELOGRAM_RIGHT, PARALLELOGRAM_LEFT, ENG_DATABASE, ENG_QUEUE, ENG_FILE, ENG_FOLDER. Requires the plugin.";

        /// <summary><c>figjam_create_section</c></summary>
        public const string CreateSection =
            "Creates a section — a container for grouping board contents. To put elements inside it, " +
            "set their coordinates within the section bounds and call appendChild through figma_execute. Requires the plugin.";

        /// <summary><c>figjam_create_table</c></summary>
        public const string CreateTable =
            "Creates a table on the board: comparison matrices, structured data. " +
            "data is a JSON array of arrays of strings, one per table row, for example [[\"What\",\"Who\"],[\"Design\",\"Ann\"]]. " +
            "Requires the plugin.";

        /// <summary><c>figjam_create_code_block</c></summary>
        public const string CreateCodeBlock =
            "Creates a code block on the board — for code snippets, configuration examples and technical notes. " +
            "language, for example 'JAVASCRIPT', 'PYTHON', 'TYPESCRIPT', 'JSON', 'HTML', 'CSS'. Requires the plugin.";

        /// <summary><c>figjam_auto_arrange</c></summary>
        public const string AutoArrange =
            "Lays board nodes out in a grid, a row or a column — handy after creating elements in bulk. " +
            "nodeIds is comma-separated ids, at most 500. layout: grid, horizontal, vertical. Requires the plugin.";

        /// <summary><c>figjam_get_board_contents</c></summary>
        public const string GetBoardContents =
            "Reads the contents of a board: stickies, shapes, connectors, tables, code blocks and sections " +
            "with their text and coordinates. Call it to learn what is already on the board, or to pull " +
            "structured data out of it. nodeTypes is a comma-separated filter: STICKY, SHAPE_WITH_TEXT, CONNECTOR, " +
            "TABLE, CODE_BLOCK, SECTION, FRAME, TEXT. Requires the plugin.";

        /// <summary><c>figjam_get_connections</c></summary>
        public const string GetConnections =
            "Returns the relation graph of a board: which connectors join which nodes and with which labels. " +
            "This is how a flowchart is read as a structure rather than as a picture. Requires the plugin.";
    }

    /// <summary><c>LibraryTools</c>.</summary>
    public static class Library
    {
        /// <summary><c>figma_get_library_component_by_key</c></summary>
        public const string GetLibraryComponentByKey =
            "The full property definitions, variants and visual specifications of ONE published library component " +
            "by its key — the 40-character hexadecimal string from figma_search_components. " +
            "Take it when you already have the key and need to know which properties and variants the component offers before " +
            "inserting it through figma_instantiate_component. It works for both a COMPONENT_SET and a single COMPONENT: " +
            "for a set you must insert the key of the VARIANT from variants[], not the key of the set itself.";

        /// <summary><c>figma_get_library_style_by_key</c></summary>
        public const string GetLibraryStyleByKey =
            "Metadata of a published library style by its key: name, type, description and where it lives " +
            "(fileKey plus nodeId). The symmetric counterpart of figma_get_library_component_by_key. " +
            "Costs one REST request; draft and unpublished styles are not visible here.";

        /// <summary><c>figma_search_team_components</c></summary>
        public const string SearchTeamComponents =
            "Searches the published components of a whole team library by name or description substring — unlike " +
            "figma_search_components, which only sees the currently open file. Takes the team id or a figma.com link " +
            "with /team/{id}/. Returns keys ready for figma_get_library_component_by_key and figma_instantiate_component; " +
            "paginate with nextCursor. Costs REST requests.";

        /// <summary><c>figma_get_library_variables</c></summary>
        public const string GetLibraryVariables =
            "Variables from the team libraries the current file subscribes to: which tokens — colors, spacing, " +
            "sizes, strings, flags — are available at all, without a link to the library file and without an enterprise plan. " +
            "It works through the plugin: exactly the libraries connected in the current file are visible. " +
            "Next the variable key goes into figma_import_library_variable. The filters compare substrings case-insensitively. " +
            "Requires the plugin.";

        /// <summary><c>figma_import_library_variable</c></summary>
        public const string ImportLibraryVariable =
            "Imports one variable from a subscribed library into the current file. After the import the variable " +
            "is addressed locally by the returned id and fits any tool that binds variables " +
            "to nodes. Calling it again returns the same id. The key comes from figma_get_library_variables — it is not an id. " +
            "Requires the plugin.";
    }

    /// <summary><c>NodeTools</c>.</summary>
    public static class Node
    {
        /// <summary><c>figma_clone_node</c></summary>
        public const string CloneNode = "Copies a node next to the original. The response carries the id of the copy. Requires the plugin.";

        /// <summary><c>figma_delete_node</c></summary>
        public const string DeleteNode = "Deletes a node together with everything inside it. The operation is irreversible. Requires the plugin.";

        /// <summary><c>figma_rename_node</c></summary>
        public const string RenameNode = "Renames a node (a layer in the layers panel). Requires the plugin.";

        /// <summary><c>figma_set_strokes</c></summary>
        public const string SetStrokes =
            "Sets the strokes of a node. The strokes format is an array of Figma Paint in JSON, color components from 0 to 1: " +
            "[{\"type\":\"SOLID\",\"color\":{\"r\":0,\"g\":0,\"b\":0}}]. Requires the plugin.";

        /// <summary><c>figma_set_image_fill</c></summary>
        public const string SetImageFill =
            "Puts an image into a node as a fill. Give it filePath and the server reads the picture from disk itself; " +
            "give it imageData and the bytes come as base64 without the data: prefix, at the cost of carrying them " +
            "through the context. scaleMode: FILL (the default), FIT, CROP, TILE. Requires the plugin.";

        /// <summary><c>figma_create_child</c></summary>
        public const string CreateChild =
            "Creates a node inside a parent. nodeType: FRAME, RECTANGLE, ELLIPSE, TEXT, LINE, VECTOR and so on. " +
            "properties is JSON with the properties of the new node: {\"name\":\"Button\",\"width\":120,\"height\":40," +
            "\"x\":0,\"y\":0,\"characters\":\"Text\"}. Requires the plugin.";

        /// <summary><c>figma_set_description</c></summary>
        public const string SetDescription =
            "Sets the description of a component or a style — what designers see in the assets panel. " +
            "Markdown is supported through descriptionMarkdown. Requires the plugin.";

        /// <summary><c>figma_set_instance_properties</c></summary>
        public const string SetInstanceProperties =
            "Changes the properties of a component instance: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT. " +
            "This is the right way to change the contents of an instance — editing a text node inside it directly " +
            "may silently do nothing. The properties format is JSON: {\"Label\":\"Buy\",\"Show Icon\":true}. " +
            "Requires the plugin.";

        /// <summary><c>figma_boolean_operation</c></summary>
        public const string BooleanOperation =
            "Combines vector shapes: UNION, SUBTRACT, INTERSECT, EXCLUDE make a boolean group, FLATTEN bakes into one " +
            "vector. Node order matters for SUBTRACT — the first node is the base. The icon-work primitive. " +
            "Requires the plugin.";

        /// <summary><c>figma_group_nodes</c></summary>
        public const string GroupNodes =
            "Wraps nodes into a group under the first node's parent. Requires the plugin.";

        /// <summary><c>figma_ungroup_node</c></summary>
        public const string UngroupNode =
            "Dissolves a group or frame, releasing its children into the parent; returns the freed ids. " +
            "Requires the plugin.";

        /// <summary><c>figma_place_image</c></summary>
        public const string PlaceImage =
            "Places a raster image into the file as a rectangle with an image fill, sized to the source. Three ways in: " +
            "filePath, which the server reads from disk and is the way to place a photograph at full resolution; " +
            "base64, which carries the bytes through the context and suits an icon; and url, which Figma downloads " +
            "itself and only from a host the plugin manifest allows, so a link to a local file server will not do. " +
            "PNG, JPG, GIF, WebP up to 4096x4096. Requires the plugin.";

        /// <summary><c>figma_create_node_from_jsx</c></summary>
        public const string CreateNodeFromJsx =
            "Builds a node tree from one declarative JSON spec — the bulk way to create layouts, faster than a chain of " +
            "figma_create_child calls. Components: AutoLayout, Frame, Text, Rectangle, Ellipse, Line, SVG, Image, " +
            "Fragment; children hold nested specs or strings for text. Style ids and instances are not supported here. " +
            "Requires the plugin.";

        /// <summary><c>figma_create_page</c></summary>
        public const string CreatePage =
            "Creates a page in the document; makeCurrent switches the editor to it right away. " +
            "Nodes are then created on it with figma_create_child. Requires the plugin.";

        /// <summary><c>figma_create_node_from_svg</c></summary>
        public const string CreateNodeFromSvg =
            "Imports SVG markup as a frame with vector children — the way to bring an icon from code into Figma. " +
            "Appends to the parent node, or to the current page when parentId is empty; " +
            "x and y are relative to the parent. Requires the plugin.";
    }

    /// <summary><c>QualityTools</c>.</summary>
    public static class Quality
    {
        /// <summary><c>figma_lint_design</c></summary>
        public const string LintDesign =
            "Checks a subtree for the usual defects: values off the tokens, detached styles, " +
            "elements hugging instead of filling, inconsistent spacing. rules is a comma-separated list of rules; " +
            "empty means every rule. Requires the plugin.";

        /// <summary><c>figma_audit_component_accessibility</c></summary>
        public const string AuditComponentAccessibility =
            "Accessibility audit of a component: text contrast, tap target sizes, presence of text alternatives. " +
            "targetSize is the minimum interactive area size in pixels (44 per WCAG). Requires the plugin.";

        /// <summary><c>figma_get_annotations</c></summary>
        public const string GetAnnotations = "Developer annotations on a node — the notes a designer left for the implementation. Requires the plugin.";

        /// <summary><c>figma_set_annotations</c></summary>
        public const string SetAnnotations =
            "Puts annotations on a node. annotations is a JSON array: [{\"label\":\"Spacing\",\"labelMarkdown\":\"…\"}]. " +
            "See the categories in figma_get_annotation_categories. Requires the plugin.";

        /// <summary><c>figma_add_annotation_category</c></summary>
        public const string AddAnnotationCategory =
            "Creates an annotation category. color is one of yellow, orange, red, pink, violet, blue, teal, green. " +
            "The categories that already exist come from figma_get_annotation_categories. Requires the plugin.";

        /// <summary><c>figma_get_annotation_categories</c></summary>
        public const string GetAnnotationCategories = "The annotation categories defined in the file. Requires the plugin.";
    }

    /// <summary><c>ReadTools</c>.</summary>
    public static class Read
    {
        /// <summary><c>figma_get_file_data</c></summary>
        public const string GetFileData =
            "The structure of a Figma file and the document tree. CAUTION: it can eat a lot of tokens — start with depth=1. " +
            "For component descriptions use figma_get_component. It fits best for learning what a file contains " +
            "and finding the nodeId of the nodes you need. The response is already cleaned of vector geometry and defaults, repeated calls " +
            "are served from the cache until the file changes in Figma.";

        /// <summary><c>figma_get_component</c></summary>
        public const string GetComponent =
            "Metadata of a single node or component: name, type, description, component properties, the children it holds. " +
            "This is the right tool for the questions 'what is this component' and 'which properties does it have'. " +
            "For a picture use figma_get_component_image, for the full file tree use figma_get_file_data.";

        /// <summary><c>figma_get_variables</c></summary>
        public const string GetVariables =
            "Figma variables (design tokens): colors, spacing, typography, multi-mode ones (Light/Dark) included. " +
            "It returns a summary by collection and the list of variables. Note: the Variables REST API is available on the " +
            "Enterprise plan only — on other plans the call comes back with a clear 403, and the tokens have to be taken through the plugin. " +
            "For styles (not variables) use figma_get_styles.";

        /// <summary><c>figma_get_file_meta</c></summary>
        public const string GetFileMeta =
            "Name, last-modified time, author of the last edit and editor type of a file — without downloading " +
            "the document. The cheap way to ask 'did anything change?' before spending the expensive file read. " +
            "REST only, one request from the light bucket.";

        /// <summary><c>figma_get_styles</c></summary>
        public const string GetStyles =
            "The styles of a file: color, text, effect and grid styles. These are styles, not variables — " +
            "for variables call figma_get_variables.";

        /// <summary><c>figma_get_component_image</c></summary>
        public const string GetComponentImage =
            "Renders a node into a picture (PNG, JPG, SVG, PDF). Needed on requests like 'show me', 'a screenshot', " +
            "'what does it look like'. For component data use figma_get_component. " +
            "The source is chosen automatically: the Figma Desktop plugin → the cache → Figma REST. The plugin and the cache " +
            "spend no Figma quota at all, and the plugin additionally shows the state right now, " +
            "unsaved edits included. The response carries a source field: cache/figma-api give a link " +
            "in data.url (it lives 30 days), plugin gives a path to a file on disk in data.image.path " +
            "(open the file to see the picture). With deliver=both a picture taken through the plugin also comes back " +
            "as image content, for a caller that cannot open a path on this machine.";

        /// <summary><c>figma_get_file_for_plugin</c></summary>
        public const string GetFileForPlugin =
            "The structure of a file without visual properties: ids, names, types, component links " +
            "and plugin data only. Needed when developing plugins and when only the hierarchy matters. Noticeably lighter than figma_get_file_data.";
    }

    /// <summary><c>SessionTools</c>.</summary>
    public static class Session
    {
        /// <summary><c>figma_list_open_files</c></summary>
        public const string ListOpenFiles =
            "Shows every Figma file the plugin is currently running in, which of them is the target of the commands, " +
            "and which other agents work in each. Useful when several files are open and commands go to the wrong one.";

        /// <summary><c>figma_navigate</c></summary>
        public const string Navigate =
            "Selects the file the following commands will go to, by a link to a Figma file or its key. " +
            "The target then stays put: a human switching files in Figma does not move it, only another figma_navigate does. " +
            "With several files connected and no target chosen, the write tools refuse and list the files — this call is the answer. " +
            "The response names the other agents working in the same file, so the agent can keep to its own page or section.";

        /// <summary><c>figma_execute_across_files</c></summary>
        public const string ExecuteAcrossFiles =
            "Runs the same JavaScript IN SEVERAL Figma files AT ONCE — in parallel, without moving " +
            "the target. For auditing and editing across a multi-file design system: " +
            "one check runs over every file and the results are compared with each other — instead of " +
            "switching the active file and calling figma_execute one by one. In every file the code runs " +
            "in that file's own plugin sandbox, with the same figma object as figma_execute. " +
            "The targets must be named explicitly: fileKeys (the list from figma_list_open_files) or allFiles=true. " +
            "Requires a running plugin in every target file.";

        /// <summary><c>figma_reconnect</c></summary>
        public const string Reconnect =
            "Re-reads the list of connected files and reports the target. " +
            "Call it when the server keeps aiming at a file that is already closed.";

        /// <summary><c>figma_reload_plugin</c></summary>
        public const string ReloadPlugin = "Reloads the plugin UI in Figma. It helps when the plugin stopped answering. Requires the plugin.";

        /// <summary><c>figma_get_console_logs</c></summary>
        public const string GetConsoleLogs =
            "The latest console lines from the plugin sandbox. Needed to understand why figma_execute failed " +
            "or what exactly the plugin did.";

        /// <summary><c>figma_get_remarks</c></summary>
        public const string GetRemarks =
            "Reads what the human typed in the plugin panel: their words plus the layers they had selected. " +
            "THIS IS THE CHANNEL FOR the case where the build is finished, the human looked at it and found a " +
            "defect — call it after finishing a build and before starting the next task, because a remark left " +
            "while nothing was running reaches nobody otherwise. Reading TAKES the remarks: the same one never " +
            "comes back, so act on it in the same turn. A remark typed WHILE figma_execute_steps was running is " +
            "not here — that one stops the build and comes back in its own answer. waitMs holds the call open until " +
            "somebody writes, up to 60000 ms: nothing in MCP lets a server wake an agent, so a wait here is the " +
            "only way to hear the human within a second rather than at the next turn. Requires the plugin.";

        /// <summary><c>figma_clear_console</c></summary>
        public const string ClearConsole = "Starts this agent's console from now: figma_get_console_logs and figma_watch_console then show only newer lines. Other agents working in the same file keep theirs. Handy before a new figma_execute run.";

        /// <summary><c>figma_watch_console</c></summary>
        public const string WatchConsole =
            "Waits for new lines to appear in the plugin console up to the given timeout. " +
            "Needed when an edit runs asynchronously and the result arrives with a delay.";

        /// <summary><c>figma_notify</c></summary>
        public const string Notify =
            "Shows a toast inside the Figma window — the way to tell the human at the canvas that a batch of work " +
            "is done or needs their eyes. Requires the plugin.";

        /// <summary><c>figma_scroll_to_node</c></summary>
        public const string ScrollToNode =
            "Scrolls the user's viewport to a node, switching pages when needed; select=true also selects it. " +
            "Call it after creating something so the human sees the result without hunting for it. Requires the plugin.";

        /// <summary><c>figma_set_file_thumbnail</c></summary>
        public const string SetFileThumbnail =
            "Makes a frame the file cover shown in the project browser; an empty nodeId resets to the default. " +
            "Requires the plugin.";

        /// <summary><c>figma_export_node</c></summary>
        public const string ExportNode =
            "Exports a node through the live editor: SVG returns the markup itself, PDF/PNG/JPG are saved to disk " +
            "and the path is returned. With deliver=both a PNG or JPG also comes back as image content, which is what " +
            "a caller outside this machine needs, since the path opens nothing there. No REST quota is spent — this " +
            "replaces the paid /v1/images render for files open in Figma Desktop. Requires the plugin.";

        /// <summary><c>figma_get_active_users</c></summary>
        public const string GetActiveUsers =
            "Who is in the file right now: the token owner (me) and the other live collaborators with their multiplayer " +
            "colors. Useful before heavy mutations — a human working in the same file deserves figma_commit_undo " +
            "batches and gentler pacing. Requires the plugin.";

        /// <summary><c>figma_take_screenshot</c></summary>
        public const string TakeScreenshot =
            "Captures a node or the current page straight from the plugin. Unlike figma_get_component_image, " +
            "it shows the state RIGHT NOW, including edits made a moment ago — so this is the tool " +
            "that verifies the result of a mutation. By default the picture is saved to disk and the response carries " +
            "the file path (image.path) and the metadata: open the file to see it. Pass deliver=both and the picture " +
            "comes back as image content as well — the way to look at it from another process, at the cost of carrying " +
            "the bytes in the answer. Requires the plugin.";

        /// <summary><c>figma_get_text_styles</c></summary>
        public const string GetTextStyles = "The text styles of a file read through the plugin: name, size, weight, line height. Requires the plugin.";
    }

    /// <summary><c>SlidesTools</c>.</summary>
    public static class Slides
    {
        /// <summary><c>figma_list_slides</c></summary>
        public const string ListSlides =
            "The list of presentation slides: ids, names, grid positions and the skip flag. " +
            "Works in Figma Slides files only. Requires the plugin.";

        /// <summary><c>figma_get_slide_content</c></summary>
        public const string GetSlideContent = "The content tree of a slide: texts, shapes and frames with their properties. Requires the plugin.";

        /// <summary><c>figma_get_slide_grid</c></summary>
        public const string GetSlideGrid = "The two-dimensional slide grid: how the slides are laid out in rows and columns. Requires the plugin.";

        /// <summary><c>figma_get_slide_transition</c></summary>
        public const string GetSlideTransition = "The current transition settings of a slide: style, duration, easing curve. Requires the plugin.";

        /// <summary><c>figma_get_focused_slide</c></summary>
        public const string GetFocusedSlide = "The slide currently open in single-slide mode. Requires the plugin.";

        /// <summary><c>figma_create_slide</c></summary>
        public const string CreateSlide = "Creates an empty slide. Its grid position can be set by row and column. Requires the plugin.";

        /// <summary><c>figma_delete_slide</c></summary>
        public const string DeleteSlide =
            "Deletes a slide from the presentation. The action is destructive — it is undone only through undo in Figma. " +
            "Requires the plugin.";

        /// <summary><c>figma_duplicate_slide</c></summary>
        public const string DuplicateSlide = "Duplicates a slide. The copy lands next to the original. Requires the plugin.";

        /// <summary><c>figma_reorder_slides</c></summary>
        public const string ReorderSlides =
            "Rearranges the slides into a new grid. grid is a JSON array of arrays of ids, " +
            "each nested array being one grid row: [[\"1:2\",\"1:3\"],[\"1:4\"]]. " +
            "The action is destructive: the order is replaced entirely. Requires the plugin.";

        /// <summary><c>figma_set_slide_transition</c></summary>
        public const string SetSlideTransition =
            "Sets the transition of a slide. style: NONE, DISSOLVE, SLIDE_FROM_LEFT/RIGHT/TOP/BOTTOM, " +
            "PUSH_FROM_LEFT/RIGHT/TOP/BOTTOM, MOVE_FROM_LEFT/RIGHT/TOP/BOTTOM, " +
            "SLIDE_OUT_TO_LEFT/RIGHT/TOP/BOTTOM, MOVE_OUT_TO_LEFT/RIGHT/TOP/BOTTOM, SMART_ANIMATE. " +
            "curve: LINEAR, EASE_IN, EASE_OUT, EASE_IN_AND_OUT, GENTLE, QUICK, BOUNCY, SLOW. Requires the plugin.";

        /// <summary><c>figma_skip_slide</c></summary>
        public const string SkipSlide = "Turns skipping a slide in presentation mode on and off. Requires the plugin.";

        /// <summary><c>figma_add_text_to_slide</c></summary>
        public const string AddTextToSlide =
            "Adds text to a slide: font, color, alignment and the rest of the formatting. " +
            "width enables line wrapping. Requires the plugin.";

        /// <summary><c>figma_add_shape_to_slide</c></summary>
        public const string AddShapeToSlide = "Adds a rectangle or an ellipse to a slide. shapeType: RECTANGLE or ELLIPSE. Requires the plugin.";

        /// <summary><c>figma_set_slide_background</c></summary>
        public const string SetSlideBackground =
            "Sets the slide background: it creates or updates a rectangle covering the whole slide. " +
            "color is hex of the form '#181818'. Requires the plugin.";

        /// <summary><c>figma_set_slides_view_mode</c></summary>
        public const string SetSlidesViewMode = "Switches the presentation view: mode is 'grid' or 'single-slide'. Requires the plugin.";

        /// <summary><c>figma_focus_slide</c></summary>
        public const string FocusSlide = "Moves to a slide in single-slide mode. Requires the plugin.";
    }

    /// <summary><c>SlotTools</c>.</summary>
    public static class Slot
    {
        /// <summary><c>figma_create_slot</c></summary>
        public const string CreateSlot =
            "Creates a SlotNode inside a component and at the same time defines a linked property of type SLOT with the same name " +
            "(renaming the slot renames the property). It works on a standalone COMPONENT and on the variants inside " +
            "a COMPONENT_SET — call it once per variant then. GRID layout on slots is forbidden. Requires the plugin.";

        /// <summary><c>figma_get_slots</c></summary>
        public const string GetSlots =
            "The list of slots of a component, a component set or an instance: ids, names, property keys, " +
            "sizes and the current contents. Call it on an instance before figma_append_to_slot to learn the slot names. " +
            "Requires the plugin.";

        /// <summary><c>figma_append_to_slot</c></summary>
        public const string AppendToSlot =
            "Puts content into an instance slot: it either clones sourceNodeId there or creates a new node by nodeType " +
            "(FRAME, RECTANGLE, ELLIPSE, TEXT, LINE, POLYGON, STAR, VECTOR). A slot is addressed directly by slotId " +
            "or by the pair instanceId + slotName. Widgets, stickies and bare ComponentNodes do not go into a slot. Requires the plugin.";

        /// <summary><c>figma_reset_slot</c></summary>
        public const string ResetSlot =
            "Clears a slot, returning it to its default state. It is addressed the same way as figma_append_to_slot: " +
            "slotId or instanceId + slotName. Requires the plugin.";

        /// <summary><c>figma_add_slot_property</c></summary>
        public const string AddSlotProperty =
            "The manual alternative to figma_create_slot: it defines a property of type SLOT and binds it to an already existing " +
            "frame. The frame must be a direct child of the component (of its variant, for a set), must not use " +
            "GRID layout and must not sit inside another slot. preferredValues is a JSON array of the form " +
            "[{\"type\":\"COMPONENT\",\"key\":\"…\"}]. For new slots figma_create_slot is better. Requires the plugin.";
    }

    /// <summary><c>TokenTools</c>.</summary>
    public static class Token
    {
        /// <summary><c>figma_export_tokens</c></summary>
        public const string ExportTokens =
            "Exports Figma variables into code. Formats: dtcg, tokens-studio, css-vars, tailwind-v4, tailwind-v3, " +
            "scss, less, ts-module, json-flat, json-nested, style-dictionary-v3. " +
            "The data source is chosen automatically: when the plugin is running the variables come from it (works on any plan), " +
            "otherwise through the Variables REST API (Enterprise only). " +
            "It returns the file contents — writing them to disk is up to you.";

        /// <summary><c>figma_import_tokens</c></summary>
        public const string ImportTokens =
            "Reads tokens from code and pushes them into Figma variables. content is the file contents, " +
            "format is how to parse it (dtcg, tokens-studio, css-vars, scss, tailwind-v4, tailwind-v3, " +
            "json-flat, json-nested, style-dictionary-v3). " +
            "dryRun=true (the default) only shows the plan of changes without changing anything. " +
            "Writing requires the plugin.";

        /// <summary><c>figma_convert_tokens</c></summary>
        public const string ConvertTokens =
            "Converts tokens from one format into another without touching Figma. " +
            "Useful for handing the same system out in CSS, Tailwind and TypeScript.";
    }

    /// <summary><c>VariableTools</c>.</summary>
    public static class Variable
    {
        /// <summary><c>figma_create_variable_collection</c></summary>
        public const string CreateVariableCollection =
            "Creates a variable collection — a container of tokens with modes (themes). " +
            "The collection is created first, then variables are put into it with figma_create_variable. Requires the plugin.";

        /// <summary><c>figma_delete_variable_collection</c></summary>
        public const string DeleteVariableCollection = "Deletes a variable collection together with all its variables. The operation is irreversible. Requires the plugin.";

        /// <summary><c>figma_create_variable</c></summary>
        public const string CreateVariable =
            "Creates a variable in a collection. resolvedType: COLOR, FLOAT, STRING or BOOLEAN. " +
            "A color in valuesByMode is set by the string '#RRGGBB'. Name hierarchy is made with a slash: 'color/brand/primary'. " +
            "Requires the plugin.";

        /// <summary><c>figma_update_variable</c></summary>
        public const string UpdateVariable =
            "Changes the value of a variable in a specific mode. A color is set by the string '#RRGGBB', " +
            "a number by a number, a reference to another variable by its id. Requires the plugin.";

        /// <summary><c>figma_delete_variable</c></summary>
        public const string DeleteVariable = "Deletes a variable. When it is used somewhere Figma refuses — remove the bindings first. Requires the plugin.";

        /// <summary><c>figma_rename_variable</c></summary>
        public const string RenameVariable = "Renames a variable. Slashes in the name define the group hierarchy. Requires the plugin.";

        /// <summary><c>figma_set_variable_description</c></summary>
        public const string SetVariableDescription = "Sets the description of a variable — designers see it in the variables panel. Requires the plugin.";

        /// <summary><c>figma_add_mode</c></summary>
        public const string AddMode =
            "Adds a mode to a collection — this is how themes (Dark) and breakpoints (Mobile) are made. " +
            "The number of modes is limited by the Figma plan. Requires the plugin.";

        /// <summary><c>figma_rename_mode</c></summary>
        public const string RenameMode = "Renames a mode of a collection. Requires the plugin.";

        /// <summary><c>figma_get_token_values</c></summary>
        public const string GetTokenValues =
            "Reads variables straight from the plugin, bypassing REST. It works on any Figma plan, " +
            "unlike figma_get_variables. Requires the plugin.";

        /// <summary><c>figma_batch_create_variables</c></summary>
        public const string BatchCreateVariables =
            "Creates many variables in one collection at once: noticeably faster than one by one. " +
            "The variables format is a JSON array: [{\"name\":\"color/bg\",\"resolvedType\":\"COLOR\"," +
            "\"valuesByMode\":{\"1:0\":\"#FFFFFF\"}}]. Requires the plugin.";

        /// <summary><c>figma_batch_update_variables</c></summary>
        public const string BatchUpdateVariables =
            "Updates the values of many variables in one call. The updates format is a JSON array: " +
            "[{\"variableId\":\"VariableID:1:2\",\"modeId\":\"1:0\",\"value\":\"#FF0000\"}]. Requires the plugin.";

        /// <summary><c>figma_setup_design_tokens</c></summary>
        public const string SetupDesignTokens =
            "Rolls out the foundation of a design system in one call: it creates a collection with the modes you need " +
            "and fills it with variables. The tokens format is a JSON array, the same as in figma_batch_create_variables. " +
            "Requires the plugin.";
    }

    /// <summary><c>VersionTools</c>.</summary>
    public static class Version
    {
        /// <summary><c>figma_save_version</c></summary>
        public const string SaveVersion =
            "Saves a named checkpoint into the file's version history — the anchor for figma_diff_versions and " +
            "figma_generate_changelog. Call it after a finished batch of agent work, so the history reads like a " +
            "changelog instead of autosaves. Requires the plugin.";

        /// <summary><c>figma_get_file_versions</c></summary>
        public const string GetFileVersions =
            "The version history of a file with metadata: label, description, author, time. It walks the pages itself " +
            "up to maxVersions. By default it returns named versions only — autosaves are skipped; " +
            "includeAutosaves=true shows every saved state. To continue, pass the " +
            "next_cursor from the previous response. The token needs the file_versions:read scope (OAuth) or " +
            "the Versions Read permission on a personal token.";

        /// <summary><c>figma_get_file_at_version</c></summary>
        public const string GetFileAtVersion =
            "The file or individual nodes as they were at a past version. The response has the same shape " +
            "as figma_get_file_data, only the snapshot is bound to a version. Version ids come from " +
            "figma_get_file_versions. nodeIds and depth shrink the response noticeably.";

        /// <summary><c>figma_diff_versions</c></summary>
        public const string DiffVersions =
            "Compares two versions of a file. It always returns the cheap page structure comparison " +
            "(added, removed, renamed) — that is two API requests. componentIds adds a breakdown of " +
            "specific nodes at depth 2: children that appeared and disappeared, name and description changes, " +
            "changes of componentPropertyDefinitions on a COMPONENT_SET and of variable bindings — two requests " +
            "per node. toVersion='current' compares against the current state. When componentIds is not passed, " +
            "the current selection in Figma is used. REST does not show description and annotation edits in version snapshots — " +
            "they are collected from the plugin event buffer while it is connected.";

        /// <summary><c>figma_get_changes_since_version</c></summary>
        public const string GetChangesSinceVersion =
            "The same as figma_diff_versions, only the second version is always the current one. Handy for the question " +
            "'what changed since the last sync with the code'.";

        /// <summary><c>figma_generate_changelog</c></summary>
        public const string GenerateChangelog =
            "A changelog between two versions in Markdown — ready text for release notes, " +
            "a pull request or an MDX page. On top of the version comparison it adds authors and dates, for which " +
            "it makes one cheap request to the version list. It returns both the markdown and the structured data. " +
            "toVersion='current' compares against the current state.";

        /// <summary><c>figma_blame_node</c></summary>
        public const string BlameNode =
            "Finds the version an edit appeared in — the answer to 'who added this and when'. " +
            "It walks the history by binary search: about log2(N) requests instead of N. It returns the version metadata: " +
            "label, author, time. Autosaves are included by default — the attribution is more precise that way; " +
            "the Figma system user shows up on scheduled snapshots and is marked " +
            "attribution_certainty='system_attributed'. Pass EXACTLY ONE of targetComponentProperty " +
            "or targetChildNodeId.";
    }

    /// <summary><c>WriteTools</c>.</summary>
    public static class Write
    {
        /// <summary><c>figma_get_selection</c></summary>
        public const string GetSelection =
            "What is selected in Figma right now in the target file: the ids, names, types and sizes of the nodes. " +
            "This is the right first step for 'move this' and 'change the selected one' — the nodeId comes from the response. " +
            "While no target is chosen it reports the file the human touched last, with its fileKey. " +
            "Requires a running Snail Bridge plugin.";

        /// <summary><c>figma_move_node</c></summary>
        public const string MoveNode =
            "Moves a node. The x and y coordinates are relative to the parent, exactly as the Figma Plugin API understands them. " +
            "To move it '10 pixels to the right', first read the current position (figma_get_component " +
            "or figma_get_selection) and add the offset to it. Requires the plugin.";

        /// <summary><c>figma_resize_node</c></summary>
        public const string ResizeNode =
            "Resizes a node. With withConstraints=true Figma recalculates the nested elements by their constraints — " +
            "usually that is what you want for frames. Requires the plugin.";

        /// <summary><c>figma_set_fills</c></summary>
        public const string SetFills =
            "Sets the fills of a node. The fills format is an array of Figma Paint in JSON, for example " +
            "[{\"type\":\"SOLID\",\"color\":{\"r\":1,\"g\":0,\"b\":0},\"opacity\":1}]. " +
            "Channel components are fractions from 0 to 1, not 0-255. Requires the plugin.";

        /// <summary><c>figma_set_text</c></summary>
        public const string SetText =
            "Changes the text in a TEXT node. Careful: when the node belongs to a component instance and the text is driven " +
            "by a component property, the edit may not apply — use figma_set_instance_properties then. " +
            "Requires the plugin.";

        /// <summary><c>figma_execute</c></summary>
        public const string Execute =
            "Runs arbitrary JavaScript in the Figma plugin sandbox: the whole figma object (Plugin API) is available. " +
            "The universal tool for what the specialised commands do not cover. " +
            "The code runs as the body of an async function; return a value with return. " +
            "The asynchronous Figma methods (loadFontAsync, getNodeByIdAsync, loadAllPagesAsync) must be awaited. " +
            "The code runs in the target file: the one figma_navigate chose, or the only connected one. " +
            "With several files connected and none chosen the call refuses and lists them. " +
            "Requires the plugin.";

        /// <summary><c>figma_execute_steps</c>; the step cap must stay equal to
        /// <see cref="Snail.MCP.Figma.Tools.ToolLimits.MaxSteps"/>, which a catalog test checks.</summary>
        public const string ExecuteSteps =
            "Runs short scripts one after another, so a layout grows element by element instead of appearing all at " +
            "once, and a failure names the step that caused it instead of the whole batch. Each step is a figma_execute " +
            "body, and each runs on its own: declarations do not survive between steps, so carry what the next step " +
            "needs on globalThis. Up to 60 steps in one call; globals live on afterwards, so a longer build continues " +
            "in the next call. Prefer this over one large figma_execute: the sandbox parser chokes on a big nested " +
            "expression, and there the error says nothing about where. The human can step in from the plugin panel " +
            "while it runs: the answer then carries stopped=true, their remark and what they had selected, and the " +
            "steps that did not run are simply sent again once the remark is dealt with. A remark left BEFORE this " +
            "call does not stop it: that one waits for figma_get_remarks. Build into ONE frame created " +
            "by the first step: undo cannot merge the steps back together, so a single container is what makes the " +
            "whole build undoable by deleting it. Requires the plugin.";

        /// <summary><c>figma_get_selection_colors</c></summary>
        public const string GetSelectionColors =
            "Every color used in the current selection, with the styles they come from — the quick token audit: " +
            "select a screen, see which paints are loose values instead of styles or variables. " +
            "Returns null-ish when nothing is selected. Requires the plugin.";

        /// <summary><c>figma_sort_styles</c></summary>
        public const string SortStyles =
            "Reorders the local styles panel: alphabetically by default, or by an explicit comma-separated id list. " +
            "styleType is PAINT, TEXT, EFFECT or GRID; ids come from figma_get_styles. Folder prefixes (Brand/…) sort " +
            "with their names. Requires the plugin.";

        /// <summary><c>figma_commit_undo</c></summary>
        public const string CommitUndo =
            "Closes the current undo group: everything changed since the previous commit becomes a single Ctrl+Z step " +
            "for the human working in the same file. Call it after a finished batch of mutations, " +
            "not after every small edit. Requires the plugin.";

        /// <summary><c>figma_create_style</c></summary>
        public const string CreateStyle =
            "Creates a local style. styleType is PAINT, TEXT, EFFECT or GRID; properties is JSON with the style contents: " +
            "paints for PAINT, fontName/fontSize/lineHeight and the other text fields for TEXT, effects for EFFECT, " +
            "layoutGrids for GRID. A slash in the name makes a folder: Brand/Primary. " +
            "Existing styles are read by figma_get_styles. Requires the plugin.";

        /// <summary><c>figma_get_status</c></summary>
        public const string GetStatus =
            "The state of the bridge to Figma: whether the plugin is connected, which files are visible, which of them " +
            "is the target of the commands and which other agents work there. " +
            "Call it first when the write tools answer 'the plugin is not connected'.";
    }
}
