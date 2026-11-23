# Talk2Tia — Tool catalog

121 tools grouped in toolsets. Choose toolsets, a readonly profile and single tools with the configuration file or the
`TALK2TIA_TOOLSETS` environment variable (comma separated:
`portal,hardware,plc,online,diagnostics,diagnostics_write,library,hmi,object`; default: all). `portal` is always enabled.
See *Configuration* in the getting started guide.

Legend: **RO** = read-only, **⚠** = destructive (needs `confirm=true` or `overwrite=true`). The readonly profile exposes
the RO tools plus `portal_attach`, `portal_start`, `portal_disconnect`, `project_open`, `project_select`, `project_close`,
`lib_open_global`, `online_configure`, `online_go_online`, `online_go_offline` and `diag_db_snapshot`.


## Session (toolset: portal)

| Tool | | Description |
|---|---|---|
| `portal_status` | RO | Shows whether Talk2Tia is connected to TIA Portal, the connection mode and the open projects. |
| `portal_list_instances` | RO | Lists the running TIA Portal processes with their id, mode and open project. |
| `portal_attach` |  | Attaches to a running TIA Portal instance. The user must confirm the Openness access dialog in TIA Portal. Without processId, attaches to the only running instance. |
| `portal_start` |  | Starts a new TIA Portal instance owned by Talk2Tia. Use withUserInterface=false for headless automation (faster). |
| `portal_disconnect` |  | Releases the connection. An instance started by Talk2Tia is closed (unsaved changes are lost, so confirm=true is required when a project has unsaved changes); an attached instance keeps running. |

## Project (toolset: portal)

| Tool | | Description |
|---|---|---|
| `project_info` | RO | Returns details of the active project: path, author, languages, devices and software (PLC/HMI). |
| `project_open` |  | Opens a TIA Portal project (.ap21). Set upgrade=true to open a project from an older TIA Portal version. |
| `project_create` |  | Creates a new empty project in the given directory and makes it active. |
| `project_select` |  | Selects which open project is the active one for the following tools. |
| `project_save` |  | Saves the active project. |
| `project_save_as` |  | Saves the active project under a new directory (TIA 'Save as'). |
| `project_close` | ⚠ | Closes the active project. Unsaved changes are lost: confirm=true is required if the project is modified. |
| `project_archive` |  | Archives the active project (.zap21). Mode: Compressed (default), None, DiscardRestorableData, DiscardRestorableDataAndCompressed. |
| `project_retrieve` |  | Retrieves (extracts and opens) an archived project (.zapXX). |
| `project_export_texts` |  | Exports the project texts (for translation) to an .xlsx file. |
| `project_import_texts` |  | Imports translated project texts from an .xlsx file previously exported with project_export_texts. |

## Hardware & networks (toolset: hardware)

| Tool | | Description |
|---|---|---|
| `hw_list_devices` | RO | Returns the hardware tree: devices, their modules (rack position, type, order number) and network interfaces. |
| `hw_get_item` | RO | Returns all attributes of a device or device item (module, interface, port). Use 'Device/Item' to disambiguate. |
| `hw_set_attributes` |  | Sets attributes of a device or device item (e.g. Name, Comment, cycle time, protection, IP parameters on a node via obj_set). |
| `hw_catalog_search` | RO | Searches the hardware catalog by text or order number. Returns type identifiers to use with hw_create_device / hw_plug_module. |
| `hw_create_device` |  | Creates a device with its main module. typeIdentifier example: 'OrderNumber:6ES7 513-1AL02-0AB0/V2.9'. |
| `hw_plug_module` |  | Plugs a new module into a rack or device item at a slot position. |
| `hw_delete` | ⚠ | Deletes a device or a device item (module). Requires confirm=true. |
| `hw_list_subnets` | RO | Lists the subnets with their connected nodes and IO systems. |
| `hw_create_subnet` |  | Creates a subnet. typeIdentifier: System:Subnet.Ethernet, System:Subnet.Profibus, System:Subnet.Mpi, ... |
| `hw_connect_node` |  | Connects a network interface node of a device item to a subnet (created if missing), optionally setting its address. |
| `hw_disconnect_node` |  | Disconnects a network interface node from its subnet. |

## PLC blocks (toolset: plc)

| Tool | | Description |
|---|---|---|
| `plc_list` | RO | Lists the PLCs in the active project with device, CPU and type. |
| `plc_list_blocks` | RO | Lists program blocks (OB, FB, FC, DB) with path, number, language and consistency. Optionally limited to a group or a name filter. |
| `plc_list_groups` | RO | Lists the block group (folder) tree of a PLC. |
| `plc_get_block` | RO | Returns all attributes of a block (number, language, header, memory, dates, optimized access, ...). |
| `plc_read_block` | RO | Reads the code of a block. format: 'auto' (SCL source for SCL/STL/DB, SIMATIC SD document for LAD/FBD, XML otherwise), 'scl' (textual source: .scl/.db/.awl), 'document' (SIMATIC SD .s7dcl/.s7res, the editable text form for LAD/FBD and SCL), 'xml' (SimaticML, works for every language but needs a consistent block). Returns the content inline or saves it to outputDirectory. |
| `plc_write_scl` | ⚠ | Creates or replaces blocks/types from SCL source text (FUNCTION_BLOCK, FUNCTION, ORGANIZATION_BLOCK, DATA_BLOCK, TYPE; also .db/.udt/.awl sources). The source is imported through a temporary external source and compiled into blocks. Replacing existing blocks requires overwrite=true. |
| `plc_write_document` | ⚠ | Creates or replaces a block from SIMATIC SD documents (the text form returned by plc_read_block format='document'). Use it to edit LAD/FBD (KOP/FUP) and SCL blocks. Replacing an existing block requires overwrite=true. |
| `plc_write_xml` | ⚠ | Imports blocks from SimaticML XML (as returned by plc_read_block format='xml'). Works for every language. Replacing existing blocks requires overwrite=true. |
| `plc_create_group` |  | Creates a block group path (all missing levels). |
| `plc_delete_group` | ⚠ | Deletes a block group and all blocks inside it. Requires confirm=true. |
| `plc_delete_block` | ⚠ | Deletes a block. Requires confirm=true. |
| `plc_set_block_attributes` |  | Changes block properties. Supported keys include Name, Number, AutoNumber, HeaderAuthor, HeaderFamily, HeaderName, HeaderVersion, MemoryLayout, and any writable attribute shown by plc_get_block. Use 'Title'/'Comment' with an optional culture. |
| `plc_create_instance_db` |  | Creates an instance DB for a function block. |
| `plc_create_fb` |  | Creates an empty function block in the given language (LAD, FBD, SCL, STL, GRAPH). For blocks with code prefer plc_write_scl or plc_write_document. |
| `plc_show_in_editor` | RO | Opens the block in the TIA Portal editor (only with a user interface). |
| `plc_cross_references` | RO | Returns the cross references of a block: where it is used and what it uses. |

## PLC data types (toolset: plc)

| Tool | | Description |
|---|---|---|
| `plc_list_types` | RO | Lists the PLC data types (UDT) with their group path and consistency. |
| `plc_read_type` | RO | Reads a PLC data type. format: 'scl' (.udt source, default), 'document' (SIMATIC SD) or 'xml' (SimaticML). |
| `plc_write_type` | ⚠ | Creates or replaces a PLC data type from SIMATIC SD documents (format 'document') or SimaticML (format 'xml'). For TYPE ... END_TYPE source text use plc_write_scl. Replacing requires overwrite=true. |
| `plc_delete_type` | ⚠ | Deletes a PLC data type. Requires confirm=true. |

## PLC tags & constants (toolset: plc)

| Tool | | Description |
|---|---|---|
| `plc_list_tag_tables` | RO | Lists the PLC tag tables with tag and constant counts. |
| `plc_get_tag_table` | RO | Returns the tags and user constants of a tag table. |
| `plc_find_tag` | RO | Searches tags by name or address in all tag tables of a PLC. |
| `plc_create_tag_table` |  | Creates a tag table (and missing groups). |
| `plc_delete_tag_table` | ⚠ | Deletes a tag table with all its tags. Requires confirm=true. |
| `plc_upsert_tags` |  | Creates or updates tags in a tag table in one call. Existing tags (same name) are updated with the given fields. |
| `plc_delete_tags` | ⚠ | Deletes tags from a tag table. Requires confirm=true. |
| `plc_upsert_constants` |  | Creates or updates user constants in a tag table. |
| `plc_export_tag_table` | RO | Exports a tag table as SimaticML XML (inline or to outputDirectory). |
| `plc_import_tag_table` | ⚠ | Imports tag tables from SimaticML XML. Replacing existing tables requires overwrite=true. |

## External sources (toolset: plc)

| Tool | | Description |
|---|---|---|
| `plc_list_external_sources` | RO | Lists the external source files stored in the PLC. |
| `plc_add_external_source` |  | Adds a source file (.scl, .db, .udt, .awl) to the PLC external sources without generating blocks. |
| `plc_generate_blocks_from_source` | ⚠ | Generates (or regenerates) blocks from an external source already in the project. Existing blocks with the same name are replaced. |
| `plc_generate_source` |  | Generates one source file from several blocks and/or types (e.g. a full SCL library for review or versioning). |
| `plc_delete_external_source` | ⚠ | Deletes an external source from the project (generated blocks are kept). Requires confirm=true. |

## Compile (toolset: online)

| Tool | | Description |
|---|---|---|
| `plc_compile` |  | Compiles a PLC or HMI. scope: 'software' (program changes, default), 'hardware' (device configuration and software), or 'block' (a single block). Returns state, error/warning counts and the messages with their location. |

## Online, compare & download (toolset: online)

| Tool | | Description |
|---|---|---|
| `online_connection_options` | RO | Lists the connection modes (e.g. PN/IE), PC interfaces, target interfaces and addresses available for a PLC, plus the currently configured connection. Use the names with online_configure. |
| `online_accessible_devices` | RO | Scans the network through a PC interface and lists the accessible devices (like 'Accessible devices' in TIA Portal). |
| `online_configure` |  | Configures the online connection of a PLC (mode, PC interface and target interface or IP address). |
| `online_state` | RO | Returns the online state of a PLC (Offline, Online, NotReachable, ...). |
| `online_go_online` |  | Establishes an online connection to the PLC using the configured connection. |
| `online_go_offline` |  | Closes the online connection to the PLC. |
| `plc_compare_online` | RO | Compares the offline program with the PLC (online). Lists the objects that differ. |
| `plc_compare` | RO | Compares the program of two PLCs in the project (offline/offline). |
| `plc_download` | ⚠ | Downloads to the real PLC using the configured online connection. This changes the running plant: ALWAYS ask the user first and pass confirm=true. options: Software (default), SoftwareOnlyChanges, Hardware, or 'Hardware,Software'. The CPU is stopped only if allowStop=true; data blocks are re-initialized only if reinitializeDataBlocks=true. |

## Diagnostics & debugging (toolset: diagnostics)

Runtime data through the CPU Web API (PLCs declared in the configuration file) and through Openness.

| Tool | | Description |
|---|---|---|
| `diag_list_plcs` | RO | Lists the PLCs configured for the Web API (name, address, user). With check=true pings each PLC, logs in and reports the rights of the user and the Web API version. |
| `diag_cpu_state` | RO | Reads the CPU operating mode (run, stop, startup, hold, ...), its system time and the Web API version. |
| `diag_read_variables` | RO | Reads the current values of PLC variables in one request. Use symbolic names in Web API syntax: "DB name".member.sub[2] for data blocks (optimized blocks included) or "Tag name" for PLC tags (inputs, outputs, memory). The variables must be accessible from HMI/OPC UA/Web API in TIA Portal. |
| `diag_browse_variables` | RO | Browses the variables available through the Web API: without 'variable' lists the top level (data blocks and tags); with 'variable' lists its members (children) or describes it (var). Returns names, data types and whether they are read-only. |
| `diag_diagnostic_buffer` | RO | Reads the CPU diagnostic buffer (newest first): timestamp, incoming/outgoing status, short, long and help texts. Requires CPU firmware with DiagnosticBuffer.Browse (S7-1500 V3.0 or later). |
| `diag_alarms` | RO | Reads the active alarms of the CPU (program alarms and system diagnostics): id, number, status, timestamp, texts and acknowledgement state. Requires CPU firmware with Alarms.Browse (S7-1500 V3.0 or later). |
| `diag_webapi_methods` | RO | Lists the Web API methods supported by the CPU firmware and the rights of the configured user. Use it to find out which diagnostics are available on a given CPU. |
| `diag_list_watch_tables` | RO | Lists the watch tables of a PLC in the TIA project (with group paths) and their number of entries. |
| `diag_watch_table` | RO | Returns the entries of a watch table of the TIA project and, with monitor=true (default), reads their current values from the CPU through the Web API, like 'Monitor all' in TIA Portal. Entries with only an absolute address cannot be read through the Web API. |
| `diag_db_snapshot` |  | Takes a snapshot of the actual values of a data block from the CPU through TIA Portal (the PLC must be reachable online) and returns them. With create=false returns the last snapshot stored in the project. Works without the Web API. The snapshot is stored in the offline project; the PLC is not changed. |

## Runtime actions (toolset: diagnostics_write)

Act on the running CPU. Excluded from the readonly profile; every call needs `confirm=true`.

| Tool | | Description |
|---|---|---|
| `diag_write_variables` | ⚠ | Writes values to PLC variables of the running CPU (like 'Modify' in a watch table). This changes the plant: ALWAYS ask the user first and pass confirm=true. Variables use Web API syntax ("DB name".member). |
| `diag_set_operating_mode` | ⚠ | Requests a CPU operating mode change to run or stop. Stopping the CPU stops the machine: ALWAYS ask the user first and pass confirm=true. |
| `diag_acknowledge_alarm` | ⚠ | Acknowledges an alarm of the CPU by id (from diag_alarms). Ask the user first and pass confirm=true. |
| `diag_webapi_call` | ⚠ | Calls any Web API method of the CPU (e.g. Modules.ReadStatus, Syslog.Browse, Plc.ReadSystemTime) for what has no dedicated tool. It can also change the CPU, so it always needs confirm=true: ask the user first. Use diag_webapi_methods to list the supported methods. |

## Libraries (toolset: library)

| Tool | | Description |
|---|---|---|
| `lib_list_global` | RO | Lists the open global libraries and the known ones (system, corporate, user) with path and state. |
| `lib_open_global` |  | Opens a global library (.al21). Opened read-only unless readOnly=false. Set upgrade=true for older versions. |
| `lib_create_global` |  | Creates a new user global library. |
| `lib_save_global` |  | Saves an open global library (must be opened read-write). |
| `lib_close_global` | ⚠ | Closes a global library. Unsaved changes are lost unless save=true (confirm=true needed to discard them). |
| `lib_archive_global` |  | Archives a global library (.zal21). |
| `lib_browse` | RO | Shows the content of a library: type folders, types with their versions (state, default, author) and master copies. |
| `lib_type_info` | RO | Details of a library type: all versions, dependencies, dependents and the instances in a PLC. |
| `lib_create_type_from_block` |  | Creates a new library type from an existing block or PLC data type (like 'Add type' in TIA Portal). The new type is created in the project library (optionally in a type folder). |
| `lib_instantiate_type` |  | Creates an instance of a library type (block or PLC data type) in a PLC. Uses the default version unless 'version' is given. |
| `lib_type_edit` |  | Starts editing a project library type: creates an in-work version and its test instance (like 'Edit type'). Then change the instance with plc_write_* tools, compile, and finish with lib_type_release (or lib_type_discard). |
| `lib_type_release` |  | Releases the in-work version of a project library type (like 'Release version'). |
| `lib_type_discard` | ⚠ | Discards the in-work version of a project library type. Requires confirm=true. |
| `lib_type_set_default` |  | Sets which version of a library type is the default. |
| `lib_delete` | ⚠ | Deletes a library type, one version of it, or a master copy. Requires confirm=true. |
| `lib_create_folder` |  | Creates a folder (path) in the types or master copies area of a library. |
| `lib_export_type` | RO | Exports a library type version as SIMATIC SD documents (or SimaticML with format='xml') to a directory. |
| `lib_import_type` |  | Creates a library type (or a new version of an existing type) from SIMATIC SD documents in a directory. |
| `lib_update_project` |  | Updates the PLC instances to the latest versions of library types (like 'Update project'). Without types, the whole library is used. |
| `lib_update_library` |  | Copies types from one library to another (e.g. project library -> global library, like 'Update library'). |
| `lib_harmonize` |  | Harmonizes the project with the library (names and/or paths of instances follow the library structure). |
| `lib_cleanup` | ⚠ | Cleans up a library: deletes unused types/versions (like 'Clean up library'). Requires confirm=true. |
| `lib_update_check` | RO | Checks which instances in the project are out of date compared to the library. |
| `lib_create_master_copy` |  | Creates a master copy from a project object (e.g. a PLC data type, a tag table or any object path via obj_* syntax). |
| `lib_instantiate_master_copy` |  | Creates objects in the project from a master copy. target: blocks | types | tagtables | devices. |

## HMI (toolset: hmi)

| Tool | | Description |
|---|---|---|
| `hmi_list` | RO | Lists the HMI devices (WinCC Comfort/Advanced and WinCC Unified) in the project. |
| `hmi_list_screens` | RO | Lists the screens of an HMI (with folder path). |
| `hmi_list_tags` | RO | Lists the HMI tag tables and their tags (name, data type, connection, PLC tag). |
| `hmi_export` | RO | Exports a WinCC Comfort/Advanced screen or tag table as SimaticML XML (inline or to outputDirectory). For WinCC Unified objects use obj_browse / obj_get. |
| `hmi_import` | ⚠ | Imports a WinCC Comfort/Advanced screen or tag table from SimaticML XML. Replacing requires overwrite=true. |

## Generic object model (toolset: object)

| Tool | | Description |
|---|---|---|
| `obj_browse` | RO | Describes any Openness object: its compositions, attributes (with access mode and types), services and actions. For a composition, lists its items and what can be created in it. Start from 'project' and walk down. |
| `obj_get` | RO | Reads the attributes of any Openness object (all readable attributes, or only the listed ones). |
| `obj_set` |  | Writes attributes of any Openness object. Values are converted to the attribute type (enums by name). Multilingual texts are set for 'culture' (default: first project language). |
| `obj_invoke` | ⚠ | Invokes an action of an Openness object or composition (see 'actions' in obj_browse), e.g. Delete, Compile, ShowInEditor. Parameters are passed by name; an object parameter can be given as {"path": "..."}. Deleting actions require confirm=true. |
| `obj_create` |  | Creates a new object in a composition through the generic API (see 'creatable' in obj_browse on the composition). Parameters are passed by name. |
