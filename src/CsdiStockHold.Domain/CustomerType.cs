namespace CsdiStockHold.Domain;

/// <summary>
/// The two customer types defined by the specification.
/// Numbering starts at 1 so that the default value (0) is not a valid type.
/// </summary>
public enum CustomerType
{
    Regular = 1,
    Special = 2,
}
