namespace Domain.Connections;

public sealed class LazyConnection<T>(Func<T> factory)
    where T : class
{
    private readonly Lock _gate = new();

    public T Value
    {
        get
        {
            var value = Volatile.Read(ref field);

            if (value is not null)
            {
                return value;
            }

            lock (_gate)
            {
                field ??= factory();

                return field;
            }
        }
    }
}
