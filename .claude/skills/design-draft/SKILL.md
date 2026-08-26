---
name: design-draft
description: Draft the design states a story's acceptance criteria imply as Figma frames, using the tokens already in the file. Use when someone asks for a first-pass design, wireframes, or the states a feature needs.
---

# Design draft

Produces a first draft for a designer to edit. It is not a deliverable and it does not
replace design work.

## Inputs

- A Jira issue key, or the acceptance criteria directly.
- The Figma file to draft into.

## Procedure

1. Read the acceptance criteria.
2. Derive the states the criteria imply. Most features need more than the happy path -
   empty, filled, invalid, in flight, done, plus whatever the error criteria describe.
   Write the list out and map each state to the criterion that forces it.
3. Read the target file's existing variables, styles and components before drawing
   anything.
4. Draft one frame per state on a new page named `<feature> / v1`, using only tokens that
   already exist in the file.
5. Label each frame with the criterion id it satisfies.
6. Report the page name and the state-to-criterion mapping.

## Rules

- Never introduce a new colour, spacing value or type style. If a token is missing, say so
  and stop - a missing token is a design decision, not a gap to fill.
- Write access on the remote Figma server is beta. Draft into a scratch page, never into a
  library file.
- If the criteria imply a state with no precedent in the file, flag it rather than
  inventing one.
- The designer owns the file. Do not modify frames you did not create.
