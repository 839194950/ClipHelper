using System.Windows.Forms;
using LocalClipboard.Infrastructure.Settings;
using LocalClipboard.Infrastructure.Windows;

namespace LocalClipboard.App.IntegrationTests.Windows;

[Collection(nameof(ClipboardCollection))]
public sealed class GlobalHotkeyIntegrationTests
{
    [Fact]
    public Task Register_RejectsACombinationAlreadyOwnedByAnotherWindow() => StaTest.RunAsync(() =>
    {
        using var first = new GlobalHotkeyManager();
        using var second = new GlobalHotkeyManager();
        Keys key = RegisterFirstAvailable(first, Keys.F12, Keys.F14, Keys.F16, Keys.F18, Keys.F20, Keys.F22, Keys.F24);

        Action register = () => second.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, key);
        Assert.Throws<InvalidOperationException>(register);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Unregister_AllowsTheCombinationToBeRegisteredAgain() => StaTest.RunAsync(() =>
    {
        using var first = new GlobalHotkeyManager();
        Keys key = RegisterFirstAvailable(first, Keys.F11, Keys.F13, Keys.F15, Keys.F17, Keys.F19, Keys.F21, Keys.F23);
        first.Unregister();

        using var second = new GlobalHotkeyManager();
        second.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, key);
        return Task.CompletedTask;
    });

    private static Keys RegisterFirstAvailable(GlobalHotkeyManager manager, params Keys[] candidates)
    {
        foreach (Keys key in candidates)
        {
            try
            {
                manager.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, key);
                return key;
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException("No integration-test hotkey combination was available.");
    }
}
