---
name: story-from-transcript
description: Turn a client-call transcript in Confluence into a Jira story with numbered Given/When/Then acceptance criteria. Use when someone asks to write up a call, turn a meeting into a ticket, or draft requirements from a transcript.
---

# Story from transcript

Turns one meeting transcript into one Jira story. It does not turn a transcript into a
specification, and it does not fill gaps.

## Inputs

- A Confluence page holding the transcript and summary of a single call. The caller gives
  the page id or the title.
- The Jira project key to create the story in. Default `KAN`.

## Procedure

1. Read the Confluence page with the Atlassian tools. Read all of it - the decision is
   often in the last five minutes, not in the summary at the top.
2. Build three lists, and keep them separate:
   - **Decided** - the client stated it and nobody contradicted it.
   - **Discussed** - raised, then left open. Record the timestamp.
   - **Assumed** - things the feature needs that nobody in the call said.
3. Write the story description from the *Decided* list only. Use the client's own words
   wherever they are concrete.
4. Write one acceptance criterion per testable statement in *Decided*, numbered `AC-1`,
   `AC-2`, ... in Given/When/Then form. Each one must be checkable by one person in one
   sitting.
5. Where a criterion is real but under-specified, write the criterion and append the gap
   in plain text - for example `(character limit not specified)`. Never choose a value to
   make a criterion look finished.
6. Add an **Open questions** section from the *Discussed* and *Assumed* lists. Each entry
   names who can answer it.
7. Create the Jira issue and link it back to the Confluence page by URL.
8. Report the issue key, then read the open-questions list back to the caller.

## Rules

- Do not invent a requirement. An invented requirement is worse than a missing one,
  because nobody argues with it.
- Do not merge two separate client asks into one criterion to keep the count down.
- Quote a timestamp whenever you claim the call decided something.
- The story is a draft. The BA is the author and edits it before anyone estimates it.
