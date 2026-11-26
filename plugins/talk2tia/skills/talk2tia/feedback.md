# Talk2Tia feedback

Talk2Tia improves through the problems its users hit. Your job: notice them, offer to report them, write a report the
maintainers can act on, and let the user decide whether and how to send it.

## When to offer a report

Offer it **after** finishing (or giving up on) the user's task, in one short sentence, and only once per problem:

- a Talk2Tia tool returned an error that looks like a bug (crash, "not found" for an object that exists, an exception
  from TIA Portal, wrong output);
- you needed a workaround for something a dedicated tool should do (generic `obj_*` tools, another format, manual steps);
- a missing tool or option blocked the user or forced manual work in TIA Portal;
- the user complains about Talk2Tia or suggests an improvement.

Do not offer it for problems of the user's project (compile errors in their code, wrong addresses) or of TIA Portal
itself, unless Talk2Tia should have handled them better.

Example: *"Fatto. Nota: `plc_read_block` non trova i blocchi con `/` nel nome, ho usato `obj_get`. Vuoi che prepari una
segnalazione per gli sviluppatori di Talk2Tia?"*

## Writing the report

1. Collect, from the conversation and by asking only what is missing:
   - **kind**: `bug`, `improvement` or `question`;
   - **title**: tool + symptom, e.g. *"plc_read_block: names containing / are read as group paths"*;
   - **description**: what the user wanted, steps to reproduce, expected vs actual result, the workaround used, and for
     improvements the proposal and why it matters;
   - **tool** and **error** message, if any;
   - **client**: the MCP client you run in (e.g. Claude Code, Claude desktop app), if known.
2. **Remove project content** before anything else: code, block/tag/DB names, IP addresses, customer, plant and
   project names, file paths with user names. Replace them with neutral placeholders (`FB_Example`, `192.168.0.x`).
   Keep them only if the user explicitly says they can be shared.
3. Call `portal_feedback_report` (Talk2Tia tool). It adds the environment (Talk2Tia and TIA Portal versions, Windows,
   configuration) and returns the Markdown report, a prefilled GitHub issue link and an e-mail link.
   If the Talk2Tia server is not available, write the same report yourself with the sections *What happened / Proposal*,
   *Tool*, *Error*, *Environment*.
4. Show the title and the report to the user and ask them to confirm or edit it.

## Sending it

Ask the user which channel they prefer, then:

- **GitHub issue** (public, needs a GitHub account): if the GitHub CLI is available and the user agrees, run
  `gh issue create --repo insideapp-srl/talk2tia --title "<title>" --body-file <report.md>`; otherwise give the user the
  `githubIssueUrl` to open. Before creating it, search for duplicates
  (`gh issue list --repo insideapp-srl/talk2tia --search "<keywords>"`) and, if one exists, suggest adding a comment instead.
- **E-mail** to `support@insideapp.help` (private, for reports that cannot be public): use an e-mail tool if you have
  one and the user approves, otherwise give the `mailtoUrl` or the address with the report to paste.

Never send anything without the user's explicit approval, and tell them where it was sent (issue link or address).
