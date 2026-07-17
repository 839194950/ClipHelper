# Popup Window Interaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the clipboard history window visible when it loses focus, add a custom draggable title bar, expose it in the taskbar while visible, and hide it through Esc, the close button, or successful content restoration.

**Architecture:** Preserve the existing borderless WinForms popup and add one focused title-bar control inside `PopupForm`. The title bar owns the close action and Win32 drag gesture, while `ShowInTaskbar = true` gives the visible form a standard Windows taskbar entry that disappears automatically when the form is hidden; the obsolete Deactivate handler and child-dialog guard are removed.

**Tech Stack:** C# 14, .NET 10, WinForms, xUnit, Windows user32 APIs.

---

### Task 1: Add Explicit Popup Closing And Dragging

**Files:**
- Modify: `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`
- Modify: `src/LocalClipboard.App/UI/PopupForm.cs`
- Modify: `src/LocalClipboard.App/TrayApplicationContext.cs`

- [ ] **Step 1: Write the failing popup behavior test**

Add a test that shows the popup, invokes its Deactivate lifecycle event, confirms it remains visible, locates the custom title bar and close button by name, clicks the close button, and confirms the form is hidden rather than disposed:

```csharp
[Fact]
public Task PopupForm_DeactivationStaysVisibleAndCloseButtonHides() => StaTest.RunAsync(() =>
{
    using PopupForm form = PopupForm.CreateForTest();
    form.Show();
    Application.DoEvents();

    typeof(Form)
        .GetMethod("OnDeactivate", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(form, [EventArgs.Empty]);

    Assert.True(form.Visible);
    Assert.Single(form.Controls.Find("TitleBar", searchAllChildren: true));
    Button closeButton = Assert.IsType<Button>(Assert.Single(
        form.Controls.Find("CloseButton", searchAllChildren: true)));

    closeButton.PerformClick();
    Application.DoEvents();

    Assert.False(form.Visible);
    Assert.False(form.IsDisposed);
    return Task.CompletedTask;
});
```

Add `using System.Reflection;` to the test file.

- [ ] **Step 2: Run the test and verify the red state**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm_DeactivationStaysVisibleAndCloseButtonHides"
```

Expected: FAIL because the current Deactivate handler hides the form and no controls named `TitleBar` or `CloseButton` exist.

- [ ] **Step 3: Add the custom title bar and close behavior**

Add Win32 constants and imports to `PopupForm`:

```csharp
private const int WmNcLButtonDown = 0x00A1;
private static readonly nint HtCaption = new(2);

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool ReleaseCapture();

[DllImport("user32.dll")]
private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
```

Add `using System.Runtime.InteropServices;`. In `BuildLayout`, create a 36-pixel title bar before the search panel:

```csharp
var titleBar = new Panel
{
    Name = "TitleBar",
    Dock = DockStyle.Top,
    Height = 36,
    BackColor = palette.Surface
};
var titleLabel = new Label
{
    AutoSize = false,
    Dock = DockStyle.Fill,
    Padding = new Padding(12, 0, 0, 0),
    Text = "剪贴板历史",
    TextAlign = ContentAlignment.MiddleLeft,
    ForeColor = palette.PrimaryText
};
var closeButton = new Button
{
    Name = "CloseButton",
    Dock = DockStyle.Right,
    Width = 42,
    Text = "×",
    FlatStyle = FlatStyle.Flat,
    BackColor = palette.Surface,
    ForeColor = palette.SecondaryText,
    TabStop = false,
    UseVisualStyleBackColor = false
};
closeButton.FlatAppearance.BorderSize = 0;
closeButton.Click += (_, _) => Hide();
closeButton.MouseEnter += (_, _) => closeButton.BackColor = palette.Selection;
closeButton.MouseLeave += (_, _) => closeButton.BackColor = palette.Surface;
titleBar.MouseDown += TitleBar_MouseDown;
titleLabel.MouseDown += TitleBar_MouseDown;
titleBar.Controls.Add(titleLabel);
titleBar.Controls.Add(closeButton);
```

Add `Controls.Add(titleBar);` after adding the search panel so DockStyle.Top places the title bar above it. Add the drag handler:

```csharp
private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
{
    if (e.Button != MouseButtons.Left) return;
    ReleaseCapture();
    SendMessage(Handle, WmNcLButtonDown, HtCaption, nint.Zero);
}
```

Remove `Deactivate += PopupForm_Deactivate;`, delete `PopupForm_Deactivate`, and remove the unused `childDialogOpen` field and `ChildDialogOpen` property.

- [ ] **Step 4: Remove obsolete dialog guard calls**

In `TrayApplicationContext.ShowSettings` and `TrayApplicationContext.ClearHistory`, remove the assignments to `popup.ChildDialogOpen` and their corresponding `finally` blocks. Keep dialog disposal, error logging, and message boxes unchanged.

- [ ] **Step 5: Run the targeted tests and verify green**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm"
```

Expected: all popup tests pass.

- [ ] **Step 6: Write the failing taskbar visibility test**

In `PopupForm_UsesApprovedWindowBehavior`, change the existing taskbar assertion to:

```csharp
Assert.True(form.ShowInTaskbar);
```

- [ ] **Step 7: Run the taskbar test and verify the red state**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm_UsesApprovedWindowBehavior"
```

Expected: FAIL because `PopupForm` still sets `ShowInTaskbar = false`.

- [ ] **Step 8: Enable the visible taskbar entry**

In the `PopupForm` constructor, replace:

```csharp
ShowInTaskbar = false;
```

with:

```csharp
ShowInTaskbar = true;
Text = "剪贴板历史";
```

The form remains hidden through `Hide()`, so WinForms removes the taskbar entry whenever the popup closes without exiting the tray process.

Subscribe to the form's `Activated` event and refresh the timeline so returning through the taskbar shows clipboard entries captured while another application was active:

```csharp
Activated += PopupForm_Activated;

private void PopupForm_Activated(object? sender, EventArgs e) => _ = RefreshAsync(append: false);
```

- [ ] **Step 9: Run the targeted taskbar test and verify green**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm"
```

Expected: all popup tests pass.

- [ ] **Step 10: Run complete verification**

Run:

```powershell
dotnet test LocalClipboard.slnx --no-restore
dotnet build LocalClipboard.slnx --no-restore -warnaserror
git diff --check
```

Expected: 0 failed tests, 0 build warnings, 0 build errors, and no whitespace errors.

- [ ] **Step 11: Perform desktop acceptance**

Start the application and verify:

1. Alt+V opens the popup.
2. Clicking another application does not hide it.
3. Dragging the title bar moves the popup.
4. Clicking × hides it without exiting the tray application.
5. Esc hides it.
6. Restoring an entry hides it.
7. Alt+V opens it again.
8. The visible popup appears as “剪贴板历史” in the taskbar and can be reactivated from there.
9. Hiding the popup removes its taskbar entry while leaving the tray icon running.

- [ ] **Step 12: Commit the completed Task 12 work**

```powershell
git add docs src tests
git commit -m "feat: compose tray clipboard application"
```
