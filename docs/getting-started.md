# Talk2Tia — Getting started

> **Public beta (v0.11).** Talk2Tia is feature-complete but still in beta: use it on test projects, review every change
> before saving or downloading, and please report issues on [GitHub](https://github.com/insideapp-srl/talk2tia/issues).

Talk2Tia is an MCP server that lets an AI assistant (Claude Code, Claude Desktop or any MCP client)
work inside Siemens TIA Portal V21 through the official Openness API.

## Requirements

- Windows 10/11 x64
- TIA Portal V21 with the **TIA Portal Openness** option installed
- The Windows user must be a member of the local group **Siemens TIA Openness**
  (sign out and in again after being added)
- .NET Framework 4.8 (included in Windows 10/11)

Run `talk2tia --doctor` to check the environment.

## Build from source

```powershell
dotnet build -c Release
dotnet test
```

The Siemens assemblies are referenced from the TIA Portal installation
(`C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`) and are never copied or committed.
Override the location with `dotnet build /p:TiaPublicApiDir=...` or, at runtime, with `TALK2TIA_PUBLICAPI_DIR`.

The executable is `src\Talk2Tia.Server\bin\<Configuration>\net48\talk2tia.exe`.

## Configure an MCP client

### Claude Code

```powershell
claude mcp add talk2tia -- "C:\Program Files\Talk2Tia\talk2tia.exe"
```

### Claude Desktop (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "talk2tia": {
      "command": "C:\\Program Files\\Talk2Tia\\talk2tia.exe",
      "env": { "TALK2TIA_TOOLSETS": "all" }
    }
  }
}
```

## Configuration

Talk2Tia works without configuration. An optional JSON file decides which tools the assistant gets and which PLCs it can
read at runtime. Talk2Tia looks for it in this order:

1. `--config <path>` on the command line;
2. the `TALK2TIA_CONFIG` environment variable;
3. `%APPDATA%\Talk2Tia\config.json`, if it exists.

```jsonc
{
  // "full" (default) or "readonly"
  "profile": "readonly",
  // Optional: toolsets to expose (default: all). "portal" is always included.
  "toolsets": ["plc", "online", "diagnostics"],
  // Optional: tools added on top of the profile and toolsets. * is a wildcard.
  "allow": ["plc_compile"],
  // Optional: tools removed in any case. Deny always wins.
  "deny": ["obj_*"],
  // Optional: PLCs reachable through the CPU Web API, for the diagnostics tools.
  "plcs": {
    "Line1": {
      "address": "192.168.0.10",
      "username": "talk2tia",
      "passwordEnv": "TALK2TIA_LINE1_PASSWORD",
      "certificateThumbprint": "3F2A...C9"
    }
  }
}
```

Tools are selected in this order: `toolsets`, then `profile`, then `allow`, then `deny`. Tools left out are not registered
at all, so the assistant does not see them. An unknown name in `allow` or `deny` stops the server with an error, so typos
are caught. Run `talk2tia --doctor --config <path>` to check the file: it prints the profile, how many tools are exposed
and whether each PLC answers.

### Readonly profile

`"profile": "readonly"` (or `--read-only`, or `TALK2TIA_READONLY=1`) exposes only the tools that read: blocks, tags, types,
hardware, libraries, cross references, online compare and the diagnostics. It keeps what is needed to work without changing
anything: attach to or start TIA Portal, open, select and close projects, open global libraries, configure the online connection, go online and
offline, and take data block snapshots. Writing code, tags or hardware, saving, compiling, library changes, downloads,
`obj_set`/`obj_invoke`/`obj_create` and the runtime actions are excluded. Add single tools back with `allow`, for example
`"allow": ["plc_compile"]`.

To give different rights to different clients, point each client to its own file with `TALK2TIA_CONFIG` in its MCP
configuration.

### PLC Web API (diagnostics)

The `diagnostics` tools read the running CPU through its Web API: operating mode, diagnostic buffer, alarms, variable values
and watch tables. Supported CPUs: S7-1500 and ET 200SP CPUs, S7-1200 G2 and PLCSIM Advanced with the web server. The
diagnostic buffer and alarms need S7-1500 firmware V3.0 or later; `diag_webapi_methods` shows what a CPU supports.

On the CPU, in TIA Portal (*Properties > Web server*), then download the hardware configuration:

1. enable the web server on the interface the PC uses;
2. under *User management*, add a user for Talk2Tia with the rights it needs: *read values* and *read diagnostics* for the
   `diagnostics` toolset; *write values*, *change operating mode* and *acknowledge alarms* only if you also enable
   `diagnostics_write`;
3. make the variables accessible: *Accessible from HMI/OPC UA/Web API* in the data blocks and tag tables.

In the configuration file, `passwordEnv` names the environment variable that holds the password: the password itself never
goes in the file. Set it in the MCP client configuration (`env`) or in the Windows user environment.

The CPU uses a self-signed certificate by default. Trust it with `certificateThumbprint` (SHA-1 or SHA-256, from the
certificate shown by the browser), or install the CPU certificate in the Windows certificate store.
`"allowUntrustedCertificate": true` skips the check and is meant only for isolated test networks. `timeoutSeconds` sets the
request timeout (default 10).

The `diagnostics_write` toolset (`diag_write_variables`, `diag_set_operating_mode`, `diag_acknowledge_alarm`,
`diag_webapi_call`) acts on the running plant. It is never part of the readonly profile, and every call needs
`confirm=true`. To leave it out entirely, list the toolsets you want in `toolsets` without `diagnostics_write`.

`diag_db_snapshot` does not use the Web API: it reads the actual values of a data block through TIA Portal while the PLC is
reachable online, like *Snapshot of the actual values* in the data block editor.

## Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `TALK2TIA_CONFIG` | — | Path of the configuration file. |
| `TALK2TIA_TOOLSETS` | `all` | Comma-separated toolsets to expose: `portal,hardware,plc,online,diagnostics,diagnostics_write,library,hmi,object`. Overrides `toolsets` in the file. Fewer tools = less context for the model. |
| `TALK2TIA_READONLY` | `0` | `1` forces the readonly profile. |
| `TALK2TIA_PUBLICAPI_DIR` | from registry | Folder containing `Siemens.Engineering.*.dll` (V21, net48). |

## First session

1. Open TIA Portal and your project (or let the assistant start one with `portal_start`).
2. Ask the assistant to connect: it calls `portal_attach`. TIA Portal shows the Openness access dialog — confirm it.
3. Work in natural language, e.g.
   - "List the blocks of PLC_1 that are not consistent and compile the software."
   - "Read FB_Conveyor in SCL, add a jam-detection timer and write it back."
   - "Convert this network of OB1 (LAD) to use the new motor FB."
   - "Create a library type from FB_Motor, release version 1.0.0 and update all PLCs."
4. Changes are live in the open project but **not saved** until `project_save` (or Save in TIA Portal).

## How code editing works

| Language | Read | Write |
|---|---|---|
| SCL / STL / DB / UDT | `plc_read_block` / `plc_read_type` with format `scl` | `plc_write_scl` (source text, through a temporary external source) |
| LAD / FBD (KOP / FUP) | `plc_read_block` with format `document` (SIMATIC SD `.s7dcl` + `.s7res`) | `plc_write_document` |
| Any language | format `xml` (SimaticML) | `plc_write_xml` |

Always run `plc_compile` after writing code: the tool returns errors and warnings with their location.

## Reviewing changes

Every write tool (`plc_write_scl`, `plc_write_document`, `plc_write_xml`, `plc_write_type`) exports the block before and
after the change and returns a `diff` in Markdown, so the assistant can show exactly what changed in TIA Portal:

- **SCL, DB, UDT**: a unified diff of the source (`+` added, `-` removed lines, three lines of context);
- **LAD and FBD**: only the networks that changed, drawn before and after, with the added and removed elements listed
  and marked in the drawings. Changed SCL networks inside a LAD block and interface changes are shown as diffs.

```text
┃                #MachineValve.Command    #MachineValve.Command
┃ + #DemoEnable        .LifeBit               .CommandRest
┠──────┤ ├────────────────┤/├───────────┬──────────(R)──────────
┃                                       │ #MachineValve.Command
┃                                       │     .CommandWork
┃                                       └──────────(R)──────────
```

`plc_diff_block` shows the diff of a proposed change without writing it, for a review before applying it.
`plc_render_block` draws the networks of any LAD/FBD block on request. Pass `showDiff=false` to a write tool to skip the
diff.

### The talk2tia skill

The repository ships an [Agent Skill](https://github.com/insideapp-srl/talk2tia/tree/main/plugins/talk2tia/skills/talk2tia) with
guidance for the assistant. Its first topic teaches it to always show these diffs, to keep the drawings intact, to explain
them and to offer a review before large changes; new topics will be added to the same skill. It is also attached to each
release as `talk2tia-skill.zip`.

- **Claude Code** (recommended): install the Talk2Tia plugin from the repository's marketplace. It adds the skill and
  registers the MCP server installed by the MSI (`C:\Program Files\Talk2Tia\talk2tia.exe`); if you already added the
  server by hand with the same path, Claude Code keeps your configuration.
  ```text
  /plugin marketplace add insideapp-srl/talk2tia
  /plugin install talk2tia@talk2tia
  ```
  Update it with `/plugin update talk2tia@talk2tia` (or enable auto-update for the marketplace in `/plugin`).
- **Claude Code, skill only**: copy the `talk2tia` folder from the zip to `%USERPROFILE%\.claude\skills\` (all projects)
  or to `.claude\skills\` in a project.
- **Claude Desktop / claude.ai**: upload `talk2tia-skill.zip` in *Settings > Capabilities > Skills*.

The drawings are plain text, so they look the same in every client, including the terminal. An HTML report is optional
and made only on request.

## Safety

- Destructive tools need `confirm=true` (or `overwrite=true` for replacing code). The assistant is instructed to ask first.
- `plc_download` touches the real plant: it requires explicit approval, never stops the CPU unless `allowStop=true`,
  and never re-initializes data blocks unless `reinitializeDataBlocks=true`.
- Every Openness call runs on a single dedicated STA thread, as required by the API.

## Known limitations

- Download dialogs that require a password (protected CPUs) are reported but not answered.
- WinCC Unified screens have no dedicated export tool yet: use the `obj_*` tools.
- Enterprise endpoint protection (e.g. Defender ASR "prevalence" rules) can block a freshly built, unsigned
  `talk2tia.exe`. Release builds must be code-signed; for development ask IT for a folder exclusion.

## Disclaimer

Talk2Tia is provided "as is", without warranty of any kind. InsideApp S.r.l. accepts no liability for any damage,
loss of data, production downtime, injury or other consequence arising from its use, including changes made by AI
assistants to projects, PLC programs or plants. Every change must be reviewed, tested and approved by qualified
personnel before it reaches a real machine. See the [LICENSE](https://github.com/insideapp-srl/talk2tia/blob/main/LICENSE) and the [legal notice](https://talk2tia.insideapp.it/legal) on the website for the full terms.
