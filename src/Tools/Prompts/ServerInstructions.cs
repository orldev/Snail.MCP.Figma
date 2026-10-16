namespace Snail.MCP.Figma.Tools.Prompts;

/// <summary>What the client hands the model at the handshake, before any tool is loaded: where to start, which family answers which task, and when one script beats a chain of calls.</summary>
/// <remarks>A host that discovers tools on demand shows the model only this text and a search box, so every family is named here by the words a task is described with. Claude Code cuts the text at 2,048 characters, so what matters most comes first and the whole stays under that; <c>ServerInstructionsTests</c> keeps the map complete, the references real and the length in.</remarks>
public static class ServerInstructions
{
    /// <summary>The instructions text the server registers for the MCP handshake.</summary>
    public const string Text = """
        Snail.MCP.Figma: 141 tools for reading and editing Figma. Reads go through the Figma Desktop plugin while it runs in the file (unsaved edits visible, no quota) and fall back to REST; every write needs the plugin.

        Start with figma_navigate and the link of the file you work in: it binds the target, and with several files open a write is refused until you do. figma_get_status shows the bridge.

        A build of many steps is one script, not a chain of calls: figma_execute runs JavaScript against the Plugin API inside Figma, figma_execute_steps runs a sequence the human can watch; only the return value comes back. A failed script explains itself in figma_get_console_logs. Check the result with figma_take_screenshot: a script answers success even when the picture is wrong. The human can type a remark into the plugin panel while you work: read it with figma_get_remarks between steps.

        Families, by the words to search for:
        - read file, node, styles, variables, render: figma_get_file_data, figma_get_variables, figma_get_component_image
        - edit nodes (move, resize, fills, text, delete): figma_move_node, figma_set_fills; create nodes, SVG, images: figma_create_child, figma_create_node_from_jsx
        - components, variants, instances: figma_search_components, figma_instantiate_component
        - variables, modes, themes: figma_create_variable, figma_setup_design_tokens; tokens in code formats: figma_export_tokens, figma_import_tokens
        - design to code, parity, docs: figma_get_component_for_development, figma_check_design_parity; whole design system: figma_get_design_system_kit; from a codebase: figma_ds_analyze
        - versions, diffs, blame: figma_get_file_versions, figma_diff_versions; comments: figma_get_comments; team libraries: figma_search_team_components
        - FigJam: figjam_create_sticky, figjam_get_board_contents; Slides: figma_list_slides; slots: figma_get_slots
        - lint, accessibility, annotations: figma_lint_design, figma_get_annotations; selection: figma_get_selection; self-check: csharp_diagnose
        """;
}
