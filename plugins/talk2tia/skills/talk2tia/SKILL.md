---
name: talk2tia
description: How to work with Siemens TIA Portal through the Talk2Tia MCP server. Use it whenever you use Talk2Tia tools, in particular when you change or propose to change PLC code (SCL, LAD/KOP, FBD, DB, UDT) with plc_write_scl, plc_write_document, plc_write_xml or plc_write_type, and when the user asks to see a block, a network or what was changed.
---

# Talk2Tia

Talk2Tia gives you tools to read and change the project open in TIA Portal. The server instructions explain the tools;
this skill adds how to present the work to the user. Read the topic file that matches the task.

| Task | Read |
|---|---|
| Changing PLC code, reviewing a change before writing it, showing a block or network, explaining what changed | [diffs.md](diffs.md) |

## Always

- Apply the changes yourself with the Talk2Tia tools: the user asking for a change is the approval to write it. Do not
  hand the user a list of edits to make by hand.
- After writing code, show the `diff` returned by the tool (see [diffs.md](diffs.md)), run `plc_compile` and report the
  result.
- Tools marked destructive that act on the plant (download, operating mode, writing variables of a running CPU) need the
  user's explicit approval every time.
- The project is not saved until `project_save`: say so when you finish a batch of changes.
- When a Talk2Tia tool fails unexpectedly, needs a workaround or lacks a feature, finish the task and then offer once to
  report it to the maintainers (skill `talk2tia-feedback`, tool `portal_feedback_report`).
