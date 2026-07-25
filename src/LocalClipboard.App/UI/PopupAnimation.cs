namespace LocalClipboard.App.UI;

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
        if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta));
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
