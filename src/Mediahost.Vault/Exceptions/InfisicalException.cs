namespace Mediahost.Vault.Exceptions;

public sealed class InfisicalException : Exception
{
    public int StatusCode { get; }

    public InfisicalException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public InfisicalException(string message, int statusCode, Exception inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
