# docs/

Design, specification, and reference material for ChatApp. Code-adjacent notes that would get stale inside a commit message but are load-bearing for architectural decisions live here.

See the [repo-root README](../README.md) for the stack overview.

---

## Reading order

1. [`specs/00-product-spec.md`](specs/00-product-spec.md) — **start here.** Scope, user-facing features, non-goals.
2. [`specs/01-architecture.md`](specs/01-architecture.md) — **authoritative technical spec.** Runtime model, bounded contexts, decisions-vs-spec table, deliberate deviations (PBKDF2 not Argon2; ClamAV deferred behind `IAttachmentScanner`; REST-write / SignalR-broadcast split).
3. [`specs/features/`](specs/features/) — per-feature slices (foundation → identity → messaging → attachments → hardening). Numbered in implementation order.
4. [`design/01-layout-design.md`](design/01-layout-design.md) — UI layout and information architecture.
5. [`implementation-plan.md`](implementation-plan.md) — end-to-end delivery plan aligned with the numbered feature specs.

**Before making structural changes**, read `specs/01-architecture.md`. It encodes the reasons behind several non-obvious choices; don't unwind them without updating the spec.

---

## Layout

```
docs/
├── specs/
│   ├── 00-product-spec.md        # Scope and non-goals
│   ├── 01-architecture.md        # Runtime model, bounded contexts, decisions
│   └── features/                 # Numbered per-feature specs (01-foundation → 18-additional-changes)
├── design/
│   └── 01-layout-design.md       # UI / IA design notes
├── requirements/
│   └── requirement.md            # Source requirements
├── bugs/                         # Bug reports and post-mortems (currently empty)
└── implementation-plan.md        # Delivery plan
```

---

## Feature specs

Eighteen numbered slices in [`specs/features/`](specs/features/) cover the full scope in implementation order:

| # | Slice                                              |
|---|----------------------------------------------------|
| 01 | Foundation (projects, DB, auth scaffolding)       |
| 02 | Identity (register, login, sessions)              |
| 03 | Profile and sessions UI                           |
| 04 | Friends and personal chats                        |
| 05 | Realtime backbone (hubs, groups)                  |
| 06 | DM messaging                                      |
| 07 | Presence states                                   |
| 08 | Rooms basics                                      |
| 09 | Room messaging                                    |
| 10 | Unread markers and counts                         |
| 11 | Edit / delete / reply                             |
| 12 | Attachments                                       |
| 13 | Room invitations                                  |
| 14 | Room moderation                                   |
| 15 | User bans (social)                                |
| 16 | Pagination and virtual scroll                     |
| 17 | Hardening — rate limits, CSRF, headers            |
| 18 | Additional changes                                |

Each slice is self-contained (data model deltas, API shape, UI behaviour, tests).

---

## Conventions

- **Markdown only.** No binary diagrams checked in — use Mermaid blocks inside markdown if you need visuals, so they render in GitHub.
- **Absolute paths in links** when crossing folders: `../server/README.md`, not `../../server/README.md`.
- **Keep specs in sync with code.** If you deliberately diverge, update the "decisions vs spec" table in `specs/01-architecture.md`; don't silently drift.
- **Bug reports** in `bugs/` follow one file per issue: short title, repro steps, root cause, fix commit.
