namespace CsdiStockHold.Domain;

public enum DomainErrorCode
{
    InvalidCustomerType,
    InvalidProductName,
    InvalidStockQuantity,
    InvalidQuantity,
    InvalidIdempotencyKey,
    CustomerLimitExceeded,
    InsufficientStock,
    CustomerNotFound,
    ProductNotFound,
    IdempotencyKeyConflict,
}
