namespace Snail.MCP.Figma.Tools.Prompts;

/// <summary>What the client hands the model at the handshake, before any tool is loaded: where to start, which family answers which task, and when one script beats a chain of calls.</summary>
/// <remarks>A host that discovers tools on demand shows the model only this text and a search box, so every family is named here by the words a task is described with; <c>ServerInstructionsTests</c> keeps the map complete and the references real.</remarks>
public static class ServerInstructions
{
    public const string Text = """
        Snail.MCP.Figma: 141 tools for reading and editing Figma. Reads go through the Figma Desktop plugin while it runs in the file (unsaved edits visible, no API quota) and fall back to the REST API; every write needs the plugin.

        Start of a task: figma_navigate with the link of the file you work in. It binds the target and keeps it; with several files open the first write is refused until you do. figma_get_status shows the bridge, figma_get_selection what the human has selected.

        Tool families, by the words a task is described with:
        - read a file, page, node, component, styles, variables, render a picture: figma_get_file_data, figma_get_component, figma_get_styles, figma_get_variables, figma_get_component_image
        - edit nodes on the canvas (move, resize, fills, strokes, text, rename, clone, delete, group): figma_move_node, figma_set_fills, figma_set_text, figma_delete_node
        - create nodes and layouts, import SVG, place images: figma_create_child, figma_create_node_from_jsx, figma_create_node_from_svg, figma_place_image
        - components, variants, instances and their properties: figma_search_components, figma_instantiate_component, figma_set_instance_properties, figma_create_component_set
        - variables, collections, modes (themes, breakpoints): figma_create_variable, figma_batch_create_variables, figma_batch_update_variables, figma_add_mode, figma_setup_design_tokens
        - design tokens in code formats (DTCG, Tokens Studio, CSS, SCSS, Tailwind, Style Dictionary): figma_export_tokens, figma_import_tokens, figma_convert_tokens
        - design to code: a component spec for implementation, parity check, Markdown docs: figma_get_component_for_development, figma_check_design_parity, figma_generate_component_doc
        - the whole design system in one call (styles, variables, components, images): figma_get_design_system_kit
        - a design system extracted out of an existing codebase: figma_ds_analyze, figma_ds_extract_tokens, figma_ds_scaffold, figma_ds_extract_component, figma_ds_verify, figma_ds_status
        - version history, diffs, blame, changelog, named checkpoints: figma_get_file_versions, figma_diff_versions, figma_blame_node, figma_generate_changelog, figma_save_version
        - comments: figma_get_comments, figma_post_comment; team libraries: figma_search_team_components, figma_get_library_variables, figma_import_library_variable
        - FigJam boards (stickies, shapes, connectors, tables, sections): figjam_create_sticky, figjam_create_connector, figjam_get_board_contents
        - Figma Slides: figma_list_slides, figma_create_slide, figma_add_text_to_slide, figma_set_slide_transition
        - component slots: figma_create_slot, figma_get_slots, figma_append_to_slot
        - quality: lint, accessibility audit, developer annotations: figma_lint_design, figma_audit_component_accessibility, figma_get_annotations, figma_set_annotations
        - session and console: connected files, console logs, screenshots, toasts, viewport: figma_list_open_files, figma_get_console_logs, figma_take_screenshot, figma_notify, figma_scroll_to_node
        - self-check of the server and the plugin: csharp_diagnose

        A build of many steps is one script, not a chain of calls: figma_execute runs JavaScript against the Plugin API inside Figma, figma_execute_steps runs a sequence the human can watch grow; intermediate data stays in the sandbox and only the return value comes back. When a script fails, figma_get_console_logs has the reason. Check the result with figma_take_screenshot: a script answers success even when the picture is wrong.

        The human can type a remark into the plugin panel while you work: figma_get_remarks reads it. Between the steps of a long job, read it.
        """;
}
