using System;

namespace FinamApiGrpc.Models.Orders;

/// <summary>
/// Запрос на выставление биржевой заявки
/// </summary>
public class OrderRequest
{
    /// <summary>
    /// Символ инструмента (например, "SBER@MISX")
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Количество в шт.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Сторона (Buy/Sell)
    /// </summary>
    public OrderSide Side { get; set; }

    /// <summary>
    /// Тип заявки (Market, Limit и т.д.)
    /// </summary>
    public OrderType Type { get; set; }

    /// <summary>
    /// Срок действия заявки
    /// </summary>
    public TimeInForce TimeInForce { get; set; }

    /// <summary>
    /// Цена для лимитных и стоп-лимитных заявок
    /// </summary>
    public decimal? LimitPrice { get; set; }

    /// <summary>
    /// Цена срабатывания для стоп-заявок
    /// </summary>
    public decimal? StopPrice { get; set; }

    /// <summary>
    /// Условие срабатывания стоп-заявки
    /// </summary>
    public StopCondition? StopCondition { get; set; }

    /// <summary>
    /// Уникальный идентификатор заявки (опционально)
    /// </summary>
    public string? ClientOrderId { get; set; }

    /// <summary>
    /// Комментарий к заявке
    /// </summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Состояние заявки (ответ от сервера)
/// </summary>
public class OrderResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string? ExecId { get; set; }
    public OrderStatus Status { get; set; }
    public OrderRequest Order { get; set; } = new();
    public DateTime? TransactAt { get; set; }
    public DateTime? AcceptAt { get; set; }
    public DateTime? WithdrawAt { get; set; }
    public decimal InitialQuantity { get; set; }
    public decimal ExecutedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
}
