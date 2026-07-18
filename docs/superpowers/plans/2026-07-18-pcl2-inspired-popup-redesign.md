# PCL2-Inspired Popup Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the clipboard history popup to the confirmed lightweight PCL-style visual system while preserving all existing clipboard, keyboard, taskbar, and tray behavior.

**Architecture:** Keep the existing WinForms borderless popup and owner-drawn ListBox. Extract palette, frame drawing, list-card rendering, and animation timing into focused helpers so PopupForm coordinates state and events instead of owning every visual detail. Use one UI Timer only while a transition is active; do not add WPF, WinUI, WebView, or third-party UI dependencies.

**Tech Stack:** C# 14, .NET 10, WinForms, GDI+, xUnit integration tests on STA threads, existing SQLite/history services.

---

## Existing Working-Tree Context

The branch already contains the previously accepted popup changes in:

- `src/LocalClipboard.App/UI/PopupForm.cs`
- `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

Do not reset or revert those edits. They already provide the 600px width, adaptive tall height, complete four-edge frame, taskbar entry, title-bar dragging, fixed-height rows, and single-click/double-click/Enter interaction behavior. The implementation below layers the new visual system on top of that baseline.

## File Map

- Create `src/LocalClipboard.App/UI/PopupAnimation.cs`: deterministic easing and a small timer-driven transition primitive.
- Create `src/LocalClipboard.App/UI/PopupListRenderer.cs`: owner-draw card geometry, colors, icons, text clipping, and state-specific rendering.
- Modify `src/LocalClipboard.App/UI/ThemePalette.cs`: add the muted surfaces, soft accent, hover border, and shadow colors needed by the B palette while keeping high-contrast handling.
- Modify `src/LocalClipboard.App/UI/PopupForm.cs`: compose the new frame/search/filter/list helpers, expose the settings action, track hover/focus/animation state, and preserve existing query/input behavior.
- Modify `src/LocalClipboard.App/TrayApplicationContext.cs`: pass the existing settings-opening action into PopupForm.
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`: cover the new visual hierarchy, animation lifecycle, and preserved interaction semantics.
- Modify `tests/LocalClipboard.App.IntegrationTests/Startup/ProgramTests.cs` only if startup smoke coverage needs to assert that the popup constructor wiring remains valid.

## Task 1: Lock The Visual Contract With Failing Tests

**Files:**
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`
- Verify `src/LocalClipboard.App/UI/PopupForm.cs`

- [ ] **Step 1: Add tests for the B visual hierarchy**

Add one STA test that locates the named controls and asserts the contract without asserting implementation details such as exact GDI pixels:

```csharp
[Fact]
public Task PopupForm_ExposesLightweightPclVisualHierarchy() => StaTest.RunAsync(() =>
{
    using PopupForm form = PopupForm.CreateForTest();

    Panel titleBar = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("TitleBar", true)));
    TextBox searchBox = Assert.IsType<TextBox>(Assert.Single(form.Controls.Find("SearchBox", true)));
    FlowLayoutPanel filterPanel = Assert.IsType<FlowLayoutPanel>(Assert.Single(form.Controls.Find("FilterPanel", true)));
    ListBox timeline = Assert.IsType<ListBox>(form.Controls.OfType<ListBox>().Single());

    Assert.Equal(new Size(600, 1200), form.ClientSize);
    Assert.Equal(new Padding(1), form.Padding);
    Assert.Equal(44, titleBar.Height);
    Assert.Equal(BorderStyle.FixedSingle, searchBox.BorderStyle);
    Assert.Equal(42, filterPanel.Height);
    Assert.Equal(DrawMode.OwnerDrawFixed, timeline.DrawMode);
    return Task.CompletedTask;
});
```

- [ ] **Step 2: Add tests for settings-button wiring and animation lifecycle**

Add a constructor test helper that supplies an `Action` for opening settings, then assert a named `SettingsButton` exists and can invoke that action. Add a timer lifecycle test against the animation helper API defined in Task 2:

```csharp
[Fact]
public Task PopupForm_SettingsButtonInvokesSettingsAction() => StaTest.RunAsync(() =>
{
    int invocationCount = 0;
    using PopupForm form = PopupForm.CreateForTest(() => invocationCount++);

    Button settingsButton = Assert.IsType<Button>(Assert.Single(
        form.Controls.Find("SettingsButton", searchAllChildren: true)));
    settingsButton.PerformClick();

    Assert.Equal(1, invocationCount);
    return Task.CompletedTask;
});
```

- [ ] **Step 3: Run the focused tests and verify red**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm_ExposesLightweightPclVisualHierarchy|FullyQualifiedName~PopupForm_SettingsButtonInvokesSettingsAction"
```

Expected: FAIL because the settings action overload, named button, and full visual contract do not yet exist.

## Task 2: Add The Deterministic Animation Primitive

**Files:**
- Create `src/LocalClipboard.App/UI/PopupAnimation.cs`
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Write easing tests first**

Add tests for the pure easing function and transition completion:

```csharp
[Theory]
[InlineData(0f, 0f)]
[InlineData(1f, 1f)]
public void PopupAnimation_EaseOutPreservesEndpoints(float input, float expected)
{
    Assert.Equal(expected, PopupAnimation.EaseOut(input), precision: 4);
}

[Fact]
public void PopupAnimation_TransitionCompletesAtTarget()
{
    var transition = new PopupAnimation(TimeSpan.FromMilliseconds(120));

    Assert.False(transition.Advance(TimeSpan.Zero));
    Assert.True(transition.Advance(TimeSpan.FromMilliseconds(120)));
    Assert.Equal(1f, transition.Progress, precision: 4);
}
```

- [ ] **Step 2: Run the animation tests and verify red**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupAnimation"
```

Expected: FAIL because `PopupAnimation` does not exist.

- [ ] **Step 3: Implement the minimal animation primitive**

Create an internal sealed type with this API:

```csharp
internal sealed class PopupAnimation
{
    private readonly TimeSpan duration;
    private TimeSpan elapsed;

    internal PopupAnimation(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        this.duration = duration;
    }

    internal float Progress { get; private set; }

    internal bool Advance(TimeSpan delta)
    {
        elapsed += delta;
        float linear = Math.Clamp((float)(elapsed.TotalMilliseconds / duration.TotalMilliseconds), 0f, 1f);
        Progress = EaseOut(linear);
        return linear >= 1f;
    }

    internal static float EaseOut(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return 1f - MathF.Pow(1f - value, 3f);
    }
}
```

- [ ] **Step 4: Run animation tests and verify green**

Run the same focused command. Expected: all animation tests pass with no warnings.

## Task 3: Update The Palette And Compose The New Frame

**Files:**
- Modify `src/LocalClipboard.App/UI/ThemePalette.cs`
- Modify `src/LocalClipboard.App/UI/PopupForm.cs`
- Modify `src/LocalClipboard.App/TrayApplicationContext.cs`
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Extend the palette contract with explicit B surfaces**

Add these fields to `ThemePalette` after `Selection`:

```csharp
Color MutedSurface,
Color SoftAccent,
Color HoverBorder,
Color Shadow
```

For light mode use `MutedSurface = Color.FromArgb(246, 248, 251)`, `SoftAccent = Color.FromArgb(232, 244, 255)`, `HoverBorder = Color.FromArgb(120, 186, 245)`, and `Shadow = Color.FromArgb(35, 40, 65, 75)`. For dark mode use equivalent muted surfaces and preserve high-contrast system colors.

- [ ] **Step 2: Add the settings action without changing tray behavior**

Change the PopupForm constructor to accept `Action openSettings`, store a null-checked delegate, and update `CreateForTest` with an overload accepting the action. In `TrayApplicationContext`, pass `ShowSettings` as the final constructor argument:

```csharp
popup = new PopupForm(
    paths.ImagesRoot,
    historyService.QueryAsync,
    historyService.DeleteAsync,
    historyService.SetFavoriteAsync,
    ActivateEntryAsync,
    ShowSettings);
```

- [ ] **Step 3: Implement the B title bar and frame**

Keep the existing borderless/taskbar behavior. Use a white `TitleBar` with a 4px blue accent strip, semibold title text, a named `SettingsButton`, and the existing named `CloseButton`. Keep `Padding = new Padding(1)` so the four-edge frame remains visible. Use the existing Win32 drag handler only for the title area.

- [ ] **Step 4: Run PopupForm tests and verify green**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm"
```

Expected: all popup tests pass, including existing close, taskbar, query, selection, favorite, and deletion behavior.

## Task 4: Rebuild Search And Filter Styling

**Files:**
- Modify `src/LocalClipboard.App/UI/PopupForm.cs`
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Add focused visual-state tests**

Test that the search box retains its placeholder and fixed border, filter buttons have transparent inactive state, and the active filter uses `SoftAccent` plus `Accent` text. The test should inspect actual button properties after constructing the real form.

- [ ] **Step 2: Implement the search surface**

Keep the current 150ms query debounce. Put the search box inside the existing named `SearchPanel`, add a leading search glyph label and a trailing clear button that only becomes visible when `searchBox.TextLength > 0`. The clear button must call `searchBox.Clear()` and return focus to the text box.

- [ ] **Step 3: Implement segmented filter visuals**

Use transparent inactive buttons with `palette.SecondaryText`; active buttons use `palette.SoftAccent`, `palette.Accent`, and semibold font. Keep the current `PopupFilter` tags and `FilterButton_Click` query behavior. Add pointer cursor and 100ms hover transition only; do not add a second query or timer.

- [ ] **Step 4: Run the focused visual and query tests**

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm"
```

Expected: all popup tests pass.

## Task 5: Extract And Upgrade List Card Rendering

**Files:**
- Create `src/LocalClipboard.App/UI/PopupListRenderer.cs`
- Modify `src/LocalClipboard.App/UI/PopupForm.cs`
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Add renderer geometry tests**

Move the existing summary/star geometry assertions into renderer-facing tests and add assertions that the card rectangle stays inside the item bounds with an 8px horizontal inset, a 4px vertical inset, and a 1px border. Keep the two-line summary contract:

```csharp
Rectangle card = PopupListRenderer.GetCardBounds(new Rectangle(0, 0, 600, 92));
Assert.Equal(new Rectangle(8, 4, 584, 84), card);
Assert.True(PopupForm.GetSummaryBounds(new Rectangle(0, 0, 600, 92), 14, 558).Bottom <= 82);
```

- [ ] **Step 2: Run renderer tests and verify red**

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupListRenderer|FullyQualifiedName~PopupForm_TextSummaryBoundsStayInsideListItem"
```

Expected: FAIL until the renderer API exists.

- [ ] **Step 3: Implement `PopupListRenderer`**

Expose an internal renderer with `GetCardBounds`, `Draw`, and `GetStarBounds` methods. The `Draw` method must:

- Fill the list background first.
- Draw a white card with a 1px border and a subtle shadow only for hover/selected states.
- Draw a 44–52px image thumbnail or a consistent text/image type glyph.
- Draw the time and metadata in the secondary color.
- Clip the summary to the existing two-line summary rectangle.
- Draw a blue selected border and pale blue selected fill; selected state wins over hover.
- Draw a 2px maximum right translation for hover by changing only the card rectangle, not the ListBox item geometry.

- [ ] **Step 4: Delegate `PopupForm.Timeline_DrawItem` to the renderer**

Keep event handling in PopupForm, but remove duplicated GDI brushes and geometry from the form. The form passes the current palette, `ClipboardEntryView`, selected state, hovered index, and focus state into the renderer.

- [ ] **Step 5: Run all popup tests and verify green**

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm|FullyQualifiedName~PopupListRenderer"
```

Expected: all popup and renderer tests pass, with no row overlap or scrolling regression.

## Task 6: Add Lightweight Hover, Selection, Filter, And Window Transitions

**Files:**
- Modify `src/LocalClipboard.App/UI/PopupForm.cs`
- Modify `src/LocalClipboard.App/UI/PopupAnimation.cs` if a reusable transition collection is needed
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Add tests for animation suppression and timer cleanup**

Test the pure decision helper used by PopupForm:

```csharp
[Fact]
public void PopupAnimation_DisablesTransitionsForHighContrast()
{
    Assert.False(PopupForm.ShouldAnimateForTest(highContrast: true, uiEffectsEnabled: true));
    Assert.False(PopupForm.ShouldAnimateForTest(highContrast: false, uiEffectsEnabled: false));
    Assert.True(PopupForm.ShouldAnimateForTest(highContrast: false, uiEffectsEnabled: true));
}
```

Add a form disposal test that confirms the animation timer is stopped/disposed and no timer callback runs after `Dispose`.

- [ ] **Step 2: Implement one shared 16ms animation timer**

Use one `System.Windows.Forms.Timer` with `Interval = 16`. Maintain only visible item transitions and a small window transition record. Start the timer when a transition is added; stop it when the transition collection is empty or the form is hidden. On each tick, advance transitions using `PopupAnimation.Advance`, invalidate only affected item rectangles, and apply the final state when complete.

- [ ] **Step 3: Add pointer and keyboard state transitions**

Keep the existing `Timeline_MouseMove` and `Timeline_MouseLeave` behavior, but animate hovered card values instead of immediately invalidating to the final state. Keep keyboard selection immediate enough to feel responsive while animating only fill/border color for 120ms.

- [ ] **Step 4: Add open/close transitions without delaying business actions**

On `ShowPopup`, set the final location and size before showing, then apply a short 160ms fade/translation if animations are enabled. On close, complete the hide transition only after a successful activation or explicit close; Esc and the close button must still hide immediately if animations are disabled. Do not change the existing `ShowInTaskbar` or background tray lifetime.

- [ ] **Step 5: Verify animation and interaction tests**

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm|FullyQualifiedName~PopupAnimation"
```

Expected: all tests pass, including single-click selection, double-click/Enter activation, star double-click protection, Delete, Esc, taskbar activation refresh, and timer cleanup.

## Task 7: Add Empty, Loading, Accessibility, And DPI States

**Files:**
- Modify `src/LocalClipboard.App/UI/PopupForm.cs`
- Modify `src/LocalClipboard.App/UI/PopupListRenderer.cs`
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Add tests for empty and loading messages**

Use the existing injected query delegate to return an empty list and assert a named empty-state label is visible without moving the search/filter controls. Add a failing-query case and assert the existing footer error text remains available.

- [ ] **Step 2: Implement stable empty/loading surfaces**

Add a lightweight centered empty state inside the list region, with separate text for no history and no search results. Loading-more status stays in the footer and never blocks input. Keep the list region height stable so the popup does not jump.

- [ ] **Step 3: Implement accessible names and high-contrast fallback**

Set `AccessibleName`/`AccessibleDescription` for search, settings, close, clear-search, filter buttons, and the timeline. Keep `ThemePalette.ReadCurrent()` returning system colors under high contrast and bypass all custom animation.

- [ ] **Step 4: Run UI tests at the normal test scale**

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm"
```

Expected: all popup tests pass.

## Task 8: Full Verification And Desktop Acceptance

**Files:**
- Verify `src/LocalClipboard.App/UI/PopupForm.cs`
- Verify `src/LocalClipboard.App/UI/PopupListRenderer.cs`
- Verify `src/LocalClipboard.App/UI/PopupAnimation.cs`
- Verify `src/LocalClipboard.App/UI/ThemePalette.cs`
- Verify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Run the complete automated suite**

```powershell
dotnet test LocalClipboard.slnx --no-restore
dotnet build LocalClipboard.slnx --no-restore -warnaserror
dotnet list LocalClipboard.slnx package --vulnerable --include-transitive
git diff --check
```

Expected: all tests pass, build has 0 warnings and 0 errors, no vulnerable packages are reported, and `git diff --check` is clean.

- [ ] **Step 2: Launch the app and verify the primary loop**

Stop any old `LocalClipboard.App` process, launch `src/LocalClipboard.App/bin/Debug/net10.0-windows/LocalClipboard.App.exe`, press `Alt+V`, then verify:

1. The window opens with a white PCL-style frame, blue accent, complete border, and subtle shadow.
2. Search focus, clear-search, filter selection, hover, and keyboard selection animate without input lag.
3. Single-click selects, double-click/Enter applies, star click toggles favorite, Delete removes, and Esc/× closes.
4. The window remains discoverable in the taskbar and refreshes after taskbar reactivation.
5. Long text remains clipped to two lines; images keep their thumbnails and metadata.

- [ ] **Step 3: Verify DPI and accessibility states**

Manually check 100%, 125%, 150%, and 200% Windows display scaling plus high-contrast mode. Confirm no clipped title, search box, filter label, card border, thumbnail, or footer text. Confirm animations disable cleanly in high contrast.

- [ ] **Step 4: Commit the completed visual redesign**

```powershell
git add src tests docs/superpowers/plans/2026-07-18-pcl2-inspired-popup-redesign.md
git diff --cached --check
git commit -m "feat: refresh popup visual design"
```

Do not commit the existing design-only commit again, and do not include unrelated files.
