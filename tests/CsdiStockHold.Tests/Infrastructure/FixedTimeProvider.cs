namespace CsdiStockHold.Tests.Infrastructure;

/// <summary>
/// A <see cref="TimeProvider"/> whose current UTC time only changes when a test says so,
/// which makes reservation expiry deterministic. Safe to read from several threads.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private long _utcTicks = utcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref _utcTicks), TimeSpan.Zero);

    public void SetUtcNow(DateTimeOffset utcNow) => Volatile.Write(ref _utcTicks, utcNow.UtcTicks);

    public void Advance(TimeSpan duration) => Interlocked.Add(ref _utcTicks, duration.Ticks);
}
