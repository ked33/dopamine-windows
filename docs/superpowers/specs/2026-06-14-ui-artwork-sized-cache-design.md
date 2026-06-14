# UI artwork sized cache design

## Summary

This design reduces memory and image-processing overhead for artwork shown in Dopamine UI surfaces by requesting artwork at display-appropriate sizes and caching artwork by normalized size.

The change is intentionally limited to UI thumbnails, notification thumbnails, and album-cover color extraction. It must not change original artwork quality for editing, exporting, or writing metadata back to audio files.

## Goals

- Avoid loading full-resolution artwork for small UI surfaces such as tray controls, playback controls, and notifications.
- Keep high-DPI UI artwork sharp by using display size plus an upscale factor.
- Prevent small thumbnails and larger artwork from sharing the same memory-cache entry.
- Preserve the existing background-playback behavior that skips hidden artwork loading.
- Keep the implementation local to existing metadata and UI artwork code.

## Non-goals

- Do not change artwork editing, exporting, or metadata write-back paths.
- Do not rewrite the full image pipeline or replace `ImageUtils` in the first pass.
- Do not change album-list artwork loading unless implementation reveals a direct cache bug.
- Do not disable album-cover color following; only reduce the artwork size used for color extraction.
- Do not introduce persistent on-disk thumbnail caches in this pass.

## Current code observations

- `IMetadataService.GetArtworkAsync(path, int size = 0)` already accepts a requested size.
- `MetadataService.GetEmbeddedArtwork` returns the original embedded artwork when `size == 0`, and resized artwork when `size > 0`.
- `MetadataService.GetAlbumArtwork` passes the requested size through to `ImageUtils.Image2ByteArray`.
- The current metadata artwork memory cache is keyed only by filename, so different requested sizes can pollute each other.
- `CoverArtControlViewModel` currently calls `metadataService.GetArtworkAsync(track.Path)` without a size, so playback UI artwork requests original artwork bytes.
- `CoverArtControl` has actual UI dimensions and already reacts to `Loaded` and `SizeChanged`, making it the natural place to derive a display-driven artwork size.
- Album-list artwork already passes `UpscaledCoverSize` through `PathToImageConverter`, so it is not the first target for this change.
- Background visibility gating is already in place through `IAppVisibilityService`; hidden UI artwork should remain skipped.

## Recommended approach

Use display-driven size buckets:

1. `CoverArtControl` computes a desired artwork pixel size from its actual UI size.
2. The desired size is normalized to a small fixed set of buckets.
3. `CoverArtControlViewModel` requests artwork with the normalized size.
4. `MetadataService` uses both filename and normalized size in its memory-cache key.
5. Direct UI thumbnail callers such as notifications and color extraction pass explicit small sizes.

This avoids a larger image-service rewrite while fixing the main current problem: playback UI surfaces can request full-resolution artwork and share one filename-only cache entry.

## Size policy

Use fixed normalized buckets:

```text
128, 256, 512, 900, 1500
```

The requested size is calculated from:

- `max(ActualWidth, ActualHeight)` for the UI control.
- `Constants.CoverUpscaleFactor` as the high-DPI clarity factor.
- The next bucket greater than or equal to the scaled size.
- A hard maximum of `1500`.

Suggested examples:

- Tray controls artwork: `60 * 2.5 = 150`, bucket `256`.
- Bottom playback controls artwork: `70 * 2.5 = 175`, bucket `256`.
- Micro player artwork: `135 * 2.5 = 337.5`, bucket `512`.
- Cover player artwork: `350 * 2.5 = 875`, bucket `900`.
- Large Now Playing artwork: `600 * 2.5 = 1500`, bucket `1500`.

If the control has no valid size yet, it should use a safe default bucket instead of falling back to original artwork. A default of `900` is appropriate for the shared playback artwork path because it avoids blurry larger controls while still preventing multi-megapixel original artwork from entering the UI path.

## Cache design

`MetadataService` should normalize requested artwork sizes before cache lookup and before generating cache keys.

Cache key format:

```text
<filename>|artwork-size:<normalized-size>
```

Rules:

- `size <= 0` means original artwork and should use an explicit original key, for example `artwork-size:original`.
- `size > 0` should be normalized to a bucket before lookup and storage.
- Different buckets for the same file must not share a cache entry.
- The existing short absolute expiration can remain for the first implementation.
- Cache key creation should be centralized in `MetadataService` to avoid caller-specific behavior.

This prevents two failure modes:

- A small tray thumbnail being reused by a large Cover Player image.
- A full-resolution image being reused by small controls and defeating the memory optimization.

## Data flow

### Playback UI artwork

1. `CoverArtControl` loads or changes size.
2. It derives a normalized artwork size and exposes it to the view model.
3. `CoverArtControlViewModel` refreshes artwork when:
   - playback succeeds,
   - current track changes,
   - visibility changes from background to visible,
   - the requested artwork size changes enough to move to a different bucket.
4. If `IAppVisibilityService.IsBackgroundPlaybackMode` is true, the view model clears artwork and skips loading.
5. If visible, the view model calls `metadataService.GetArtworkAsync(track.Path, requestedArtworkSize)`.
6. The UI continues binding the returned `byte[]` through the existing image converter path.

### Notifications

Notification artwork should request an explicit thumbnail size.

- System media thumbnail: use a notification-appropriate bucket, preferably `512`.
- Legacy notification window: request `512`, then keep the existing `ByteToBitmapImage(artworkData, 300, 300, 0)` display conversion.
- If background playback mode is active, keep the current behavior of passing no artwork.

### Album-cover color extraction

When following album cover color and the UI is visible, `AppearanceService` should request a small artwork size before calling `ImageUtils.GetDominantColor`.

Use `256` as the first-pass size. This should be enough for dominant color extraction while avoiding full-resolution artwork. If visual color differences are unacceptable in manual testing, increase this path to `512` without changing the rest of the design.

## Error handling

- Artwork loading failures must never affect playback.
- If artwork loading fails, the UI should keep showing the existing placeholder behavior.
- Invalid or zero control sizes should use the default bucket, not original artwork.
- Background mode should keep clearing or skipping UI artwork.
- If a requested thumbnail cannot be generated, callers can receive `null` and keep current fallback behavior.

## Implementation outline

### Metadata service

- Add a private helper to normalize requested artwork sizes.
- Add a private helper to build size-aware cache keys.
- Use the normalized size for cache lookup, artwork loading, and cache storage.
- Keep `size == 0` behavior for original artwork consumers.

### Cover art control

- Add a dependency property or equivalent binding path for requested artwork size.
- Calculate requested size on `Loaded` and `SizeChanged`.
- Avoid refreshing artwork for every pixel-level resize; only trigger a view-model refresh when the normalized bucket changes.

### Cover art view model

- Store the current requested artwork size.
- Refresh artwork using `GetArtworkAsync(track.Path, requestedArtworkSize)`.
- Keep existing `IAppVisibilityService` checks.
- Avoid duplicate refreshes when the track and requested bucket did not change.

### Direct callers

- Update notification thumbnail calls to pass an explicit size.
- Update album-cover color extraction to pass `256`.
- Leave editing, export, and metadata write-back calls unchanged.

## Testing and verification

Static verification:

- Confirm UI thumbnail callers pass non-zero sizes.
- Confirm original artwork paths for edit, export, and metadata write-back still use original artwork.
- Confirm cache keys include normalized size.
- Confirm background mode still prevents hidden artwork loading.

Build verification:

- Build the solution if the local environment supports it.
- If local build tools are unavailable, perform static verification and state that runtime/build verification was not performed.

Manual verification:

- Play tracks with normal and very large embedded artwork.
- Switch tracks in the full player, Now Playing, Cover Player, Micro Player, and tray controls.
- Confirm artwork remains sharp at 100%, 200%, and 250% display scaling where available.
- Confirm notification artwork still appears when notifications are allowed and UI is visible.
- Confirm album-cover color following still changes theme color in visible mode and skips work in background mode.
- Confirm edit/export/save artwork workflows preserve original image quality.

Resource comparison:

- Compare process working set and CPU spikes while switching tracks with high-resolution artwork.
- Compare tray popup opening and Cover Player switching with the same test track.
- Treat this as static/design success unless runtime profiling shows no measurable improvement.

## Rollback plan

The change should be reversible by restoring:

- filename-only artwork cache keys,
- `CoverArtControlViewModel` calls without requested size,
- direct notification and color-extraction calls without explicit thumbnail sizes.

Because original artwork editing and write-back paths are out of scope, rollback should not affect user metadata data.
