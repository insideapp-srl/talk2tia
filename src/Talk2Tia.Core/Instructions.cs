namespace Talk2Tia.Core;

internal static class Instructions
{
    public const string Text = """
Talk2Tia drives Siemens TIA Portal V21 through the Openness API.

Getting started
- Call portal_status first. If not connected: portal_list_instances, then portal_attach (an open TIA Portal; the user must
  confirm the Openness access dialog in TIA Portal) or portal_start (new instance, with or without UI).
- Open or create a project (project_open / project_create). With several open projects, call project_select.
- Objects are addressed by name. Blocks, types and tag tables also accept "Group/SubGroup/Name" paths.
  When the project has several PLCs, pass the 'plc' argument (PLC software, CPU or device name).

Editing PLC code
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

Safety rules
- Tools that change the plant or delete data (plc_download, deletes, overwrite imports, closing without saving) require
  confirm=true. Ask the user before setting it. Never download to a CPU without explicit user approval.
- Save the project (project_save) after a batch of successful changes.

Anything without a dedicated tool can be reached through the generic object-model tools (obj_browse, obj_get,
obj_set, obj_invoke, obj_create): every Openness object exposes its compositions, attributes and actions.
""";
}
