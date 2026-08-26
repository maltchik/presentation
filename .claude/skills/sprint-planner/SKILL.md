---
name: sprint-planner
description: Propose a sprint order from a Jira backlog by dependency, risk and value, and say what will not fit. Use when someone is planning a sprint, prioritising a backlog, or asking what to pull in next.
---

# Sprint planner

Proposes an order and finds the dependencies. It does not estimate and it does not commit
anything.

## Inputs

- A Jira project key or board.
- The team's capacity in points for the sprint.
- Recent completed-points history, if it is available.

## Procedure

1. Read the candidate issues including their descriptions, not only the titles.
   Dependencies live in prose at least as often as in link fields.
2. Build the dependency graph. Include the implicit ones: two issues that clearly touch the
   same component, and any issue whose prerequisite is not in the candidate set.
3. Order by hard dependencies first, then risk - the thing most likely to surprise you goes
   early - then value.
4. Fill to capacity using the team's own estimates. Stop at capacity. Do not round up to
   fill the sprint.
5. Write the proposal: the ordered list with points, a committed-versus-capacity line, and
   a **Not in this sprint** section with a reason for each issue left out.
6. Post it as a comment for the product owner. Do not move issues into the sprint.

## Rules

- Do not estimate. An issue with no estimate goes under `Needs estimate` and stays out of
  the total.
- Every ordering decision that is not obvious gets one sentence of reasoning.
- Flag any issue whose blocker sits outside the sprint. That is the finding that saves the
  sprint.
- The product owner decides. This is a proposal that arrives with the reading already done.
