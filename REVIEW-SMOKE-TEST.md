# Review smoke test

Throwaway file used to check that the Claude PR review workflow actually posts a
review. It touches nothing the build or tests depend on.

The workflow is skipped on any PR that edits `.github/workflows/`, because
`claude-code-action` requires the workflow file to match the copy on the default
branch. That guard is why a PR changing the review workflow never gets reviewed
by it — the review has to be exercised from a PR like this one instead.

Delete this file once the check is confirmed.
