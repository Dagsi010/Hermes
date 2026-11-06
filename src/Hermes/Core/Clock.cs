namespace Hermes.Core;

/// <summary>
/// The app's notion of "now". A --fake-now argument shifts it so the overlay
/// can be checked at any time of day; time keeps flowing from that point.
/// </summary>
public static class Clock
{
    static TimeSpan _offset = TimeSpan.Zero;

    public static DateTime Now => DateTime.Now + _offset;

    public static void Fake(DateTime now) => _offset = now - DateTime.Now;
}
