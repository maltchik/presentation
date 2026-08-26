---
name: test-cases-from-ac
description: Generate test cases from a story's acceptance criteria, including the edge cases the criteria imply but never state. Use when QA needs a test plan, a verification checklist, or coverage for a ticket.
---

# Test cases from acceptance criteria

Gives QA a list to edit instead of a blank page. QA still owns the verdict.

## Inputs

- A Jira issue key with numbered acceptance criteria.
- The design page for the feature, if one exists.

## Procedure

1. Read the criteria and the linked design.
2. Write one test case per criterion: preconditions, steps, expected result. Tag it with
   the criterion id.
3. Then write the cases the criteria imply but do not state, each tagged with the criterion
   it derives from:
   - boundary values on every number in the criteria - limit, limit plus one, zero, empty
   - every state in the design that no criterion mentions
   - the reverse of each "then" - what must NOT happen
   - what happens on a second attempt
4. List any criterion you could not derive a case from. That is a gap in the story, not in
   the tests. Report it.
5. Write the cases into Jira as sub-items of the issue.
6. Report the coverage table: criterion id to case ids.

## Rules

- Every case traces to a criterion id. A case that traces to nothing is either a missing
  criterion or scope creep - say which one.
- Steps must be executable by someone who has not read the story.
- Do not mark anything passed or failed. This skill writes cases; people run them.
