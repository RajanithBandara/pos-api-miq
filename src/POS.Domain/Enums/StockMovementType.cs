namespace POS.Domain.Enums;

public enum StockMovementType
{
    Sale = 1,
    Return = 2,
    Purchase = 3,
    AdjustmentIn = 4,
    AdjustmentOut = 5,
    TransferIn = 6,
    TransferOut = 7,
    Damaged = 8,
    Expired = 9,
    OpeningStock = 10
}
