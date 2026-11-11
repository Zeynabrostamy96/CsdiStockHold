namespace CsdiStockHold.Domain;

/// <summary>
/// Raised when a business rule of the domain is violated.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(DomainErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainErrorCode Code { get; }
}
