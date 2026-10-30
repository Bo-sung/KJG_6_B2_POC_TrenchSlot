using System;

public interface ILogUtcClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class LogUtcClock : ILogUtcClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class LogFixedClock
{
    public double ElapsedSeconds { get; private set; }
    public double? FixedDeltaTime { get; private set; }
    public long TickCount { get; private set; }

    public LogFixedClock(double? _initialFixedDeltaTime = null)
    {
        if (_initialFixedDeltaTime.HasValue)
        {
            Validate(_initialFixedDeltaTime.Value);
            FixedDeltaTime = _initialFixedDeltaTime;
        }
    }

    public void Advance(double _fixedDeltaTime)
    {
        Validate(_fixedDeltaTime);
        if (double.IsInfinity(ElapsedSeconds + _fixedDeltaTime))
        {
            throw new ArgumentOutOfRangeException(nameof(_fixedDeltaTime));
        }
        FixedDeltaTime = _fixedDeltaTime;
        ElapsedSeconds += _fixedDeltaTime;
        TickCount = checked(TickCount + 1);
    }

    private static void Validate(double _fixedDeltaTime)
    {
        if (double.IsNaN(_fixedDeltaTime) || double.IsInfinity(_fixedDeltaTime) || _fixedDeltaTime <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(_fixedDeltaTime));
        }
    }
}
