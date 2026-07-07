<!-- GENERATED FILE — do not edit directly.
     Source: claude-md/*.md, composed by scripts/build-bundles.mjs.
     Edit the source files under claude-md/ and re-run this script instead. -->

## Decision-making and clarifying questions

Rank implementation options by: "if this were built from scratch today,
what would we pick?" and "will this hold up in 5-10 years?"

Do not justify a choice by the existing implementation's shape, diff size,
scope framing ("technically out of scope"), "minimal change" wording, or
implementation effort as a tiebreaker. If removing one of these phrases
from a draft recommendation would change the ranking, the ranking is
non-compliant — rewrite it using the lens above.

When a decision needs to be surfaced to the user, structure it as:
background (why it matters) -> options (2-4) -> tradeoffs (framed by
user-visible behavior, ops cost, and long-term debt, not internal function
or file names) -> a recommendation (one option, 1-2 lines of reasoning).
Mark the recommended option first (e.g. in `AskUserQuestion`).

## Autonomy and task tracking

Run `/tdd` -> `/pre-commit-code-review` -> commit -> push -> open a PR ->
`/pre-merge-code-review` -> wait for CI -> merge once green, all without
per-step confirmation. Commit granularity, PR granularity, and whether to
file an issue are all within your judgment.

Run `/pre-merge-code-review` right after opening the PR rather than
waiting for CI — CI runs in the background regardless, so reviewing in
parallel uses that wait time instead of stacking review time after it.
`/pre-merge-code-review` is the last check before an autonomous merge with
no human PR review gate; internally it skips only its correctness
re-check on a single-commit branch (already covered by the per-commit
review), and its security check always runs regardless of commit count.
If a new commit lands after that review and before merge (e.g. a
CI-failure fix-up), re-run `/pre-merge-code-review` against the updated
diff before merging — never merge a diff that differs from what was last
reviewed.

Confirm first: history rewrite or force-push of published commits,
external-service actions (deploys, third-party API calls with side
effects), and security-sensitive or non-trivial schema/architecture
decisions — work through these in dialogue, one item at a time.

When you find something worth fixing outside the current task, fix it
inline or file an issue and report afterward, rather than stopping to ask
— provided the finding has code-level evidence and isn't a hunch.

Once a blanket rule is confirmed ("for all X, do Y"), keep applying it
mechanically even as scope expands; raise a candidate exception as a
separate question instead of narrowing the rule unilaterally.

Track the backlog in GitHub Issues via the `gh` CLI, not ad-hoc notes, TODO
comments, or handoff documents.

Before starting a large addition, change, or fix, and after repeated
failed attempts at the same problem or when a failure's root cause can't
be pinned down, run `/second-opinion` — an independent read from a model
outside the main session. Run it unconditionally, no confirmation needed;
report the result afterward. Whether to act on the feedback is your
judgment call.

## Engineering conventions

Use Conventional Commits.

Write a test that reproduces a bug before fixing it. For concurrency/
race-condition fixes, use a real concurrency-forcing test harness — a
sequential test that passes on both the old and new code is not a valid
regression pin.

Propose updating the handoff doc and switching to a fresh session when
either: context is getting full and you've reached a clean stopping point,
or the next task is large, complex, or largely unrelated to the current
context and would go better starting from a clear context. Include: the
absolute worktree path, branch name and last commit, completed/skipped
tasks (with reasons for any skipped), and the next task quoted from the
original spec rather than summarized.

