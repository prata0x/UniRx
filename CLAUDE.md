# CLAUDE.md

UniRx (Reactive Extensions for Unity), maintained as a personal fork after
upstream (`neuecc/UniRx`) was publicly archived. The author has moved on to
`Cysharp/R3`, which is not a drop-in successor, so this fork exists to keep
UniRx usable for projects that still depend on it.

@.claude/vendor/universal.md
@.claude/vendor/product-repo.md

## Fork maintenance policy

- Never open new issues or PRs against upstream (`neuecc/UniRx`) — it is
  archived and unmaintained. Port useful unmerged upstream PRs or unresolved
  upstream issues directly into this fork instead (cherry-pick or manual
  port, adjusted for how far this fork has diverged).
- Record bugs found and fixes made in this fork's own GitHub Issues, not in
  upstream's tracker.
- **This is not an official handoff or successor project.** It is an
  unofficial personal fork with no endorsement or delegation from the
  upstream author, and it makes no support or compatibility guarantees.
  Avoid wording in release notes, README, or issue/PR text that implies an
  official role or a support channel (e.g. "taking over", "successor",
  "accepting requests") — say what changed instead.

## Issue tracking

- This fork's own GitHub Issues are the backlog (`gh issue list`), not
  upstream's.
- Label `upstream-port` groups issues created to track a ported (or
  port-candidate) upstream PR. Combine with `bug` / `enhancement` as fits
  the change. List with `gh issue list --label upstream-port`.
- Before filing a new issue, check `gh issue list` for an existing one
  covering the same ground.
- When triaging a batch of upstream PRs or an audit's findings into
  issues: file only items that need action (adopt as-is, adopt with
  changes) — skip/close-recommended items get a comment explaining why,
  not a new issue. One issue per item, not one per category.
