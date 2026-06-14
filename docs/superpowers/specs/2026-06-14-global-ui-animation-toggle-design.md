# Global UI animation toggle design

## Summary

This design adds a global visual animation toggle to `Settings > Appearance`.

The setting controls Dopamine UI motion only. When disabled, visual animations should stop or become immediate state changes. Playback behavior, audio fade, metadata work, and other non-visual logic must not change.

## Goals

- Add a user-facing toggle in `Settings > Appearance`.
- Store the preference in the existing `Appearance` settings namespace.
- Keep animations enabled by default for existing behavior.
- Disable visual motion across the main UI when the setting is off.
- Preserve final visual states when animations are skipped.
- Keep the implementation maintainable by routing code-driven animations through a shared helper.

## Non-goals

- Do not change `Settings > Playback > Playback fade`; audio fade is not a UI animation.
- Do not change playback, queue, indexing, notification eligibility, or metadata behavior.
- Do not rely on a Windows or WPF system-wide animation flag.
- Do not rewrite the theme/style system.
- Do not change Windows shell animations outside Dopamine's control.

## Current code observations

- `SettingsAppearance.xaml` already uses `Windows10ToggleSwitch` controls bound to `SettingsAppearanceViewModel` properties.
- `SettingsAppearanceViewModel` persists appearance options with `SettingsClient.Set("Appearance", ...)`.
- `BaseSettings.xml` stores default settings and currently uses `Configuration.Version` value `119`.
- UI animations are spread across several mechanisms:
  - code-driven `BeginAnimation` calls in custom controls and views,
  - XAML `Storyboard` and `BeginStoryboard` triggers,
  - `PopupAnimation` values such as `Fade` and `Slide`,
  - `SpectrumAnalyzer`, which uses a `DispatcherTimer` to draw continuous visual motion.
- Some XAML `Storyboard` instances are used for layout sizing, hover feedback, fade transitions, and page transitions.
- Several existing animations already use zero-duration state changes. These are state transitions rather than meaningful motion and can remain as-is if they do not create visible animated movement.

## Recommended approach

Use a centralized UI animation helper and apply it at animation entry points.

The helper should expose the current setting and small utility methods for code-driven animation. XAML-only animation sites should either use setting-aware resource values where practical or be converted to a small code-behind/helper path when they need to preserve final states.

This avoids a brittle one-off implementation while keeping the first pass scoped to the existing WPF application.

## Setting design

Add this setting to the `Appearance` namespace in `BaseSettings.xml`:

```xml
<Setting Name="EnableAnimations">
  <Value>True</Value>
</Setting>
```

Increment `Configuration.Version` from `119` to `120` so existing user settings can migrate and receive the new default.

The runtime meaning is:

- `true`: keep current visual animation behavior.
- `false`: skip Dopamine UI visual animations and apply final visual states immediately.

If the setting cannot be read, the helper should default to `true` to preserve existing behavior.

## Settings UI

Add a new section to `Settings > Appearance` near the other appearance-only options:

- Section title: `Animation`
- Description: `Enable visual animations`
- Control: existing `Windows10ToggleSwitch`
- Default state: on

Localization keys:

- `Animation`
- `Enable_Visual_Animations`

Add at least English and Simplified Chinese values:

- EN: `Animation`, `Enable visual animations`
- ZH-CN: `动画`, `启用界面视觉动画`

`SettingsAppearanceViewModel` should add a property such as `CheckBoxEnableAnimationsChecked`. Its setter should call:

```csharp
SettingsClient.Set<bool>("Appearance", "EnableAnimations", value, true);
```

The `true` notification argument lets currently loaded views respond immediately where they subscribe to `SettingsClient.SettingChanged`.

## Helper design

Add a small helper in the WPF app layer, for example `Dopamine/Utils/UiAnimationUtils.cs`.

Responsibilities:

- Read `Appearance.EnableAnimations`.
- Return `true` on read failures.
- Provide a single place to determine whether visual animations are allowed.
- Provide helper methods for code-driven animations where they make the call sites clearer.

Suggested API shape:

```csharp
public static class UiAnimationUtils
{
    public static bool AreAnimationsEnabled { get; }

    public static void BeginAnimationOrSet(
        DependencyObject target,
        DependencyProperty property,
        AnimationTimeline animation,
        object disabledValue);

    public static void BeginStoryboardOrApply(
        Storyboard storyboard,
        Action applyFinalState);

    public static PopupAnimation GetPopupAnimation(PopupAnimation enabledAnimation);
}
```

Exact method names can follow local style during implementation. The important constraint is that skipped animations must still set the final visual state.

## Coverage plan

### Code-driven animations

Update these code paths to consult the helper:

- `TransitioningContentControl`
- `CrossFadeContentControl`
- `ZoomInContentControl`
- `FullPlayer.AnimateBackIcon`
- `Shell.ShowClosingAnimation`
- `NowPlayingShowcase.ResizePlaybackInfo`

When animations are disabled:

- fade targets should be set directly to their final opacity,
- slide or zoom targets should be set directly to their final margin or size,
- delayed close-overlay animation should show the final overlay immediately when closing tasks require it,
- resize storyboards should apply the final size values without animation.

### XAML storyboard animations

Review all XAML `BeginStoryboard` and `Storyboard` sites. Convert or neutralize visible motion where needed.

Priority sites:

- page and section transitions,
- hover fade effects,
- mini player controls fade in/out,
- notification window control fade in/out,
- indexing and update status height/opacity transitions,
- search box text/icon transitions,
- album image source-change fade,
- semantic zoom panel fade/slide,
- playback playlist notification slide/fade.

For state-only zero-duration transitions, no behavioral change is needed unless they still produce visible motion.

### Popup animations

Replace hard-coded `PopupAnimation="Fade"` and `PopupAnimation="Slide"` with setting-aware behavior.

When animations are disabled, popups should use `PopupAnimation.None`. The popup should still open and close normally.

### Spectrum analyzer

Treat the spectrum analyzer's continuous drawing as visual UI animation.

`SpectrumAnalyzerControl` should also listen for `Appearance.EnableAnimations` changes. When the setting is off, it should unregister spectrum players and collapse or stop the animated spectrum surface. When the setting is on, it should re-run the existing registration checks.

This must not change the existing `Playback.ShowSpectrumAnalyzer` setting value. The effective behavior should be:

```text
show spectrum = Playback.ShowSpectrumAnalyzer && Appearance.EnableAnimations && current runtime conditions
```

## Data flow

1. User toggles `Enable visual animations` in `Settings > Appearance`.
2. `SettingsAppearanceViewModel` writes `Appearance.EnableAnimations`.
3. `SettingsClient.SettingChanged` notifies interested views and controls.
4. New animation starts consult the helper and either animate or apply final state.
5. Long-running visual animation surfaces such as the spectrum analyzer stop or restart on setting changes.

## Error handling

- If the setting is missing or cannot be read, keep animations enabled.
- If an animation resource cannot be found, preserve the existing defensive behavior and avoid crashing.
- Skipping an animation must not leave controls hidden, half-transparent, incorrectly sized, or with stale margins.
- Turning the setting off while an animation is active should stop or supersede the active animation where the touched control supports it.

## Testing and verification

Static verification:

- Search for `BeginStoryboard`, `Storyboard`, `BeginAnimation(`, `PopupAnimation`, `DispatcherTimer`, and `SpectrumAnalyzer`.
- Confirm each visible UI animation path is either controlled by the new setting or intentionally state-only.
- Confirm `BaseSettings.xml` remains valid XML.
- Confirm English and Simplified Chinese language files include the new keys.

Build verification:

- Build the solution if the local .NET Framework and Visual Studio/MSBuild toolchain is available.
- If local build tools are unavailable, report that build verification was not performed.

Manual verification:

- Open `Settings > Appearance` and confirm the new toggle is present.
- With animations enabled, confirm current UI motion is preserved.
- Disable animations and check:
  - settings page navigation switches immediately,
  - hover controls do not fade or resize with motion,
  - popups open without fade or slide,
  - mini player overlay controls appear and disappear immediately,
  - notification window hover controls appear and disappear immediately,
  - indexing/update bars do not animate height or opacity,
  - Now Playing resize changes apply immediately,
  - the spectrum analyzer does not keep animating.
- Re-enable animations and confirm newly triggered animations resume.
- Confirm `Settings > Playback > Playback fade` still controls audio fade independently.

## Rollback plan

Rollback should restore:

- `Appearance.EnableAnimations` removal from `BaseSettings.xml`,
- the appearance settings view-model and XAML changes,
- helper usage at animation call sites,
- spectrum analyzer checks for the animation setting,
- popup animation replacements.

Because this design does not change playback or metadata data, rollback should not affect user music data.
