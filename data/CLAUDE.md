# CLAUDE.md — data/

Runtime state. **Gitignored content.** Do not commit anything under here — it is bind-mounted into the containers and recreated on first run.

## Layout

- `data/files/` — bind-mounted to the API container at `/var/chatapp/files`. Sub-tree:
  - `attachments/` — message attachments under `{yyyy}/{mm}/{uuid}{.ext}`. Thumbnails sit next to originals as `{uuid}{.ext}.thumb.jpg` (ImageSharp, 512 px longest side, JPEG q80).
  - `avatars/` — user avatars.
  - `room-logos/` — room logos.
- `data/pg/` — historically a bind for Postgres. The current `docker-compose.yml` uses a **named volume** (`pgdata`) instead, so this folder may be empty. Prefer the named volume; don't switch to a bind mount without coordinating (Windows Docker + Postgres + bind mounts is a known pain point).

## Do / don't

- **Don't** edit or rename files directly — the API owns the layout and paths are stored in the `attachments` table. Renaming on disk breaks downloads silently.
- **Don't** commit anything here. `.gitignore` covers it, but new subfolders can slip through — add a matching ignore if you add one.
- **To wipe runtime state locally**:
  ```bash
  docker compose -f infra/docker-compose.yml down -v    # drops pgdata + clamav-db named volumes
  rm -rf data/files/*                                    # drops uploaded files
  ```
  This is destructive — only run it against local dev.
- **Attachment reaper**: the API purges rows with `message_id = null` older than 1 hour and deletes the corresponding files. If you see orphaned files, check the background service logs before deleting by hand.

## Production note

On a deployed host, `data/files/` should live on a volume with enough headroom for indefinite message retention (no TTL on attachments except the unlinked-row reaper). Back it up alongside the Postgres volume — the DB references file paths and orphaned rows vs. orphaned files both cause user-visible breakage.
