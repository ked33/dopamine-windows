# Background playback resource optimization design

## Summary

This design reduces CPU, GPU, and memory overhead while Dopamine is playing audio in the background with the main GUI hidden in the notification area.

The first implementation should optimize non-audio work only. Audio decoding, output device lifetime, and playback thread priority stay unchanged unless profiling later proves they are the dominant cost and a safe audio-specific change is designed separately.

## Goals

- Keep music playback uninterrupted while the main window is minimized or closed to the notification area.
- Reduce background UI wakeups, layout work, animation work, and FFT work that has no value while the UI is not visible.
- Avoid changing user-visible playback behavior: media keys, tray menu, tray controls, taskbar buttons, automatic next track, Last.fm scrobbling, and Discord Rich Presence should continue to work according to their settings.
- Add enough measurement points that before/after resource use can be compared instead of judged by feel.
- Keep the first implementation low-risk and reversible.

## Non-goals

- Do not lower CSCore playback thread priority.
- Do not stop or recreate the active audio output device when entering background mode.
- Do not force garbage collection as a primary optimization strategy.
- Do not disable Last.fm scrobbling, Discord Rich Presence, media keys, or external control unless the user turns those features off.
- Do not rewrite the Prism navigation model or unload the whole main UI in the first pass.

## Current code observations

- Main-window close-to-tray and minimize-to-tray currently keep the main window alive, set `WindowState.Minimized`, and hide it from the taskbar or Alt-Tab. This preserves UI state but does not release the current visual tree.
- `SpectrumAnalyzerControl` already unregisters spectrum players when `ShellService.WindowState` is minimized. The most obvious GPU-heavy FFT visualization path is therefore already partially protected.
- `PlaybackService` has a 0.5 second `progressTimer` that raises `PlaybackProgressChanged`. This event fans out to UI controls, taskbar progress, Last.fm scrobble checks, and optional external control callbacks.
- `TrayControls` is created during shell initialization. Its hidden window contains cover art, playback info, progress, volume, and playback controls, so creating it eagerly also creates event subscriptions before the user opens the tray popup.
- `LyricsControlViewModel` has a 100 ms highlight timer. It gates itself on the Now Playing lyrics page, but not on main-window visibility.
- `CSCorePlayer.GetCodec` always appends the 10-band equalizer source chain. The default equalizer setting is disabled, so there is likely avoidable per-sample processing when all bands are neutral.

## Design approach

Use a staged design with a conservative default:

1. Measure first.
2. Add a single background visibility state.
3. Gate UI-only updates using that state.
4. Lazily create tray controls.
5. Optimize the audio processing chain only where behavior is equivalent.
6. Consider aggressive UI unloading later as an optional mode.

The first implementation should target stages 0 through 4. Aggressive UI unloading should remain a later, separate change because it affects Prism region lifetime, page reconstruction, and state restoration.

## Stage 0: measurement baseline

Define four repeatable scenarios:

- Foreground playback with the full player visible.
- Background playback after minimizing or closing to tray.
- Background playback with the tray popup open.
- Now Playing lyrics page playback, then minimize to tray.

Collect at least:

- Process CPU.
- GPU engine usage where available.
- Private working set.
- Commit size.
- Thread count.
- Handle count.
- Disk and network activity.
- Relevant log lines.

Recommended tools:

- Task Manager or Process Explorer for quick process-level numbers.
- PerfView, Windows Performance Analyzer, or Visual Studio Profiler for call stacks and timer wakeups.

The implementation is considered successful only if playback remains stable and measured background UI work drops in at least the scenarios touched by the change.

## Stage 1: app visibility service

Add a small visibility contract that other components can query or subscribe to.

Suggested interface:

```csharp
public interface IAppVisibilityService
{
    bool IsMainWindowInteractiveVisible { get; }
    bool IsTrayControlsVisible { get; }
    bool IsBackgroundPlaybackMode { get; }

    event EventHandler VisibilityChanged;
}
```

Definitions:

- `IsMainWindowInteractiveVisible` is true when the main shell is not minimized and is intended to be visible.
- `IsTrayControlsVisible` is true while the tray controls popup is open.
- `IsBackgroundPlaybackMode` is true when audio may be playing but neither the main shell nor tray controls are visible.

The service can be implemented from existing shell events first:

- Shell `StateChanged`.
- Shell close-to-tray path.
- Shell restore path.
- Tray controls `Show` and `Hide`.

This keeps the first pass local to existing shell and tray lifecycle code.

## Stage 2: gate UI-only updates

Use `IAppVisibilityService` to suppress updates that are only useful when a user can see the UI.

Gate these components first:

- `ProgressControlsViewModel`.
- `ProgressControlsWithTimeViewModel`.
- `PlaybackInfoControlViewModel`.
- `PlaybackControlsViewModelBase`.
- `LyricsControlViewModel`.
- `NowPlaying.xaml.cs` hide-controls timer.

Expected behavior:

- While `IsBackgroundPlaybackMode` is true, progress controls do not update slider values or formatted time strings.
- While `IsBackgroundPlaybackMode` is true, lyrics highlighting stops.
- On visibility restore, each affected component refreshes once from `IPlaybackService`.
- Playback commands still work because command execution remains tied to `IPlaybackService`, not to UI refresh timers.

Do not gate:

- Audio playback.
- Queue advancement.
- Media key hook.
- Tray icon right-click menu.
- Last.fm scrobbling checks in the first pass.
- Discord Rich Presence updates.

## Stage 3: lazy tray controls

Change tray controls creation from eager to lazy:

- Do not resolve `TrayControls` during `Shell.InitializeWindows`.
- Resolve and initialize it on the first left-click on the tray icon.
- When shown, refresh the tray controls immediately from current playback state.
- When hidden, mark `IsTrayControlsVisible` false.

Optional follow-up:

- Keep the tray controls instance alive for a short reuse window, for example 30 to 60 seconds.
- Dispose or dereference it after the reuse window if it remains hidden.

This avoids paying for cover art, progress controls, and playback information bindings when the user never opens the tray popup.

## Stage 4: progress event split

The existing `PlaybackProgressChanged` event currently serves multiple consumers. Split the behavior conceptually before changing code:

- Core playback progress: needed for playback state, scrobbling, taskbar if enabled, and external control if enabled.
- UI progress refresh: needed only when the main window or tray controls are visible.

Implementation options:

- Minimal option: keep the existing event, but UI subscribers skip work while backgrounded.
- Cleaner option: add a separate UI progress event from a UI refresh coordinator.

Recommendation for first pass:

- Use the minimal option first.
- Only introduce a separate UI progress event after profiling shows event fan-out itself is still a meaningful cost.

Taskbar behavior:

- If `ShowProgressInTaskbar` is false, `TaskbarService` should not process progress changes.
- If true, it can keep updating in background because the taskbar progress is visible outside Dopamine.

External control behavior:

- If external control is disabled, no external progress callback work should occur.
- If enabled, keep protocol behavior stable. Optimize only unnecessary callback fan-out to dead or absent clients.

## Stage 5: equalizer chain bypass

Optimize audio processing only when behavior is equivalent:

- If equalizer is disabled and all equalizer bands are neutral, do not append the equalizer source chain.
- If equalizer is enabled, keep the current equalizer path.
- Do not change output thread priority, WASAPI mode, device lifetime, or decoder choice as part of this optimization.

First-pass decision:

- Applying an equalizer enable/disable change to the currently playing track may require rebuilding the player chain.
- To keep the first pass low-risk, the bypass only needs to affect newly created playback chains.
- If current settings code already rebuilds the active playback chain safely, the bypass may apply immediately; otherwise it applies from the next track.

Validation:

- Play MP3, FLAC, OGG, and WMA samples where available.
- Compare volume and seek behavior with equalizer off and on.
- Confirm no playback failure when switching tracks.

## Stage 6: optional online-background policy

Online features should not be silently disabled. Add a separate policy only if the user wants a stronger background resource mode.

Candidate behavior:

- Artist information downloads only when the artist-info page is visible.
- Automatic lyrics downloads only when the lyrics page is visible.
- Last.fm scrobbling continues in background.
- Discord Rich Presence continues in background if enabled.

This should be controlled by settings, not hard-coded.

## Stage 7: optional aggressive memory mode

Aggressive memory reduction is intentionally out of the first implementation.

Future option:

- On entering background mode, navigate `PlayerTypeRegion` to `Empty`.
- Save the current player type, full player page, Now Playing subpage, and mini-player state.
- On restore, rebuild the page tree and refresh from playback state.

Risks:

- Prism region state loss.
- Slower restore.
- More complex event unsubscription.
- Page-specific state bugs.

This mode should be opt-in because it trades restore latency and complexity for lower memory.

## Settings

Add one user-facing umbrella setting:

- `Playback.BackgroundResourceSavingMode`

Recommended default:

- Enabled for low-risk UI refresh suppression and lazy tray controls.
- Disabled for aggressive UI unloading.

If finer controls are needed later:

- `Playback.SuspendHiddenUiRefresh`
- `Playback.LazyCreateTrayControls`
- `Playback.UnloadUiWhenBackgrounded`
- `Lyrics.DownloadOnlyWhenVisible`
- `Lastfm.DownloadArtistInfoOnlyWhenVisible`

## Error handling

- Visibility-state updates should be best-effort and must not throw into playback code.
- If a UI component fails to refresh after restore, log the error and leave playback running.
- Tray controls lazy creation failure should not break the tray right-click exit menu.
- Equalizer bypass should fall back to the existing equalizer chain if the neutral-state check is uncertain.

## Testing and verification

Static verification:

- Build the solution.
- Search for progress and timer subscribers to confirm all intended UI subscribers are gated.
- Confirm spectrum unregister logic still compiles and remains active.

Manual runtime verification:

- Play a track and minimize to tray for at least 30 minutes.
- Confirm automatic next-track playback works.
- Confirm media keys work while backgrounded.
- Confirm tray right-click menu opens and exit works.
- Confirm tray left-click popup opens with current title, cover, progress, volume, and play/pause state.
- Confirm restoring the main window refreshes progress, time, lyrics, and cover art.
- Confirm lyrics page stops background highlighting and resumes at the current line.
- Confirm taskbar progress respects `ShowProgressInTaskbar`.
- Confirm Last.fm scrobbling still triggers when signed in.
- Confirm Discord Rich Presence still updates when enabled.
- Confirm external control still works when enabled and has no work when disabled.

Performance verification:

- Repeat the Stage 0 scenarios after each stage.
- Compare CPU wakeups and UI-thread activity before/after Stage 2.
- Compare startup and background memory before/after Stage 3.
- Compare audio CPU with equalizer disabled before/after Stage 5.

## Rollout plan

1. Add measurement notes and collect baseline.
2. Add `IAppVisibilityService`.
3. Gate UI-only refreshes.
4. Lazy-create tray controls.
5. Profile again.
6. Bypass equalizer chain when disabled.
7. Profile again.
8. Decide whether online-background policy or aggressive memory mode is worth implementing.

## Acceptance criteria

- Background playback is stable and does not skip, stutter, or stop.
- Main-window restore shows correct playback state within one refresh cycle.
- Tray popup shows correct playback state immediately after opening.
- CPU and GPU usage in background mode are lower or no worse in measured scenarios.
- Memory usage is no worse after repeated tray popup open/close cycles.
- No new unhandled exceptions appear in logs during minimize, restore, tray popup, or track changes.
