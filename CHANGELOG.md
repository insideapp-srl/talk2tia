# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Versions `0.x` are public betas.

## [Unreleased]

## [0.12.0] - 2026-11-25

### Added

- Setup wizard in the MSI installer: the legal notice (disclaimer of warranty and limitation of liability, English and
  Italian) and the MIT licence must be accepted before installing; InsideApp branding; the finish page links to the
  guide and has a "Donate on Ko-fi" button.
- Claude Code plugin marketplace in the repository: `/plugin marketplace add insideapp-srl/talk2tia` and
  `/plugin install talk2tia@talk2tia` install the `talk2tia` skill and register the Talk2Tia MCP server.

### Changed

- The skill moved to `plugins/talk2tia/skills/talk2tia` (the release zip is unchanged).

## [0.11.0] - 2026-11-23

### Added

- Visual diffs: the write tools return what changed in TIA Portal (`diff`, Markdown): a unified diff for SCL/DB/UDT
  sources and, for LAD/FBD blocks, the changed networks drawn as text before and after, with added and removed elements
  marked. New `plc_diff_block` (preview a change without writing it) and `plc_render_block` (draw LAD/FBD networks).
- `talk2tia` Agent Skill (`skills/talk2tia`, attached to releases as `talk2tia-skill.zip`) with guidance for the
  assistant; its first topic shows the diffs in chat and explains them. More topics will be added to the same skill.

## [0.10.0] - 2026-11-23

### Added

- Configuration file (`--config`, `TALK2TIA_CONFIG` or `%APPDATA%\Talk2Tia\config.json`) to choose toolsets, add or remove
  single tools (`allow`/`deny` with wildcards) and enable a `readonly` profile (`--read-only`, `TALK2TIA_READONLY=1`) that
  exposes only tools that do not change the project or the PLC. `talk2tia --doctor` checks the configuration.
- `diagnostics` toolset: CPU operating mode, diagnostic buffer, alarms, variable read and browse, watch tables with live
  values through the CPU Web API, and data block snapshots through Openness.
- `diagnostics_write` toolset: write variables, change the operating mode, acknowledge alarms and call any Web API method,
  always with `confirm=true`.
- Support the project on Ko-fi: `Sponsor` button on the repository (`.github/FUNDING.yml`), support section,
  navbar and footer links on the website, badge in the README.
- Website scroll animations: fade-up reveals, animated chat demo, count-up statistics, ambient motion
  (disabled with *reduce motion*).
- Website cookie consent banner (vanilla-cookieconsent) with accept/reject/per-category choices, a cookie policy page
  in English and Italian and a "Cookie preferences" footer link. Google Analytics 4 is loaded only after consent.
- Website SEO: `robots.txt`, social preview image with Open Graph and Twitter card tags, schema.org structured data
  (`SoftwareApplication`, `Organization`, `WebSite`), keyword-focused titles and descriptions.

### Changed

- The server instructions and the write tool descriptions tell the assistant to apply code changes itself in the open
  project (read, edit, write back) instead of listing manual edits for the user.
- Website fonts are self-hosted instead of loaded from Google Fonts (faster first paint, no visitor IP sent to Google).
- Each website page has a single `<h1>`; the 404 page is excluded from the sitemap.

- Simplified the branching model to GitHub flow: work happens on feature branches merged into `main`, releases
  are `vX.Y.Z` tags. CI runs on `main` and on pull requests.

### Fixed

- Website no longer scrolls horizontally on phones: compact navbar under 400 px, side slide-in animations replaced by
  fade-ups on small screens, long code, tables and grid cards kept within the viewport.
- Documentation pages on phones: text keeps the side margin instead of touching the screen edges, and tables are shown
  as labelled cards instead of squeezed columns.
- Website and README download links now open the releases page: `/releases/latest` ignores pre-releases and
  returned a 404 while only beta releases exist.

## [0.9.0] - 2026-11-20

First public beta.

### Added

- MCP server for Siemens TIA Portal V21 through the Openness API, with 107 tools in 7 toolsets
  (`portal`, `hardware`, `plc`, `online`, `library`, `hmi`, `object`), selectable with `TALK2TIA_TOOLSETS`.
- PLC code editing: SCL/DB/UDT as source text, LAD/FBD through SIMATIC SD documents, any language via SimaticML.
- Library management: global and project libraries, types and versions, edit/release/discard, master copies,
  update project/library, harmonize, clean-up, update check.
- Tags, constants and PLC data types in bulk; external sources; cross references.
- Compile with located errors and warnings, online/offline compare, connection configuration and download with
  conservative defaults (no CPU stop or data block re-initialization without explicit permission).
- Hardware tree, catalog search, devices, modules, subnets and nodes; WinCC screens and tags.
- Generic object-model tools to browse, read, write and invoke any Openness object.
- Confirmation guards for every destructive operation.
- `talk2tia --doctor` environment check.
- Signed MSI installer and portable zip.
- Website with documentation in English and Italian.

[Unreleased]: https://github.com/insideapp-srl/talk2tia/compare/v0.12.0...HEAD
[0.12.0]: https://github.com/insideapp-srl/talk2tia/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/insideapp-srl/talk2tia/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/insideapp-srl/talk2tia/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/insideapp-srl/talk2tia/releases/tag/v0.9.0
