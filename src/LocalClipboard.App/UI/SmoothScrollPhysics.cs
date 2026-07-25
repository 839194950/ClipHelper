namespace LocalClipboard.App.UI;

internal sealed class SmoothScrollPhysics
{
    private const double AngularFrequency = 24d;
    private double target;

    internal double Position { get; private set; }
    internal double Velocity { get; private set; }
    internal double Target => target;
    internal bool IsSettled => Math.Abs(Position - target) < 0.25d && Math.Abs(Velocity) < 1d;

    internal void SetPosition(double position)
    {
        Position = position;
        Velocity = 0d;
    }

    internal void SetTarget(double value) => target = value;

    internal void Advance(TimeSpan elapsed)
    {
        double seconds = Math.Clamp(elapsed.TotalSeconds, 0d, 0.1d);
        if (seconds <= 0d || IsSettled)
        {
            if (IsSettled) SnapToTarget();
            return;
        }

        double displacement = Position - target;
        double decay = Math.Exp(-AngularFrequency * seconds);
        double nextDisplacement = (displacement + (Velocity + (AngularFrequency * displacement)) * seconds) * decay;
        double nextVelocity = (Velocity - AngularFrequency * (Velocity + (AngularFrequency * displacement)) * seconds) * decay;
        Position = target + nextDisplacement;
        Velocity = nextVelocity;

        if (IsSettled) SnapToTarget();
    }

    private void SnapToTarget()
    {
        Position = target;
        Velocity = 0d;
    }
}
