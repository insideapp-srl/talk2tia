# Talk2Tia — Getting started

> **Public beta (v0.9).** Talk2Tia is feature-complete but still in beta: use it on test projects, review every change
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

## Environment variables

| Variable | Default | Purpose |
|---|---|---|
| `TALK2TIA_TOOLSETS` | `all` | Comma-separated toolsets to expose: `portal,hardware,plc,online,library,hmi,object`. Fewer tools = less context for the model. |
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
