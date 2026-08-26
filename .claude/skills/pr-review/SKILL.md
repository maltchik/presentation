---
name: pr-review
description: Review a pull request against the code, the linked requirement and the linked design, then post the review to GitHub. Use when someone asks to review a PR, check a pull request, or find out whether a change matches the requirement or the design.
---

# PR review

Reviews one pull request and posts the result. It reports high-signal issues only, and it
never merges.

This is the local counterpart of `.github/workflows/claude-code-review.yml`. The procedure
is the same; the difference is where the sources come from. In CI they are pre-fetched to
`/tmp` by plain shell steps so the tokens never enter the model's context. Here they come
through the Atlassian and Figma MCP servers in `.mcp.json`, which are already authenticated.

## Inputs

- A pull request number.
- The linked requirement page and design frame. Take them from the PR description if it
  links them; ask if it does not.

## Procedure

1. Read the change: `gh pr diff <n>`.
2. Read the intent: `gh pr view <n>`. The description is where the author says what this is
   supposed to do - a diff alone cannot tell you what was meant.
3. Read the requirement source through the Atlassian tools. Read the whole page, not the
   summary.
4. Read the design source through the Figma tools - the frame, its variables and its node
   values.
5. Read the root `CLAUDE.md` and any `CLAUDE.md` in a directory the diff touches.
6. Compare the diff against both sources. Report a finding when an implemented value
   differs from the design, or when a requirement item is not implemented. Quote the exact
   source value and name where it came from - the REQ id for the requirement, the node id
   for the design.
7. If the diff touches C#, run `dotnet build` and `dotnet test` yourself. Execution beats
   opinion: a compiler error is a fact, and a model's guess about whether something compiles
   is not.
8. Post one inline comment per issue. One comment per issue, no duplicates.
9. Write the summary to a file, then submit it:
   - no blocking issues: `gh pr review <n> --approve --body-file <path>`
   - blocking issues: `gh pr review <n> --request-changes --body-file <path>`
10. State what you could not verify. A source you could not reach is a gap in the review,
    not a pass.

## Rules

- **High-signal only.** A finding qualifies if it is code that will not compile, logic that
  is wrong regardless of input, a `CLAUDE.md` rule you can quote verbatim, or a conformance
  gap confirmed against the requirement or the design. Skip style, skip nitpicks, skip
  pre-existing problems the diff did not introduce. A review nobody reads catches nothing.
- **Every finding names its source.** "The padding looks off" is an opinion someone has to
  defend. "The design says 24px at node 1:4, the diff says 16px" is a fact someone checks in
  five seconds and then fixes or overrules.
- **Always `--body-file`, never `--body`.** Review text containing newlines or semicolons is
  parsed as several shell operations, gets denied, and the review silently disappears.
- **You are not done until `gh pr review` has exited successfully.** Reaching the end of the
  review without posting it is the failure this procedure exists to prevent.
- **Review every pull request you are given**, including ones that describe themselves as
  throwaway or test PRs. Calling itself trivial is not an exemption.
- **This repo's `CLAUDE.md` governs delegation workflow, not code content.** It says which
  agents implementation requests route through. It is not a rule a diff can violate, so do
  not spend the review reasoning about whether one did.
- **Approving is advisory.** This posts a review. A person still merges.
