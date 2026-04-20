# Slice 15 — Pagination + Virtual Scroll

## Context

Today both DM and room chat views call `GET /api/chats/{scope}/{id}/messages`, which returns the **first** 50 messages ordered ascending by `(CreatedAt, Id)` with no way to fetch older history (`MessageService.cs:83-132,196-246`). The client renders the entire array into the DOM (`message-list.component.ts:23-44`) and there is no virtualisation.

This slice implements the keyset pagination contract specified in `01-architecture.md` §Non-functional notes — `WHERE (created_at, id) < (@c, @i) ORDER BY created_at DESC, id DESC LIMIT 50` — and replaces the chat list with a CDK virtual scroll viewport so message lists with thousands of rows scroll smoothly. The composite indexes `(PersonalChatId|RoomId, CreatedAt, Id)` already exist (`MessageConfiguration.cs:15-18`), so no migration is required.

## Server changes

**Endpoint contract (both controllers):**

```
GET /api/chats/personal/{chatId}/messages?beforeCreatedAt={iso8601}&beforeId={guid}&limit={1..100}
GET /api/chats/room/{roomId}/messages?beforeCreatedAt={iso8601}&beforeId={guid}&limit={1..100}
```

- `beforeCreatedAt` + `beforeId` are optional; both must be supplied together (return 400 if only one is present).
- `limit` optional, default 50, clamp to `[1, 100]`.
- Default call (no cursor) returns the **most recent** page.
- Response shape unchanged — still `MessageResponse[]` — but ordered **ascending by (CreatedAt, Id)** so the client can append directly without re-sorting (server fetches `DESC` then reverses before returning).
- The oldest item in the response is the cursor for the next call: client passes its `createdAt` + `id` as `beforeCreatedAt` / `beforeId`. When the returned page has `< limit` items, history is exhausted.

**Files to modify:**

- `server/ChatApp.Api/Controllers/PersonalMessagesController.cs` — accept the three query params, pass to service.
- `server/ChatApp.Api/Controllers/RoomMessagesController.cs` — same.
- `server/ChatApp.Domain/Services/MessageService.cs` — add an optional `MessageHistoryCursor` (record with `DateTimeOffset CreatedAt`, `Guid Id`) + `int limit` to both `LoadPersonalHistoryAsync` and `LoadRoomHistoryAsync`. Replace the current ascending `Take(HistoryLimit)` with:
  ```csharp
  var query = _db.Messages.Where(scopePredicate).Where(m => m.DeletedAt == null);
  if (cursor is not null)
      query = query.Where(m => m.CreatedAt < cursor.CreatedAt
          || (m.CreatedAt == cursor.CreatedAt && m.Id.CompareTo(cursor.Id) < 0));
  var page = await query
      .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
      .Take(limit).ToListAsync();
  page.Reverse();
  ```
  Keep the existing reply-to / attachments hydration; just operate on `page`.
- Drop the `HistoryLimit = 50` constant in favour of a `DefaultPageSize = 50`, `MaxPageSize = 100`.
- Add a small DTO `MessagePageRequest` (or just bind from `[FromQuery]`) — controller validates that `beforeCreatedAt` and `beforeId` are both-or-neither.

No DB migration. No SignalR change.

## Client changes

**Add dependency:** `@angular/cdk@^19` in `client/package.json`. Import `ScrollingModule` where used.

**Service layer** (`client/src/app/core/messaging/`):

- Extend `DmService.loadHistory(chatId)` and `RoomMessagingService.loadHistory(roomId)` to accept an optional cursor `{ createdAt: string; id: string }` and return the fetched page (don't unconditionally overwrite the signal).
- Add `loadOlder(chatId|roomId)` that:
  1. Reads the current oldest message from the signal.
  2. Calls the GET with `beforeCreatedAt` + `beforeId`.
  3. **Prepends** the returned page to `_messages` (preserving the existing array — important for virtual scroll anchor stability).
  4. Tracks `hasMoreHistory` and `isLoadingOlder` signals; sets `hasMoreHistory = false` when the returned page is shorter than the requested limit.
- Initial `loadHistory` resets `hasMoreHistory = true` and replaces the signal with the most-recent page.

**View layer:**

- Replace the plain `<div>` + `@for` in `client/src/app/shared/messages/message-list.component.ts` with a `<cdk-virtual-scroll-viewport>` of fixed-ish row size (use `autoSize` strategy from `@angular/cdk/experimental/scrolling` only if rows vary a lot; otherwise `itemSize` ≈ 72 is fine for the MVP).
- Track scroll position: when the viewport's `scrolledIndexChange` reports the first visible index ≤ a small threshold (e.g. 5), call the parent's `loadOlder()` callback (emit via `@Output() loadOlder = new EventEmitter<void>()`).
- Auto-scroll-to-bottom logic must change: only scroll to bottom when (a) initial load completes, (b) user is already near the bottom and a new live message arrives. Do **not** force-scroll when older history is prepended — capture `viewport.measureScrollOffset('bottom')` before prepend and re-apply after.
- Wire the `loadOlder` output in `dms.component.ts` and `room-detail.component.ts` to the corresponding service.

**Files to modify / add:**

- `client/package.json` (+ `package-lock.json`) — add `@angular/cdk`.
- `client/src/app/core/messaging/dm.service.ts`
- `client/src/app/core/messaging/room-messaging.service.ts`
- `client/src/app/core/messaging/messaging.models.ts` — add `MessagePage` / cursor types if helpful.
- `client/src/app/shared/messages/message-list.component.ts` (+ template/scss) — virtual scroll, scroll anchor preservation, `loadOlder` output.
- `client/src/app/features/dms/dms.component.ts`
- `client/src/app/features/rooms/room-detail.component.ts`

## Out of scope

- No backend tests in this slice (deferred to slice 17, per user direction).
- No "jump to message" / deep linking — separate concern.
- No change to SignalR live-append behaviour beyond the auto-scroll heuristic above.

## Verification (manual)

1. Seed a personal chat with > 200 messages (ad-hoc script or REST loop).
2. Open the DM in the SPA; the latest 50 render and the view starts at the bottom.
3. Scroll up — older pages load on demand; scroll position stays anchored on the message that was at the top.
4. After all history is exhausted, scrolling further does **not** issue more requests.
5. Send a new message from a second browser → it appears at the bottom and the view auto-scrolls only if you were already near the bottom.
6. Repeat (2)–(5) for a room chat.
7. `GET /api/chats/personal/{id}/messages?beforeCreatedAt=2099-01-01T00:00:00Z&beforeId=00000000-0000-0000-0000-000000000000&limit=10` returns the 10 most recent messages (cursor in the far future ⇒ no filtering effect beyond ordering); supplying only `beforeCreatedAt` returns 400.
