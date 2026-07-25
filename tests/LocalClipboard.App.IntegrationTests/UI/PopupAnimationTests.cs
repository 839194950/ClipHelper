using LocalClipboard.App.UI;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class PopupAnimationTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    public void EaseOut_PreservesEndpoints(float input, float expected)
    {
        Assert.Equal(expected, PopupAnimation.EaseOut(input), precision: 4);
    }

    [Fact]
    public void Transition_CompletesAtTarget()
    {
        var transition = new PopupAnimation(TimeSpan.FromMilliseconds(120));

        Assert.False(transition.Advance(TimeSpan.Zero));
        Assert.True(transition.Advance(TimeSpan.FromMilliseconds(120)));
        Assert.Equal(1f, transition.Progress, precision: 4);
    }
}
