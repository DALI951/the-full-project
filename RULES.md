# RULES.md — MinAge

> Project rules. Binding for every session, on every machine (injected automatically).
> Rules only — current state lives in `HANDOFF.md`.
> Seeded 2026-10-11 — lines marked `[ ] ASK DALI` are not answers yet.

## What this project is
[ ] ASK DALI — one or two lines on what this is and who uses it

## Stack
Unity 2022.3.62f2 (C#)


## Repo
- Root: `F:/MinAge`
- Remote: `https://github.com/DALI951/the-full-project.git`  ← check visibility before putting anything private in here
- Branch: `main`

## Commands (exact)
| Purpose | Command | Working dir |
|---|---|---|
| build | via Unity editor / `.opencode` MCP tools | `F:/MinAge` |

## Conventions
- [ ] ASK DALI — naming, file layout, formatting, UI language (AR/EN?), branch habits
- Unity: keep every generated artefact out of git unless the project already tracks it

## Do NOT
- Never commit secrets, tokens or passwords (this may be a public repo).
- Never force-push a shared branch. Never destroy uncommitted work without asking.
- Never "fix" a failing test by weakening the test.
- [ ] ASK DALI — what has broken here before / what must never happen again

## Decisions (why — append-only)
- (none recorded yet)

## Open questions for Dali
- [ ] What are the exact build/test/deploy commands you actually use?
- [ ] What conventions must always be followed in this repo?
- [ ] What must never happen here (past pain points)?
- [ ] Anything about deployment / hosting / credentials *location* (not the secrets themselves)?
