using System;

namespace FinamApiGrpc.Models.Orders;

/// <summary>
/// Сторона заявки
/// </summary>
public enum OrderSide
{
    Unspecified = 0,
    Buy = 1,
    Sell = 2
}

/// <summary>
/// Тип заявки
/// </summary>
public enum OrderType
{
    Unspecified = 0,
    Market = 1,
    Limit = 2,
    Stop = 3,
    StopLimit = 4,
    MultiLeg = 5
}

/// <summary>
/// Срок действия заявки
/// </summary>
public enum TimeInForce
{
    Unspecified = 0,
    Day = 1,
    GoodTillCancel = 2,
    GoodTillCrossing = 3,
    Ext = 4,
    OnOpen = 5,
    OnClose = 6,
    Ioc = 7,
    Fok = 8
}

/// <summary>
/// Условие срабатывания стоп заявки
/// </summary>
public enum StopCondition
{
    Unspecified = 0,
    LastUp = 1,
    LastDown = 2
}

/// <summary>
/// Статус заявки
/// </summary>
public enum OrderStatus
{
    Unspecified = 0,
    New = 1,
    PartiallyFilled = 2,
    Filled = 3,
    DoneForDay = 4,
    Canceled = 5,
    Replaced = 6,
    PendingCancel = 7,
    Rejected = 9,
    Suspended = 10,
    PendingNew = 11,
    Expired = 13,
    Failed = 16,
    Forwarding = 17,
    Wait = 18,
    DeniedByBroker = 19,
    RejectedByExchange = 20,
    Watching = 21,
    Executed = 22,
    Disabled = 23,
    LinkWait = 24,
    SlGuardTime = 27,
    SlExecuted = 28,
    SlForwarding = 29,
    TpGuardTime = 30,
    TpExecuted = 31,
    TpCorrection = 32,
    TpForwarding = 33,
    TpCorrGuardTime = 34
}
