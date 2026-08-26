---
name: story-review
description: Check a Jira story for completeness, testability and feasibility against the codebase before it is estimated. Use when someone asks whether a story is ready, wants a feasibility check, or is about to take a ticket into planning.
---

# Story review

Reviews a story the way a tech lead does before planning: is it testable, is it ambiguous,
and can it be built here at all.

## Inputs

- A Jira issue key.
- The repository the work would land in. The current working directory unless told
  otherwise.

## Procedure

1. Read the issue and every page linked from it - especially the transcript it came from.
2. **Testability.** For each acceptance criterion, write down how you would verify it. If
   you cannot, it is not a criterion. Flag it.
3. **Ambiguity.** Find the terms that carry a decision nobody made - "fast", "works
   offline", "recent", any missing number or limit. If the story came from a transcript, go
   back to the transcript and check whether it was raised and dropped; cite the timestamp
   if it was.
4. **Feasibility.** Search the repository for what the story assumes already exists: the
   endpoint, the table, the component, the permission. Name what is missing, with paths.
5. **Sizing shape.** If the story needs work in a layer it never mentions - a backend task
   hiding inside a UI story - say so explicitly. This is usually the most valuable finding.
6. Post ONE comment on the issue: a verdict line, `READY` or
   `NOT READY - n blocking questions`, then the numbered questions, each answerable in a
   sentence.

## Rules

- Criticise the document, never the person. "AC-3 has no character limit", not "the BA
  forgot the limit".
- Blocking means blocking. Preferences go at the bottom under `Non-blocking`, or nowhere.
- Every feasibility claim names a file or a search you actually ran. Never guess about the
  codebase.
- Do not edit the story. Post the comment and stop.
