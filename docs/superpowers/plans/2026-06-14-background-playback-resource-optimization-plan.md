# Background playback resource optimization implementation plan

Source design:

- `docs/superpowers/specs/2026-06-14-background-playback-resource-optimization-design.md`

## Implementation rule

Optimize hidden UI and equivalent audio processing only. Do not lower playback thread priority, stop the active audio device, change decoder selection, or disable user-facing integrations such as media keys, Last.fm scrobbling, Discord Rich Presence, and external control.

## Commit 1: Add app visibility state

Purpose:

- Provide one source of truth for whether Dopamine is visible, tray-popup-visible, or in background playback mode.

Files to add:

- `Dopamine.Services/Shell/IAppVisibilityService.cs`
- `Dopamine.Services/Shell/AppVisibilityService.cs`

Files to edit:

- `Dopamine.Services/Dopamine.Services.csproj`
- `Dopamine/App.xaml.cs`
- `Dopamine/Views/Shell.xaml.cs`
- `Dopamine/Views/Common/TrayControls.xaml.cs`

Implementation steps:

1. Add `IAppVisibilityService` with read-only properties:
   - `IsMainWindowInteractiveVisible`
   - `IsTrayControlsVisible`
   - `IsBackgroundPlaybackMode`
   - `VisibilityChanged`
2. Add mutation methods on the same interface for the first pass:
   - `SetMainWindowInteractiveVisible(bool isVisible)`
   - `SetTrayControlsVisible(bool isVisible)`
3. Implement `AppVisibilityService` as a small `BindableBase`-free service with no UI dependencies except the booleans passed to it.
4. Register it as a singleton in `App.RegisterTypes`.
5. Inject it into `Shell` and `TrayControls`.
6. In `Shell`, set main-window visibility:
   - true after the main window is loaded or restored.
   - false when the window enters `WindowState.Minimized`.
   - false in the close-to-tray path.
   - false on final close.
7. In `TrayControls.Show`, set tray visibility true after `base.Show()`.
8. In `TrayControls.Window_Deactivated`, set tray visibility false before or after `Hide()`.

Important details:

- `IsBackgroundPlaybackMode` should be true only when both main window and tray controls are not visible.
- Visibility updates must be idempotent and raise `VisibilityChanged` only when a value actually changes.
- Visibility service errors must never propagate into playback code.

Verification:

- Build solution.
- Minimize to tray and restore the main window.
- Left-click tray icon, then click outside the tray popup.
- Add temporary debugger/log observation only if needed; remove it before committing.

Rollback:

- Revert this commit only; no behavior should depend on it yet except visibility state updates.

## Commit 2: Gate progress and playback-info UI refresh

Purpose:

- Stop UI-only progress and time-string updates when no Dopamine UI is visible.

Files to edit:

- `Dopamine/ViewModels/Common/ProgressControlsViewModel.cs`
- `Dopamine/ViewModels/Common/ProgressControlsWithTimeViewModel.cs`
- `Dopamine/ViewModels/Common/PlaybackInfoControlViewModel.cs`
- `Dopamine/ViewModels/Common/Base/PlaybackControlsViewModelBase.cs`

Implementation steps:

1. Add access to `IAppVisibilityService` in each ViewModel.
   - Prefer constructor injection where Prism already provides dependencies.
   - For existing parameterless constructors that use `ServiceLocator`, use the same local pattern to avoid broad construction changes.
2. Add a shared local check in each class:
   - UI refresh is allowed when `!visibilityService.IsBackgroundPlaybackMode`.
3. In `PlaybackProgressChanged` handlers, return early while backgrounded.
4. Subscribe to `VisibilityChanged`.
5. When leaving background mode, refresh once from `IPlaybackService`.
6. Keep command execution unchanged.

Important details:

- Do not stop `PlaybackService.progressTimer` in this commit.
- Do not gate Last.fm scrobbling or system media controls.
- Do not gate `TaskbarService` yet; it has separate semantics because taskbar progress may be visible while the main window is hidden.

Verification:

- Build solution.
- Play a track in foreground and confirm progress/time update.
- Minimize to tray and confirm playback continues.
- Restore and confirm progress/time jump to the correct current position.
- Open tray popup and confirm progress/time show current values.

Rollback:

- Revert this commit to restore previous always-refresh UI behavior.

## Commit 3: Gate lyrics and Now Playing timers

Purpose:

- Stop the 100 ms lyrics highlight timer and Now Playing controls timer while backgrounded.

Files to edit:

- `Dopamine/ViewModels/Common/LyricsControlViewModel.cs`
- `Dopamine/Views/NowPlaying/NowPlaying.xaml.cs`

Implementation steps:

1. Resolve or inject `IAppVisibilityService` in `LyricsControlViewModel`.
2. Add `VisibilityChanged` handling:
   - If backgrounded, call existing `StopHighlighting()`.
   - If visible and Now Playing lyrics page is active, restart highlighting and run one highlight pass.
3. Update playback-resume logic:
   - Resume should start highlighting only when the UI is visible and the lyrics page is active.
4. Keep `RefreshLyricsAsync` gated by the existing Now Playing page checks.
5. In `NowPlaying.xaml.cs`, stop `hideControlsTimer` when entering background mode.
6. Restart `hideControlsTimer` only when Now Playing is visible and controls are shown.

Important details:

- Do not change lyrics parsing or automatic download behavior in this commit.
- Do not update UI collections from non-UI threads beyond existing behavior.

Verification:

- Build solution.
- Navigate to Now Playing lyrics page and play a synced lyrics track.
- Confirm highlighting works in foreground.
- Minimize to tray for at least one minute.
- Restore and confirm the highlighted line catches up to the current time.

Rollback:

- Revert this commit if lyrics behavior regresses.

## Commit 4: Lazy-create tray controls

Purpose:

- Avoid creating the tray popup and its child controls during shell startup.

Files to edit:

- `Dopamine/Views/Shell.xaml.cs`
- `Dopamine.Services/Notification/INotificationService.cs`
- `Dopamine/Services/Notification/LegacyNotificationService.cs`
- `Dopamine/Services/Notification/NotificationService.cs` if inherited behavior requires no extra change, leave untouched.

Implementation steps:

1. Remove eager `this.trayControls = this.container.Resolve<TrayControls>()` from `Shell.InitializeWindows`.
2. Keep `MiniPlayerPlaylist` eager for now; it is already passed to notification service and has separate behavior.
3. Call `notificationService.SetApplicationWindows(this, this.miniPlayerPlaylist, null)` during shell initialization.
4. Add a private `EnsureTrayControls()` method on `Shell`:
   - Resolve `TrayControls` if `this.trayControls == null`.
   - Pass the new instance to `notificationService.SetApplicationWindows(null, null, this.trayControls)`.
   - Return the instance.
5. In left-click tray icon handling, call `EnsureTrayControls()` before showing the popup.
6. Keep right-click tray menu independent from `TrayControls`.
7. Make `SetApplicationWindows` explicitly support null arguments as "leave the existing reference unchanged".

Important details:

- Notification service currently checks `trayControlsWindow.IsActive` to suppress notifications while tray controls are visible. This must still work after lazy creation.
- `notificationService.HideNotification()` remains in `TrayControls.Show`.
- Do not add timed disposal in the first lazy-create commit; first prove delayed creation is stable.

Verification:

- Build solution.
- Start Dopamine and do not click the tray icon.
- Confirm no tray popup is created during startup by debugger or logging if needed.
- Right-click tray icon and confirm menu opens.
- Left-click tray icon and confirm popup opens with current playback state.
- Play, pause, skip, and change volume from the tray popup.
- Confirm notifications are not shown while tray popup is active.

Rollback:

- Revert this commit to restore eager tray popup creation.

## Commit 5: Avoid unnecessary taskbar progress work

Purpose:

- Keep taskbar progress behavior when enabled, but skip per-progress updates when disabled.

Files to edit:

- `Dopamine.Services/Taskbar/TaskbarService.cs`

Implementation steps:

1. Add a private `showProgressInTaskbar` field initialized from settings.
2. Update it in `SetShowProgressInTaskbar`.
3. In `PlaybackProgressChanged`, update `ProgressValue` only when `showProgressInTaskbar` is true.
4. Keep play/pause thumb-button text and icons unchanged.

Important details:

- Do not use app background state here. Taskbar progress may be useful specifically when the main window is hidden.
- Keep `ProgressState.None` and `ProgressValue = 0` behavior when setting is disabled.

Verification:

- Build solution.
- Toggle "Show playback progress in the Windows Taskbar".
- Confirm taskbar progress appears only when enabled.
- Confirm play/pause/previous/next thumb buttons still work.

Rollback:

- Revert this commit if taskbar progress behavior changes unexpectedly.

## Commit 6: Equalizer-disabled audio-chain bypass

Purpose:

- Remove avoidable equalizer processing when the equalizer is disabled and neutral.

Files to edit:

- `Dopamine.Core/Audio/CSCorePlayer.cs`
- `Dopamine.Services/Playback/PlaybackService.cs` if playback settings must pass an explicit equalizer-enabled flag.
- `Dopamine.Core/Audio/IPlayer.cs` if the playback settings contract changes.
- Any `IPlayer` implementations that must satisfy the changed interface.

Implementation steps:

1. Check current `IPlayer.SetPlaybackSettings` signature.
2. Prefer adding a boolean `isEqualizerEnabled` to playback settings instead of inferring from band values alone.
3. Store the flag in `CSCorePlayer`.
4. In `GetCodec`, append the equalizer source only when equalizer is enabled.
5. If bypassing the equalizer, set `this.equalizer = null` and make `ApplyFilter` safely no-op.
6. Keep existing behavior when equalizer is enabled.
7. Ensure disabling the equalizer affects newly created playback chains; immediate rebuild of the current chain is not required in the first pass.

Important details:

- Do not change decoder selection.
- Do not change WASAPI/DirectSound settings.
- Do not change audio thread priority.
- If the neutral-state check is uncertain, fall back to the current equalizer path.

Verification:

- Build solution.
- Play at least one MP3.
- If sample files are available, also play FLAC, OGG, and WMA.
- Confirm seek works.
- Confirm volume is unchanged in a practical listening check.
- Enable equalizer and confirm filters still affect audio on the next playback chain.

Rollback:

- Revert this commit if any audio regression appears.

## Commit 7: Optional measured follow-up for progress event split

Purpose:

- Only do this if profiling after commits 1-6 still shows `PlaybackProgressChanged` fan-out as a meaningful background cost.

Candidate files:

- `Dopamine.Services/Playback/IPlaybackService.cs`
- `Dopamine.Services/Playback/PlaybackService.cs`
- UI ViewModels that currently subscribe to `PlaybackProgressChanged`.

Implementation options:

1. Add a UI-only progress event and move visible UI subscribers to it.
2. Add a UI refresh coordinator that listens to core progress and app visibility.

Recommendation:

- Defer this commit unless measurements justify it.
- The earlier UI gates should be enough for the first optimization pass.

Verification:

- Repeat Stage 0 measurements before and after.
- Confirm Last.fm and system media controls still receive necessary progress updates.

Rollback:

- Revert this commit independently.

## Commit 8: Optional online-background policy

Purpose:

- Only do this if background network activity remains a user concern after UI work is complete.

Candidate files:

- `Dopamine/ViewModels/Common/ArtistInfoControlViewModel.cs`
- `Dopamine/ViewModels/Common/LyricsControlViewModel.cs`
- `Dopamine/BaseSettings.xml`
- Settings ViewModel and language files if a user-facing setting is added.

Implementation outline:

1. Add explicit settings for visible-only artist-info and lyrics downloads.
2. Default them conservatively so existing online behavior is not silently removed.
3. Gate artist-info downloads on the relevant page being visible.
4. Gate automatic lyrics download on lyrics page visibility.
5. Keep Last.fm scrobbling and Discord Rich Presence unchanged.

Verification:

- Confirm background playback still scrobbles.
- Confirm lyrics and artist info load when their pages are visible.
- Confirm no background downloads occur when the new settings are enabled.

Rollback:

- Revert this optional commit independently.

## Suggested implementation order

1. Commit 1: visibility state.
2. Commit 2: progress and playback-info UI gates.
3. Commit 3: lyrics and Now Playing timers.
4. Commit 4: lazy tray controls.
5. Measure and manually test.
6. Commit 5: taskbar disabled-state skip.
7. Commit 6: equalizer-disabled bypass.
8. Measure again.
9. Decide whether commits 7 and 8 are still worth doing.

## Per-commit static checks

Run after each code commit when the local toolchain is available:

```powershell
nuget restore .\Dopamine.sln
msbuild .\Dopamine.sln /p:Configuration=Release /m
```

If the local environment does not have the required .NET Framework or Visual Studio build tools, document that the commit received static source review only.

## Final manual regression pass

Run after the first full implementation batch:

1. Start Dopamine and play a track in the full player.
2. Confirm progress, playback info, cover art, and controls update.
3. Minimize to tray for at least 30 minutes.
4. Confirm playback continues and automatic next track works.
5. Confirm media keys work while backgrounded.
6. Open tray popup and confirm title, cover, progress, volume, and play/pause state are current.
7. Close tray popup and confirm notifications can appear again according to settings.
8. Restore main window and confirm UI catches up within one refresh cycle.
9. Navigate to Now Playing lyrics page, minimize, restore, and confirm lyrics highlight catches up.
10. Toggle taskbar progress and verify enabled/disabled behavior.
11. If Last.fm is signed in, confirm scrobbling still happens.
12. If Discord Rich Presence is enabled, confirm track status still updates.
13. If external control is enabled, confirm external play/pause/next/previous still works.

## Performance checkpoints

Measure after commit 4 and commit 6:

- Foreground CPU/GPU/private working set.
- Background tray CPU/GPU/private working set.
- Tray popup open CPU/GPU/private working set.
- Lyrics page background CPU.
- Startup memory before first tray-popup click.
- Memory after 50 tray-popup open/close cycles.

Expected result:

- Background UI CPU wakeups decrease after commits 2 and 3.
- Startup memory and event subscriptions decrease after commit 4.
- CPU during playback with equalizer disabled may decrease after commit 6.
- GPU activity should remain no worse; spectrum unregister behavior must remain intact.

## Stop conditions

Stop implementation and reassess if any of these occur:

- Audio skips, stutters, or fails to advance to the next track.
- Tray right-click exit stops working.
- Media keys stop working in background.
- Main window restore loses navigation state.
- Last.fm scrobbling stops when signed in.
- A build failure requires broad unrelated refactoring.
