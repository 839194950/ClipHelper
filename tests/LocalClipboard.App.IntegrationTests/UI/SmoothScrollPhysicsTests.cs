using LocalClipboard.App.UI;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class SmoothScrollPhysicsTests
{
    [Fact]
    public void RetargetingPreservesMotionInsteadOfRestartingAnEasingCurve()
    {
        var physics = new SmoothScrollPhysics();
        physics.SetPosition(0);
        physics.SetTarget(200);

        physics.Advance(TimeSpan.FromMilliseconds(32));
        double beforeRetarget = physics.Position;
        physics.SetTarget(400);
        physics.Advance(TimeSpan.FromMilliseconds(16));

        Assert.True(physics.Position > beforeRetarget);
        Assert.True(physics.Velocity > 0);
    }

    [Fact]
    public void EqualElapsedTimeProducesEquivalentPositionAcrossFrameSizes()
    {
        var shortFrames = new SmoothScrollPhysics();
        var longFrame = new SmoothScrollPhysics();
        shortFrames.SetTarget(400);
        longFrame.SetTarget(400);

        shortFrames.Advance(TimeSpan.FromMilliseconds(16));
        shortFrames.Advance(TimeSpan.FromMilliseconds(16));
        shortFrames.Advance(TimeSpan.FromMilliseconds(16));
        longFrame.Advance(TimeSpan.FromMilliseconds(48));

        Assert.InRange(Math.Abs(shortFrames.Position - longFrame.Position), 0, 0.01);
    }
}
