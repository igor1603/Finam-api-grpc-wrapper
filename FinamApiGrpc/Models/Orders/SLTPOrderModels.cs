using System;

namespace FinamApiGrpc.Models.Orders;

/// <summary>
/// Запрос на выставление SL/TP заявки
/// </summary>
public class SLTPOrderRequest
{
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public decimal QuantitySl { get; set; }
    public decimal SlPrice { get; set; }
    public decimal? LimitPrice { get; set; }
    public decimal QuantityTp { get; set; }
    public decimal TpPrice { get; set; }
    public decimal? TpGuardSpread { get; set; }
    public TPSpreadMeasure TpSpreadMeasure { get; set; }
    public string? ClientOrderId { get; set; }
    public ValidBefore ValidBefore { get; set; }
    public DateTime? ValidExpiryTime { get; set; }
    public string? Comment { get; set; }
}

public enum TPSpreadMeasure
{
    Undefined = 0,
    Value = 1,
    Percent = 2
}

public enum ValidBefore
{
    Unspecified = 0,
    EndOfDay = 1,
    GoodTillCancel = 2,
    GoodTillDate = 3
}
