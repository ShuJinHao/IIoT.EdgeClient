namespace IIoT.Edge.Application.Common.Persistence;

public sealed class PersistenceAccessException : Exception
{
    public PersistenceAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
