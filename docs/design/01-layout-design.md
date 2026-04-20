# ChatApp UI Redesign + Dark/Light Mode

## Context

The current Angular 19 client is functional but visually unpolished: per-component SCSS with ad-hoc hardcoded colors, no design tokens, no icon library, no UI framework, and a flat top-nav layout that doesn't match the 3-column wireframe in `docs/requirements/requirement.md` §A.3. There's also no dark mode support. We want a full-surface overhaul to a **modern minimal (Linear/Vercel-style)** aesthetic, a proper 3-column chat shell, a design-token foundation that both themes consume, and a user-facing theme toggle.

### Decisions (already agreed)

- **Visual style**: Modern minimal — generous whitespace, restrained palette, single accent, subtle borders, no gradients/glassmorphism.
- **Style stack**: CSS custom properties + SCSS. Themes via `[data-theme="light"|"dark"]` on `<html>`. No new UI framework.
- **Layout**: Sidebar left (rooms + contacts), main chat center, members/context right — matches ASCII wireframe A.3.
- **Icons**: Lucide, via `lucide-angular`.
- **Typography**: Inter via Google Fonts.
- **Default theme**: Follow `prefers-color-scheme`, then persisted override.
- **Scope**: Full overhaul — every screen.

---

## 1. Design system foundation

### 1.1 Tokens — `client/src/styles.scss` (currently near-empty)

Define all tokens as CSS custom properties on `:root`, override under `[data-theme="dark"]`. Group:

- **Color — semantic, not literal**: `--color-bg`, `--color-bg-elevated`, `--color-bg-sunken`, `--color-surface`, `--color-surface-hover`, `--color-border`, `--color-border-strong`, `--color-text`, `--color-text-muted`, `--color-text-subtle`, `--color-accent`, `--color-accent-hover`, `--color-accent-fg`, `--color-danger`, `--color-success`, `--color-warning`, `--color-focus-ring`.
- **Presence**: `--presence-online` (green), `--presence-afk` (amber), `--presence-offline` (gray).
- **Radius**: `--radius-sm: 6px`, `--radius-md: 10px`, `--radius-lg: 14px`, `--radius-pill: 999px`.
- **Spacing scale**: `--space-1: 4px` … `--space-8: 48px` (4px base).
- **Typography**: `--font-sans: 'Inter', system-ui, …`, `--font-mono`, sizes `--text-xs`…`--text-2xl`, line-heights, weights 400/500/600/700.
- **Shadow**: `--shadow-sm`, `--shadow-md`, `--shadow-dialog`. Dark theme uses stronger shadows, light uses softer.
- **Transition**: `--transition-fast: 120ms ease`, `--transition-med: 180ms ease`.
- **Z-index scale**: `--z-header`, `--z-sidebar`, `--z-dialog`, `--z-toast`.

Light palette: off-white bg (`#fafafa`), white surfaces, near-black text (`#0a0a0a`), accent `#2563eb`.
Dark palette: near-black bg (`#0a0a0b`), `#141416` surfaces, `#ededed` text, accent `#3b82f6`. Borders use low-alpha neutrals so they adapt.

### 1.2 Global reset, base, utilities

- Modern reset (box-sizing, margin reset, image/media sensible defaults).
- `html, body { background: var(--color-bg); color: var(--color-text); font-family: var(--font-sans); }`.
- Focus-visible ring using `--color-focus-ring`.
- Scrollbar styling that respects theme.
- Smooth theme swap: `html { transition: background-color var(--transition-med), color var(--transition-med); }`.

### 1.3 Typography

- Load Inter via `<link>` in `client/src/index.html` (`wght@400;500;600;700`), plus `display=swap`.
- Import in `styles.scss` not needed (already in index.html). Update `angular.json` styles list only if any font file is local.

### 1.4 Icons

- Add dep: `npm i lucide-angular`.
- In components: `import { LucideAngularModule, Search, Send, … } from 'lucide-angular';` and use `<lucide-icon [img]="SearchIcon" />`.
- Replace all text/emoji icons in templates (hamburger, send, attach, emoji, search, chevrons, close, more-horizontal, bell, etc.).

---

## 2. Dark/light mode

### 2.1 `ThemeService` — `client/src/app/core/theme/theme.service.ts` (new)

- Signal-based: `theme = signal<'light'|'dark'>(…)`, `preference = signal<'system'|'light'|'dark'>(…)`.
- On construct:
  1. Read `localStorage['chatapp.theme']` (values: `system|light|dark`, default `system`).
  2. If `system`, resolve against `window.matchMedia('(prefers-color-scheme: dark)')`.
  3. Apply `document.documentElement.setAttribute('data-theme', resolved)`.
- Expose `setPreference(pref)` which persists and re-resolves.
- Subscribe to `matchMedia.addEventListener('change', …)` to update when pref === `system`.
- Expose `toggle()` as a convenience (simple light/dark swap).

### 2.2 FOUC prevention

Inline a tiny script in `client/src/index.html` `<head>` **before** Angular boots:

```html
<script>
  (function(){
    try {
      var p = localStorage.getItem('chatapp.theme') || 'system';
      var dark = p === 'dark' || (p === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
      document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
    } catch(e){ document.documentElement.setAttribute('data-theme','light'); }
  })();
</script>
```

### 2.3 Toggle UI

- Place a theme toggle button in the app-shell top bar (sun/moon Lucide icon), and a proper tri-state selector (System / Light / Dark) in the Profile → Preferences section at `features/profile/profile.component.html` (alongside the existing sound toggle).

---

## 3. App shell redesign — `features/app-shell/`

Replace flat top-nav-only shell with a 3-zone chrome:

```
┌──────────────────────────────────────────────────────────────┐
│ TOP BAR: logo │ global search │ … │ theme │ profile menu      │
├────────────┬─────────────────────────────────┬───────────────┤
│  SIDEBAR   │        MAIN OUTLET              │  CONTEXT      │
│  (rooms +  │  (chat, rooms list, profile…)   │  (members,    │
│   contacts)│                                 │   room info)  │
└────────────┴─────────────────────────────────┴───────────────┘
```

### 3.1 Top bar (slim, 52px)

- Logo (left), centered global search (cmd+K style, stub wires to future search), right cluster: theme toggle, notification bell (stub), avatar dropdown (Profile, Sessions, Sign out).
- Remove the current horizontal route-link strip — those move into the sidebar.

### 3.2 Left sidebar (collapsible, 280px → 64px icon rail)

- Search field at top.
- **Rooms** section: accordion with *Public* and *Private* groups. Each row: room icon (`#` public, `lock` private), name, unread badge (right-aligned, pill). Active room highlighted.
- **Contacts** section: friend list with presence dot (online/afk/offline), unread badge for DMs.
- Sticky bottom: `[+ New room]` button (primary), inline-add contact trigger.
- When no room is open, sidebar is its full expanded self; when a room is open, sidebar **stays visible** but the current room gets strong active styling (§4.1.1 "accordion compact" goal is met by letting users collapse the non-current section).
- Mobile (<960px): sidebar becomes a drawer, toggled via hamburger in top bar.

### 3.3 Right context panel (260px)

- **In a room**: Room info card (name, visibility badge, owner), Admins list, Members list with presence dots grouped by status, `[Invite user]` / `[Manage room]` buttons.
- **In a DM**: peer profile, mutual rooms, ban/unban action.
- **Elsewhere** (profile/sessions/rooms-catalog): hidden or replaced with contextual help.
- Toggleable via an icon button in top bar; collapsed state hides it entirely for max chat width.

### 3.4 Files affected

- `features/app-shell/app-shell.component.ts|html|scss` — rewrite template to three-zone grid using CSS grid (`grid-template-columns: auto 1fr auto`).
- New child components (colocated under `features/app-shell/`):
  - `top-bar.component`, `side-nav.component`, `context-panel.component`, `theme-toggle.component`.
- Wire right-panel content via a `ContextPanelService` (signal-based) that feature components populate on activation — or a routed named outlet. Lean toward the service for simplicity.

---

## 4. Reusable UI primitives

Introduce tiny, unstyled-as-possible wrappers to stop duplicating styles. Place under `client/src/app/shared/ui/`.

- `ui-button` — variants: `primary | secondary | ghost | danger`; sizes `sm|md`.
- `ui-input`, `ui-textarea` — consistent border, focus ring, error state.
- `ui-card` — surface container with border + radius.
- `ui-dialog` — modal wrapper (backdrop, focus trap, ESC to close), used by manage-room, confirmations, create-room, friend-request.
- `ui-tabs` — used by manage-room.
- `ui-badge` — unread counters and pills (public/private, role: owner/admin).
- `ui-avatar` — circular, with presence ring; falls back to initials.
- `ui-presence-dot` — small colored dot (online/afk/offline).
- `ui-menu` / `ui-dropdown` — profile menu, row actions.
- `ui-toast` — integrate with existing `core/notifications/toast.service.ts`.
- `ui-empty-state` — icon + heading + subtext + CTA.
- `ui-skeleton` — loading placeholders for lists.

All primitives are standalone components, read tokens from CSS vars, support both themes automatically.

---

## 5. Per-screen redesign

Each screen consumes only tokens + UI primitives. Aim: every component's SCSS drops most literal colors in favor of `var(--…)`.

### 5.1 Auth — `features/auth/login`, `features/auth/register`, `forgot-password` (if present)

- Full-viewport split: left 40% brand panel (logo, one-line tagline, subtle background pattern using `--color-border` dots), right 60% centered auth card (max-width 400px).
- `ui-card`, `ui-input`, `ui-button`. Theme toggle stays only in top bar, not on auth screens.
- Brand panel is illustration-free so it works light & dark.
- Forgot-password: same shell, single email field, success state inline.

### 5.2 Main chat — `features/rooms/room-detail`, `features/dms`

- Header strip (sticky): room name with visibility badge, member count, pinned actions (leave, manage) in a `ui-menu`.
- Message list: messages grouped by author+time gap (<5 min) like Slack. Avatar on first message of each group, hanging indent thereafter. Reply-reference as a bordered quote block above the message body.
- Hover affordances: on message hover show small action bar (reply, edit, delete, react-future) top-right of bubble.
- "Edited" shown as muted italic tag inline.
- Infinite-scroll spinner at top; "jump to latest" floating button when scrolled up.
- Composer: `ui-textarea` with autogrow, left icons (emoji, attach), right Send button (disabled until non-empty). Reply-to chip sits above textarea with close X.
- Attachments: image previews with rounded corners, filename + caption line; non-image shows Lucide `file` icon + filename + size + download.

### 5.3 Rooms lists — `features/rooms/public-rooms`, `private-rooms`

- Catalog as responsive grid of `ui-card` tiles. Each tile: room name with `#`/`lock` icon, description (2-line clamp), member count with users icon, Join/Open button.
- Sticky search bar + "New room" CTA.
- Empty state via `ui-empty-state`.

### 5.4 Contacts — `features/contacts`

- Two sections: Incoming requests (with Accept/Decline buttons), Friends (with presence dot, DM / unfriend / ban in `ui-menu`).
- Add-friend input at top with username lookup.

### 5.5 Sessions — `features/sessions`

- Table of sessions: device/browser, IP, last active (relative), "This session" badge, `[Sign out]` button. Card per row on mobile.

### 5.6 Profile — `features/profile`

- Sectioned cards: Account (email, username read-only, display name, avatar upload), Preferences (sound, **theme: System/Light/Dark radio**), Security (change password), Danger zone (delete account — red outlined card, confirm dialog).

### 5.7 Manage room — `features/manage-room`

- `ui-dialog` with `ui-tabs`: Members / Admins / Banned / Invitations / Settings (matches §A.4 wireframe).
- Member rows: avatar, name, presence dot, role badge, row-action `ui-menu` (Make/Remove admin, Ban, Remove from room), all disabled per permission rules already in the service.
- Settings tab: name + description inputs, visibility radio, Save (primary), Delete room (danger, separated and requiring confirmation dialog).

### 5.8 Toasts

- Existing `toast.service.ts` backs a new `ui-toast` stacked container bottom-right, with success/error/info variants and auto-dismiss.

---

## 6. Critical files to modify / create

**Create**
- `client/src/app/core/theme/theme.service.ts`
- `client/src/app/shared/ui/` — one folder per primitive listed in §4.
- `client/src/app/features/app-shell/top-bar.component.{ts,html,scss}`
- `client/src/app/features/app-shell/side-nav.component.{ts,html,scss}`
- `client/src/app/features/app-shell/context-panel.component.{ts,html,scss}`
- `client/src/app/features/app-shell/theme-toggle.component.{ts,html,scss}`

**Rewrite (templates + SCSS, keep TS logic)**
- `client/src/styles.scss` — tokens, reset, base.
- `client/src/index.html` — Inter link, FOUC script.
- `client/src/app/features/app-shell/app-shell.component.{html,scss}`.
- Every feature component's `.html` + `.scss` under `client/src/app/features/**`:
  - `auth/login`, `auth/register` (+ forgot-password if exists)
  - `rooms/public-rooms`, `rooms/private-rooms`, `rooms/room-detail`
  - `dms/dms.component`
  - `contacts/*`
  - `sessions/*`
  - `profile/profile.component`
  - `manage-room/manage-room.component`

**Adjust**
- `client/package.json` — add `lucide-angular`.
- `client/src/app/core/notifications/toast.service.ts` — wire to new `ui-toast` container.
- `client/src/app/app.routes.ts` — only if we adopt the named-outlet approach for the context panel. Default path is the `ContextPanelService` signal.

**Reuse (no changes beyond consuming new primitives)**
- All services under `core/` (auth, messaging, presence, rooms, etc.) remain untouched — this is a pure presentation redesign.

---

## 7. Execution order (suggested batches)

1. **Foundation**: tokens in `styles.scss`, Inter, lucide dep, FOUC script, `ThemeService` + toggle. Verify toggle flips colors on a blank page.
2. **Primitives**: build `ui-button`, `ui-input`, `ui-card`, `ui-badge`, `ui-avatar`, `ui-presence-dot`, `ui-dialog`, `ui-tabs`, `ui-menu`, `ui-empty-state`, `ui-skeleton`, `ui-toast`.
3. **App shell**: rewrite to 3-column grid with top-bar + side-nav + context-panel. Keep current routes working.
4. **Chat surfaces**: room-detail, dms (highest-traffic screens).
5. **Rooms catalog + contacts + sessions**.
6. **Profile + manage-room dialog**.
7. **Auth screens**.
8. **Polish pass**: empty states, skeletons, transitions, responsive breakpoints (≤960px drawer sidebar, ≤720px hide context panel, composer sticky, font sizing).

---

## 8. Verification

- **Local dev**: `cd client && npm i && npm start`; open `http://localhost:4200`.
- **Theme**:
  - First load with OS set to dark → app renders dark with no flash.
  - Toggle in top bar flips theme; refresh preserves choice (localStorage).
  - Change OS theme while app is set to `System` → app updates live.
  - Profile → Preferences → Theme radio stays in sync with top-bar toggle.
- **Visual QA** each screen in both themes, 3 widths (1440, 1024, 400):
  - Auth (login/register/forgot) — centered card, no overflow.
  - Shell — 3 columns at ≥1200px, 2 at 960–1200 (context panel hides), drawer-sidebar at <960.
  - Room chat — message groups render, reply quote, attachment previews, composer autogrow, infinite scroll top spinner, jump-to-latest button.
  - DMs — same as room chat, banned state shows frozen-history banner.
  - Public/Private rooms catalog — grid reflows, search works, empty state shows when filter yields none.
  - Contacts — incoming requests, friend presence dots.
  - Sessions — current session badge, sign-out per-row works.
  - Profile — theme selector, avatar upload, danger zone dialog.
  - Manage room dialog — all five tabs, permission-gated actions disabled for non-admins.
  - Toasts — success/error variants, auto-dismiss, dark-mode legible.
- **Accessibility spot checks**:
  - Tab order sensible through top-bar → sidebar → main → context.
  - Focus ring visible on all interactive elements in both themes.
  - Contrast (WCAG AA) for body text and muted text in both themes — verify with DevTools contrast checker.
  - `prefers-reduced-motion` disables theme-swap transition.
- **Regression**:
  - `npm run build` succeeds.
  - SignalR presence events still update sidebar + members panel.
  - Any existing Playwright specs under `client/` still pass.
