namespace IdentityService.Acceptance.Infrastructure;

public sealed class RelogioTeste : TimeProvider
{
    private DateTimeOffset _agora = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Avancar(TimeSpan d) => _agora += d;

    public void Repor()
    {
        _agora = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public void Definir(DateTimeOffset instante)
    {
        _agora = DateTimeOffset.FromUnixTimeSeconds(instante.ToUnixTimeSeconds());
    }

    public void DefinirUnix(long unixSeconds)
    {
        _agora = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    }
}
