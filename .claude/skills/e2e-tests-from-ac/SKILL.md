---
name: e2e-tests-from-ac
description: Turn a story's acceptance criteria into Playwright end-to-end specs, run them, and open a pull request. Use when someone asks for e2e tests, browser tests, or automated coverage of a feature.
---

# End-to-end tests from acceptance criteria

Writes Playwright specs against the running build, runs them, and reports what actually
failed.

## Inputs

- A Jira issue key with numbered acceptance criteria.
- A URL for the running build.

## Procedure

1. Read the criteria. If `test-cases-from-ac` has already run on this issue, read its
   output instead of re-deriving the edge cases.
2. Write one `test()` per criterion, named for the behaviour rather than the id, with the
   criterion id in a comment above it.
3. Prefer role- and label-based selectors. If an element cannot be selected accessibly,
   that is an accessibility finding - report it instead of reaching for a CSS path.
4. Run `scripts/run_e2e.sh <url>` and read the output.
5. For each failure, decide and state which it is: a bug in the build, a wrong assumption
   in the test, or a criterion the build does not implement yet. Do not weaken a test to
   make it pass.
6. Open a pull request with the specs and a summary listing every criterion, its test and
   its result.

## Rules

- Never add a fixed sleep. Wait on a condition.
- Never skip a test to get a green run. A failing test is the product.
- The specs are code and get reviewed like code. Do not merge your own pull request.
