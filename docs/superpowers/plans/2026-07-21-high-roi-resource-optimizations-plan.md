# High-ROI resource optimizations implementation plan

**Goal:** Land the highest benefit/cost performance optimizations for Dopamine without rewriting audio or Prism navigation.

**Architecture:** Prefer local, reversible changes: SQLite pragmas, list load caps + DB search, progress event split, sidecar/cache TTL, parallel tag reads, visualization FPS, artwork size defaults.

**Status:** Implemented in-tree (2026-07-21). This document is the reference for behavior and verification.

---

## 1. SQLite WAL + connection strategy

**Files:** `Dopamine.Data/SQLiteConnectionFactory.cs`

- Each connection applies: `temp_store=MEMORY`, `cache_size=-8000`, `foreign_keys=ON`
- Once per process: `journal_mode=WAL`, `synchronous=NORMAL`, optional `mmap_size`
- Call sites keep `using (var conn = factory.GetConnection())` — no global connection held open across UI

**Verify:** Start app, confirm log line `SQLite configured. JournalMode=wal`. Index while browsing collection without lock errors.

## 2. Tracks page soft limit

**Files:** `Constants.cs`, `ITrackRepository` / `TrackRepository`, `TracksViewModelBase`, `CollectionTracksViewModel`

- Songs page: `TrackListLoadLimit = 2500` via `GetTracksPageAsync`
- Other track views default to unlimited (artist/album subsets stay full)

**Verify:** Library with >2500 tracks opens Songs tab quickly; first 2500 ordered by album appear.

## 3. DB-side search + result cap

**Files:** `DataUtils.CreateTrackSearchClause`, `SearchTracksAsync`, `TracksViewModelBase.FilterLists`

- Songs page: search hits SQLite (`LIMIT 500`), not in-memory full list filter
- Multi-word AND semantics aligned with previous client filter

**Verify:** Search rare title returns matches; search clears restores paged list.

## 4. Progress event split / background throttle

**Files:** `IPlaybackService`, `PlaybackService`, progress UI ViewModels

- `PlaybackProgressChanged` — core (scrobble, taskbar, external control)
- `PlaybackUiProgressChanged` — UI only; suppressed in background mode
- Timer interval: 0.5s foreground, 2.0s background

**Verify:** Minimize to tray — UI timers quiet, scrobble still works, taskbar progress still updates if enabled. Restore — slider tracks again.

## 5. Sidecar idle exit + online temp cache

**Files:** `UnblockSidecarService`, `NeteaseTemporaryAudioCache`, `Constants`

- Sidecar stops after 5 minutes without requests
- Temp audio cache: 256 MB / 6 hours (was 512 MB / 24 h)

**Verify:** Enable Unblock, play once, wait idle timeout — Node process exits. Re-play restarts sidecar.

## 6. Indexer parallel tags + less full-table materialization

**Files:** `IndexingService`

- Orphan delete via SQL `DELETE ... NOT IN (SELECT ... FolderTrack)`
- Missing files: light `SELECT TrackID, Path, SafePath`
- Update/Add: parallel `ProcessTrack` (max 4), serial SQLite writes

**Verify:** Fresh index and incremental index complete; track counts stable; no duplicate TrackIDs.

## 8. Spectrum / karaoke FPS

**Files:** `SpectrumAnalyzer`, `KaraokeLyricsControl`, `Constants`

- Spectrum ~40 ms (~25 FPS)
- Karaoke ~50 ms (~20 FPS); still stops when not visible/playing

## 9. Artwork UI path never uses original

**Files:** `MetadataService.GetArtworkAsync`

- `size <= 0` coerced to `ArtworkDefaultSize` (900)
- Edit/export paths that need original still use `FileMetadata.ArtworkData` directly

**Verify:** Play high-res embedded cover — working set does not spike to multi-megapixel decode for UI.

---

## Constants reference

| Constant | Value |
|---|---|
| `CollectionTracksInitialLimit` | 2500 |
| `CollectionSearchResultLimit` | 500 |
| `PlaybackProgressIntervalSeconds` | 0.5 |
| `PlaybackProgressBackgroundIntervalSeconds` | 2.0 |
| `SpectrumRefreshIntervalMs` | 40 |
| `KaraokeRefreshIntervalMs` | 50 |
| `UnblockSidecarIdleTimeoutMinutes` | 5 |
| `OnlineTempAudioMaxCacheBytes` | 256 MB |
| `OnlineTempAudioMaxAgeHours` | 6 |

---

## Follow-ups (not in this pass)

- Infinite scroll / Load more beyond 2500 on Songs page
- SQLite FTS5 for search
- Pre-fetch next online track only
- Aggressive UI unload tuning beyond existing 30s shell unload
