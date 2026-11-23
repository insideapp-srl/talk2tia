# Showing the changes

Talk2Tia computes the diffs itself, from the code before and after the change in TIA Portal, and draws LAD networks as
text. Your job is to show them faithfully and explain them. Never draw networks or invent diffs yourself: use what the
tools return.

## After every change

The write tools (`plc_write_scl`, `plc_write_document`, `plc_write_xml`, `plc_write_type`) return a `diff` field in
Markdown (`showDiff` is on by default).

1. Show the `diff` Markdown to the user as it is, keeping every fenced block (```` ```diff ````, ```` ```text ````) intact.
   Do not reflow, shorten or translate the content of the code blocks: the drawings depend on exact spacing.
2. Below it, summarize in the user's language: what changed in each block and network, and why.
3. Run `plc_compile` and report errors and warnings.
4. If `diff` says "no changes", say so: the code in TIA Portal is the same as before.

When the diff is very long (more than about 150 lines), show the summary and the most relevant networks, then offer the
rest. `plc_render_block` with `networks: [n]` draws single networks on request.

## Before a change (review mode)

When the user wants to review changes first, or the change is large or risky (many networks, interface changes,
deleted logic):

1. Prepare the full new content (SCL source or SIMATIC SD document).
2. Call `plc_diff_block` with it: nothing is written.
3. Show the returned diff and ask for confirmation.
4. On confirmation, write it with `plc_write_scl` / `plc_write_document` (`overwrite=true`) and show the final diff.

## Showing a block

To show a LAD/FBD block or some of its networks, call `plc_render_block` and show the `drawing` as it is.

## Reading the LAD drawings

```text
┃ / ┠       left power rail (┠ = a rung starts here)
┤ ├  ┤/├    normally open / normally closed contact
┤P├  ┤N├    positive / negative edge (edge memory bit below)
┤==├ ┤<>├   comparator: first operand above, data type and second operand below
( )  (/)    coil / negated coil
(S)  (R)    set / reset coil
┌──┐        box (call, timer, MOVE...): inputs on the left, outputs on the right, EN/ENO (or IN/Q) on the wire
┬ ├ └ ┘     parallel branches (OR) and forks to several outputs
[w4]        connection point feeding a box pin (──▶ [w4] marks where it comes from)
(1)▶        continuation of a wide rung on the next row
+ / -       element added (in "After") / removed (in "Before")
```

In "changed" networks the ```` ```diff ```` list above the drawings names the added (+) and removed (-) elements.
"Same elements, different connections" means that only the wiring changed: compare the two drawings.

## Optional HTML report

Only when the user asks for a report or a page: build a single HTML page from the same diff (monospace `<pre>` blocks,
green for added and red for removed lines, before/after drawings side by side). In Claude Desktop or claude.ai show it as
an artifact; in Claude Code save it as a file and give the path. The Markdown in chat stays the primary view.
