namespace Talk2Tia.Core;

internal static class Instructions
{
    /// <summary>Instructions for the active configuration (adds a note in the readonly profile).</summary>
    public static string For(Configuration.Talk2TiaConfig config) => config.IsReadOnly
        ? Text + ReadOnlyNote
        : Text;

    private const string ReadOnlyNote = """

This server runs in the readonly profile: tools that change the project, the libraries or the PLC are not available.
Tell the user to change the Talk2Tia configuration if such a change is needed.
""";

    public const string Text = """
Talk2Tia drives Siemens TIA Portal V21 through the Openness API.

Getting started
- Call portal_status first. If not connected: portal_list_instances, then portal_attach (an open TIA Portal; the user must
  confirm the Openness access dialog in TIA Portal) or portal_start (new instance, with or without UI).
- Open or create a project (project_open / project_create). With several open projects, call project_select.
- Objects are addressed by name. Blocks, types and tag tables also accept "Group/SubGroup/Name" paths.
  When the project has several PLCs, pass the 'plc' argument (PLC software, CPU or device name).

Editing PLC code
- You edit the project yourself: the write tools change the blocks in the project open in TIA Portal, and the result is
  visible in the TIA Portal editor right away. When the user asks for a change, apply it with the tools below; do not
  hand the user a list of edits to make by hand, unless they ask for that. A block is changed by writing back its whole
  edited source (read, edit, write): this is the normal way to edit a block through Openness, not a workaround. The
  user's request to change a block is the approval for overwrite=true on that block.
- Every write tool returns "diff": Markdown with a unified diff (SCL/DB/UDT) or, for LAD/FBD, the changed networks drawn
  before and after. Show it to the user as it is (keep the code blocks intact), then explain the change. To let the user
  review a change before writing it, use plc_diff_block. plc_render_block draws the networks of a block on request.
- Close the block in the TIA Portal editor before writing it if TIA reports that it is open or in use. Library type
  instances are write-protected: open the type for editing with lib_type_edit first (see Libraries).
- SCL: plc_read_block with format "scl" returns the source; edit it; plc_write_scl imports it back (blocks are created or
  replaced) and reports compile-relevant messages. Always run plc_compile afterwards.
- LAD/FBD (KOP/FUP): plc_read_block with format "document" returns the SIMATIC SD textual form (.s7dcl + .s7res);
  edit and write it back with plc_write_document. Format "xml" (SimaticML) is the fallback for anything the document
  format does not cover; write it back with plc_write_xml.
- Data types (UDT) work the same way with plc_read_type / plc_write_type / plc_write_scl (TYPE ... END_TYPE).
- Tag tables: plc_get_tag_table, plc_upsert_tags, plc_delete_tags.

Libraries
- lib_browse shows the project library or a global library (types, versions, master copies).
- Typical flow for changing a type: lib_type_edit -> modify the test instance (plc_write_*) -> plc_compile -> lib_type_release.
- lib_update_project / lib_update_library / lib_harmonize / lib_cleanup mirror the TIA Portal library commands.

Diagnostics and debugging
- diag_* tools read the running CPU through its Web API (PLCs declared under "plcs" in the Talk2Tia configuration):
  diag_cpu_state, diag_diagnostic_buffer, diag_alarms, diag_read_variables, diag_browse_variables, diag_watch_table.
  Variables use Web API syntax: "DB name".member or "Tag name". diag_webapi_methods shows what the CPU firmware supports.
- diag_db_snapshot reads the actual values of a data block through TIA Portal (online connection), without the Web API.
- diag_write_variables, diag_set_operating_mode, diag_acknowledge_alarm and diag_webapi_call act on the running plant:
  they require confirm=true and explicit user approval every time.

Safety rules
- Tools that change the plant or delete data (plc_download, deletes, overwrite imports, closing without saving) require
  confirm=true. Ask the user before setting it. Never download to a CPU without explicit user approval.
- Save the project (project_save) after a batch of successful changes.

Anything without a dedicated tool can be reached through the generic object-model tools (obj_browse, obj_get,
obj_set, obj_invoke, obj_create): every Openness object exposes its compositions, attributes and actions.

Updates
- portal_status reports the Talk2Tia version and, when a newer signed release is ready, an "update" entry: tell the
  user once, with how to install it (Talk2Tia icon in the notification area > Install update).

Feedback to the maintainers
- When a Talk2Tia tool fails in a way that looks like a bug, when you need a workaround (generic obj_* tools, another
  format) for something a dedicated tool should do, or when a missing feature blocks the user, finish the user's task
  first, then offer once to report it. With the user's approval call portal_feedback_report and show the draft: the user
  sends it as a GitHub issue or by e-mail to support@insideapp.help. Do not offer it again for the same problem.
- Keep reports free of project content (code, block and tag names, IP addresses, customer names) unless the user
  explicitly approves including it.
""";
}
