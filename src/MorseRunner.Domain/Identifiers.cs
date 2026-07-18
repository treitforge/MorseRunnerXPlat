namespace MorseRunner.Domain;

public readonly record struct EngineId(Guid Value)
{
    public static EngineId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct RequestId(Guid Value)
{
    public static RequestId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct ClientId(string Value)
{
    public override string ToString() => Value;
}
