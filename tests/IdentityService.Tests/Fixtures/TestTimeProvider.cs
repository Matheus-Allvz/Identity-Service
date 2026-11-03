namespace IdentityService.Tests.Fixtures;

public class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset? initial = null)
    {
        _utcNow = initial ?? new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
