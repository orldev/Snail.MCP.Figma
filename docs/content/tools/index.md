# Tools

141 tools in 18 families.
The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.

Reads go through the Figma Desktop plugin while it runs in the file — unsaved edits visible, no API quota spent — and fall back to the REST API behind a snapshot cache; every write needs the plugin. The line under each tool says whether it may reach the REST API or works on the connected file only.

| Tool | Family | What it does |
| --- | --- | --- |
| [`figma_get_file_data`](reading.md#figma_get_file_data) | Reading | The structure of a Figma file and the document tree. |
| [`figma_get_component`](reading.md#figma_get_component) | Reading | Metadata of a single node or component: name, type, description, component properties, the children it holds. |
| [`figma_get_variables`](reading.md#figma_get_variables) | Reading | Figma variables (design tokens): colors, spacing, typography, multi-mode ones (Light/Dark) included. |
| [`figma_get_file_meta`](reading.md#figma_get_file_meta) | Reading | Name, last-modified time, author of the last edit and editor type of a file — without downloading the document. |
| [`figma_get_styles`](reading.md#figma_get_styles) | Reading | The styles of a file: color, text, effect and grid styles. |
| [`figma_get_component_image`](reading.md#figma_get_component_image) | Reading | Renders a node into a picture (PNG, JPG, SVG, PDF). |
| [`figma_get_file_for_plugin`](reading.md#figma_get_file_for_plugin) | Reading | The structure of a file without visual properties: ids, names, types, component links and plugin data only. |
| [`figma_get_selection`](writing.md#figma_get_selection) | Writing | What is selected in Figma right now in the target file: the ids, names, types and sizes of the nodes. |
| [`figma_move_node`](writing.md#figma_move_node) | Writing | Moves a node. |
| [`figma_resize_node`](writing.md#figma_resize_node) | Writing | Resizes a node. |
| [`figma_set_fills`](writing.md#figma_set_fills) | Writing | Sets the fills of a node. |
| [`figma_set_text`](writing.md#figma_set_text) | Writing | Changes the text in a TEXT node. |
| [`figma_execute`](writing.md#figma_execute) | Writing | Runs arbitrary JavaScript in the Figma plugin sandbox: the whole figma object (Plugin API) is available. |
| [`figma_execute_steps`](writing.md#figma_execute_steps) | Writing | Runs short scripts one after another, so a layout grows element by element instead of appearing all at once, and a failure names the step that caused it inst… |
| [`figma_get_selection_colors`](writing.md#figma_get_selection_colors) | Writing | Every color used in the current selection, with the styles they come from — the quick token audit: select a screen, see which paints are loose values instead… |
| [`figma_sort_styles`](writing.md#figma_sort_styles) | Writing | Reorders the local styles panel: alphabetically by default, or by an explicit comma-separated id list. |
| [`figma_commit_undo`](writing.md#figma_commit_undo) | Writing | Closes the current undo group: everything changed since the previous commit becomes a single Ctrl+Z step for the human working in the same file. |
| [`figma_create_style`](writing.md#figma_create_style) | Writing | Creates a local style. |
| [`figma_get_status`](writing.md#figma_get_status) | Writing | The state of the bridge to Figma: whether the plugin is connected, which files are visible, which of them is the target of the commands and which other agent… |
| [`figma_clone_node`](nodes.md#figma_clone_node) | Nodes | Copies a node next to the original. |
| [`figma_delete_node`](nodes.md#figma_delete_node) | Nodes | Deletes a node together with everything inside it. |
| [`figma_rename_node`](nodes.md#figma_rename_node) | Nodes | Renames a node (a layer in the layers panel). |
| [`figma_set_strokes`](nodes.md#figma_set_strokes) | Nodes | Sets the strokes of a node. |
| [`figma_set_image_fill`](nodes.md#figma_set_image_fill) | Nodes | Puts an image into a node as a fill. |
| [`figma_create_child`](nodes.md#figma_create_child) | Nodes | Creates a node inside a parent. |
| [`figma_set_description`](nodes.md#figma_set_description) | Nodes | Sets the description of a component or a style — what designers see in the assets panel. |
| [`figma_set_instance_properties`](nodes.md#figma_set_instance_properties) | Nodes | Changes the properties of a component instance: TEXT, BOOLEAN, INSTANCE_SWAP, VARIANT. |
| [`figma_create_page`](nodes.md#figma_create_page) | Nodes | Creates a page in the document; makeCurrent switches the editor to it right away. |
| [`figma_create_node_from_svg`](nodes.md#figma_create_node_from_svg) | Nodes | Imports SVG markup as a frame with vector children — the way to bring an icon from code into Figma. |
| [`figma_place_image`](nodes.md#figma_place_image) | Nodes | Places a raster image into the file as a rectangle with an image fill, sized to the source. |
| [`figma_create_node_from_jsx`](nodes.md#figma_create_node_from_jsx) | Nodes | Builds a node tree from one declarative JSON spec — the bulk way to create layouts, faster than a chain of figma_create_child calls. |
| [`figma_boolean_operation`](nodes.md#figma_boolean_operation) | Nodes | Combines vector shapes: UNION, SUBTRACT, INTERSECT, EXCLUDE make a boolean group, FLATTEN bakes into one vector. |
| [`figma_group_nodes`](nodes.md#figma_group_nodes) | Nodes | Wraps nodes into a group under the first node's parent. |
| [`figma_ungroup_node`](nodes.md#figma_ungroup_node) | Nodes | Dissolves a group or frame, releasing its children into the parent; returns the freed ids. |
| [`figma_search_components`](components.md#figma_search_components) | Components | Searches components in the open file by a substring of the name. |
| [`figma_instantiate_component`](components.md#figma_instantiate_component) | Components | Places a component instance on the canvas. |
| [`figma_create_component_set`](components.md#figma_create_component_set) | Components | Builds a component set out of existing components or from a property matrix. |
| [`figma_analyze_component_set`](components.md#figma_analyze_component_set) | Components | Breaks a variant set down: which property axes exist, which combinations are there and which are missing. |
| [`figma_arrange_component_set`](components.md#figma_arrange_component_set) | Components | Lays the variants inside a set out in a grid: rows are the first property axis, columns the second. |
| [`figma_add_component_property`](components.md#figma_add_component_property) | Components | Adds a property to a component. |
| [`figma_edit_component_property`](components.md#figma_edit_component_property) | Components | Changes the default value of a component property. |
| [`figma_delete_component_property`](components.md#figma_delete_component_property) | Components | Deletes a component property. |
| [`figma_get_component_details`](components.md#figma_get_component_details) | Components | Reads a component straight from the plugin instead of REST: the current state of the file is visible, unsaved edits included. |
| [`figma_create_variable_collection`](variables.md#figma_create_variable_collection) | Variables | Creates a variable collection — a container of tokens with modes (themes). |
| [`figma_delete_variable_collection`](variables.md#figma_delete_variable_collection) | Variables | Deletes a variable collection together with all its variables. |
| [`figma_create_variable`](variables.md#figma_create_variable) | Variables | Creates a variable in a collection. |
| [`figma_update_variable`](variables.md#figma_update_variable) | Variables | Changes the value of a variable in a specific mode. |
| [`figma_delete_variable`](variables.md#figma_delete_variable) | Variables | Deletes a variable. |
| [`figma_rename_variable`](variables.md#figma_rename_variable) | Variables | Renames a variable. |
| [`figma_set_variable_description`](variables.md#figma_set_variable_description) | Variables | Sets the description of a variable — designers see it in the variables panel. |
| [`figma_add_mode`](variables.md#figma_add_mode) | Variables | Adds a mode to a collection — this is how themes (Dark) and breakpoints (Mobile) are made. |
| [`figma_rename_mode`](variables.md#figma_rename_mode) | Variables | Renames a mode of a collection. |
| [`figma_get_token_values`](variables.md#figma_get_token_values) | Variables | Reads variables straight from the plugin, bypassing REST. |
| [`figma_batch_create_variables`](variables.md#figma_batch_create_variables) | Variables | Creates many variables in one collection at once: noticeably faster than one by one. |
| [`figma_batch_update_variables`](variables.md#figma_batch_update_variables) | Variables | Updates the values of many variables in one call. |
| [`figma_setup_design_tokens`](variables.md#figma_setup_design_tokens) | Variables | Rolls out the foundation of a design system in one call: it creates a collection with the modes you need and fills it with variables. |
| [`figma_export_tokens`](design-tokens.md#figma_export_tokens) | Design tokens | Exports Figma variables into code. |
| [`figma_import_tokens`](design-tokens.md#figma_import_tokens) | Design tokens | Reads tokens from code and pushes them into Figma variables. |
| [`figma_convert_tokens`](design-tokens.md#figma_convert_tokens) | Design tokens | Converts tokens from one format into another without touching Figma. |
| [`figma_get_component_for_development`](design-to-code.md#figma_get_component_for_development) | Design to code | Component data for implementing it in code: the tree with variable bindings, sizes, auto layout and text properties, plus a ready reconstruction specification. |
| [`figma_get_component_for_development_deep`](design-to-code.md#figma_get_component_for_development_deep) | Design to code | The same as figma_get_component_for_development, but with a deep breakdown through the plugin: states, reactions, slots, Dev Mode annotations. |
| [`figma_extract_style_values`](design-to-code.md#figma_extract_style_values) | Design to code | Pulls token-like values out of the DESIGN — colors, typography, spacing, radii — and collapses duplicates. |
| [`figma_check_design_parity`](design-to-code.md#figma_check_design_parity) | Design to code | Compares a component in Figma with what is implemented in code. |
| [`figma_generate_component_doc`](design-to-code.md#figma_generate_component_doc) | Design to code | Assembles component documentation in Markdown: contents, variants, colors, typography, spacing, the Figma description and the annotations. |
| [`figma_get_design_system_kit`](design-system-kit.md#figma_get_design_system_kit) | Design system kit | The MAIN tool for capturing a design system: it replaces separate calls to figma_get_styles, figma_get_variables and figma_get_component. |
| [`figma_ds_analyze`](design-system-from-code.md#figma_ds_analyze) | Design system from code | The first step of design system extraction: it breaks down the codebase of a production application. |
| [`figma_ds_extract_tokens`](design-system-from-code.md#figma_ds_extract_tokens) | Design system from code | Mines design tokens FROM THE CODEBASE into canonical DTCG (plus, optionally, CSS variables, Tailwind, SCSS, TypeScript). |
| [`figma_ds_scaffold`](design-system-from-code.md#figma_ds_scaffold) | Design system from code | Assembles the scaffold of a design system package in outDir from a finished analysis and tokens: package.json (the application framework goes into peer depen… |
| [`figma_ds_setup_storybook`](design-system-from-code.md#figma_ds_setup_storybook) | Design system from code | Wires a freshly installed Storybook workshop to the extracted design system. |
| [`figma_ds_extract_component`](design-system-from-code.md#figma_ds_extract_component) | Design system from code | Breaks ONE component of the inventory down deeply for porting: the source (with a size cap), the local import closure (relative imports worth following), the… |
| [`figma_ds_verify`](design-system-from-code.md#figma_ds_verify) | Design system from code | Deterministic checks of the extracted design system package — the gate before handover or the trip into Figma. |
| [`figma_ds_status`](design-system-from-code.md#figma_ds_status) | Design system from code | Reads and updates the progress of design system porting. |
| [`figma_get_file_versions`](version-history.md#figma_get_file_versions) | Version history | The version history of a file with metadata: label, description, author, time. |
| [`figma_get_file_at_version`](version-history.md#figma_get_file_at_version) | Version history | The file or individual nodes as they were at a past version. |
| [`figma_diff_versions`](version-history.md#figma_diff_versions) | Version history | Compares two versions of a file. |
| [`figma_get_changes_since_version`](version-history.md#figma_get_changes_since_version) | Version history | The same as figma_diff_versions, only the second version is always the current one. |
| [`figma_generate_changelog`](version-history.md#figma_generate_changelog) | Version history | A changelog between two versions in Markdown — ready text for release notes, a pull request or an MDX page. |
| [`figma_blame_node`](version-history.md#figma_blame_node) | Version history | Finds the version an edit appeared in — the answer to 'who added this and when'. |
| [`figma_save_version`](version-history.md#figma_save_version) | Version history | Saves a named checkpoint into the file's version history — the anchor for figma_diff_versions and figma_generate_changelog. |
| [`figma_get_comments`](comments.md#figma_get_comments) | Comments | File comments: discussion threads with the author, the text, the time and the node the comment is pinned to. |
| [`figma_post_comment`](comments.md#figma_post_comment) | Comments | Leaves a comment in the file, optionally pinned to a node. |
| [`figma_delete_comment`](comments.md#figma_delete_comment) | Comments | Deletes a comment by id. |
| [`figma_get_library_component_by_key`](libraries.md#figma_get_library_component_by_key) | Libraries | The full property definitions, variants and visual specifications of ONE published library component by its key — the 40-character hexadecimal string from fi… |
| [`figma_get_library_variables`](libraries.md#figma_get_library_variables) | Libraries | Variables from the team libraries the current file subscribes to: which tokens — colors, spacing, sizes, strings, flags — are available at all, without a lin… |
| [`figma_import_library_variable`](libraries.md#figma_import_library_variable) | Libraries | Imports one variable from a subscribed library into the current file. |
| [`figma_get_library_style_by_key`](libraries.md#figma_get_library_style_by_key) | Libraries | Metadata of a published library style by its key: name, type, description and where it lives (fileKey plus nodeId). |
| [`figma_search_team_components`](libraries.md#figma_search_team_components) | Libraries | Searches the published components of a whole team library by name or description substring — unlike figma_search_components, which only sees the currently op… |
| [`figjam_create_sticky`](figjam.md#figjam_create_sticky) | FigJam | Creates a sticky note on a FigJam board. |
| [`figjam_create_stickies`](figjam.md#figjam_create_stickies) | FigJam | Creates a batch of stickies in one go — this is how a board is filled from structured data (meeting notes, brainstorm ideas). |
| [`figjam_create_connector`](figjam.md#figjam_create_connector) | FigJam | Connects two nodes with a line: this is how flowcharts, diagrams and relation maps are built. |
| [`figjam_create_shape_with_text`](figjam.md#figjam_create_shape_with_text) | FigJam | Creates a shape with a label: a flowchart node, a process step, a diagram element. |
| [`figjam_create_section`](figjam.md#figjam_create_section) | FigJam | Creates a section — a container for grouping board contents. |
| [`figjam_create_table`](figjam.md#figjam_create_table) | FigJam | Creates a table on the board: comparison matrices, structured data. |
| [`figjam_create_code_block`](figjam.md#figjam_create_code_block) | FigJam | Creates a code block on the board — for code snippets, configuration examples and technical notes. |
| [`figjam_auto_arrange`](figjam.md#figjam_auto_arrange) | FigJam | Lays board nodes out in a grid, a row or a column — handy after creating elements in bulk. |
| [`figjam_get_board_contents`](figjam.md#figjam_get_board_contents) | FigJam | Reads the contents of a board: stickies, shapes, connectors, tables, code blocks and sections with their text and coordinates. |
| [`figjam_get_connections`](figjam.md#figjam_get_connections) | FigJam | Returns the relation graph of a board: which connectors join which nodes and with which labels. |
| [`figma_list_slides`](slides.md#figma_list_slides) | Slides | The list of presentation slides: ids, names, grid positions and the skip flag. |
| [`figma_get_slide_content`](slides.md#figma_get_slide_content) | Slides | The content tree of a slide: texts, shapes and frames with their properties. |
| [`figma_get_slide_grid`](slides.md#figma_get_slide_grid) | Slides | The two-dimensional slide grid: how the slides are laid out in rows and columns. |
| [`figma_get_slide_transition`](slides.md#figma_get_slide_transition) | Slides | The current transition settings of a slide: style, duration, easing curve. |
| [`figma_get_focused_slide`](slides.md#figma_get_focused_slide) | Slides | The slide currently open in single-slide mode. |
| [`figma_create_slide`](slides.md#figma_create_slide) | Slides | Creates an empty slide. |
| [`figma_delete_slide`](slides.md#figma_delete_slide) | Slides | Deletes a slide from the presentation. |
| [`figma_duplicate_slide`](slides.md#figma_duplicate_slide) | Slides | Duplicates a slide. |
| [`figma_reorder_slides`](slides.md#figma_reorder_slides) | Slides | Rearranges the slides into a new grid. |
| [`figma_set_slide_transition`](slides.md#figma_set_slide_transition) | Slides | Sets the transition of a slide. |
| [`figma_skip_slide`](slides.md#figma_skip_slide) | Slides | Turns skipping a slide in presentation mode on and off. |
| [`figma_add_text_to_slide`](slides.md#figma_add_text_to_slide) | Slides | Adds text to a slide: font, color, alignment and the rest of the formatting. |
| [`figma_add_shape_to_slide`](slides.md#figma_add_shape_to_slide) | Slides | Adds a rectangle or an ellipse to a slide. |
| [`figma_set_slide_background`](slides.md#figma_set_slide_background) | Slides | Sets the slide background: it creates or updates a rectangle covering the whole slide. |
| [`figma_set_slides_view_mode`](slides.md#figma_set_slides_view_mode) | Slides | Switches the presentation view: mode is 'grid' or 'single-slide'. |
| [`figma_focus_slide`](slides.md#figma_focus_slide) | Slides | Moves to a slide in single-slide mode. |
| [`figma_create_slot`](slots.md#figma_create_slot) | Slots | Creates a SlotNode inside a component and at the same time defines a linked property of type SLOT with the same name (renaming the slot renames the property). |
| [`figma_get_slots`](slots.md#figma_get_slots) | Slots | The list of slots of a component, a component set or an instance: ids, names, property keys, sizes and the current contents. |
| [`figma_append_to_slot`](slots.md#figma_append_to_slot) | Slots | Puts content into an instance slot: it either clones sourceNodeId there or creates a new node by nodeType (FRAME, RECTANGLE, ELLIPSE, TEXT, LINE, POLYGON, ST… |
| [`figma_reset_slot`](slots.md#figma_reset_slot) | Slots | Clears a slot, returning it to its default state. |
| [`figma_add_slot_property`](slots.md#figma_add_slot_property) | Slots | The manual alternative to figma_create_slot: it defines a property of type SLOT and binds it to an already existing frame. |
| [`figma_lint_design`](quality.md#figma_lint_design) | Quality and annotations | Checks a subtree for the usual defects: values off the tokens, detached styles, elements hugging instead of filling, inconsistent spacing. |
| [`figma_audit_component_accessibility`](quality.md#figma_audit_component_accessibility) | Quality and annotations | Accessibility audit of a component: text contrast, tap target sizes, presence of text alternatives. |
| [`figma_get_annotations`](quality.md#figma_get_annotations) | Quality and annotations | Developer annotations on a node — the notes a designer left for the implementation. |
| [`figma_set_annotations`](quality.md#figma_set_annotations) | Quality and annotations | Puts annotations on a node. |
| [`figma_get_annotation_categories`](quality.md#figma_get_annotation_categories) | Quality and annotations | The annotation categories defined in the file. |
| [`figma_add_annotation_category`](quality.md#figma_add_annotation_category) | Quality and annotations | Creates an annotation category. |
| [`figma_list_open_files`](session.md#figma_list_open_files) | Session and console | Shows every Figma file the plugin is currently running in, which of them is the target of the commands, and which other agents work in each. |
| [`figma_navigate`](session.md#figma_navigate) | Session and console | Selects the file the following commands will go to, by a link to a Figma file or its key. |
| [`figma_execute_across_files`](session.md#figma_execute_across_files) | Session and console | Runs the same JavaScript IN SEVERAL Figma files AT ONCE — in parallel, without moving the target. |
| [`figma_reconnect`](session.md#figma_reconnect) | Session and console | Re-reads the list of connected files and reports the target. |
| [`figma_reload_plugin`](session.md#figma_reload_plugin) | Session and console | Reloads the plugin UI in Figma. |
| [`figma_get_console_logs`](session.md#figma_get_console_logs) | Session and console | The latest console lines from the plugin sandbox. |
| [`figma_get_remarks`](session.md#figma_get_remarks) | Session and console | Reads what the human typed in the plugin panel: their words plus the layers they had selected. |
| [`figma_clear_console`](session.md#figma_clear_console) | Session and console | Clears the console buffer, both on the server and in the plugin. |
| [`figma_watch_console`](session.md#figma_watch_console) | Session and console | Waits for new lines to appear in the plugin console up to the given timeout. |
| [`figma_export_node`](session.md#figma_export_node) | Session and console | Exports a node through the live editor: SVG returns the markup itself, PDF/PNG/JPG are saved to disk and the path is returned. |
| [`figma_get_active_users`](session.md#figma_get_active_users) | Session and console | Who is in the file right now: the token owner (me) and the other live collaborators with their multiplayer colors. |
| [`figma_notify`](session.md#figma_notify) | Session and console | Shows a toast inside the Figma window — the way to tell the human at the canvas that a batch of work is done or needs their eyes. |
| [`figma_scroll_to_node`](session.md#figma_scroll_to_node) | Session and console | Scrolls the user's viewport to a node, switching pages when needed; select=true also selects it. |
| [`figma_set_file_thumbnail`](session.md#figma_set_file_thumbnail) | Session and console | Makes a frame the file cover shown in the project browser; an empty nodeId resets to the default. |
| [`figma_take_screenshot`](session.md#figma_take_screenshot) | Session and console | Captures a node or the current page straight from the plugin. |
| [`figma_get_text_styles`](session.md#figma_get_text_styles) | Session and console | The text styles of a file read through the plugin: name, size, weight, line height. |
| [`csharp_diagnose`](diagnostics.md#csharp_diagnose) | Diagnostics | Self-check of the Snail.MCP.Figma server: the active configuration, the presence of the Snail Bridge JS plugin and the state of the cache. |
