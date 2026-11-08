using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinamApiGrpc.Models.Orders;
using FinamApiGrpc.ServicesClients;
using FinamApiGrpc.Streams;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tradeapi.V1;
using Grpc.Tradeapi.V1.Orders;
using Microsoft.Extensions.Logging;

using ModelsOrders = FinamApiGrpc.Models.Orders;
using ProtoOrders = Grpc.Tradeapi.V1.Orders;

namespace FinamApiGrpc.ServicesClients;

/// <summary>
/// Клиент сервиса ордеров.
/// </summary>
public class OrdersClient : OrdersService.OrdersServiceClient, IDisposable
{
    #region Поля

    private readonly ILogger _logger;

    // Стрим заявок
    private CancellationTokenSource? _ordersStreamCts;
    private Task? _ordersSubscriptionTask;
    private Action<List<OrderResponse>>? _ordersSubscriptionHandler;
    private readonly ServerStreamingLoop<SubscribeOrdersRequest, SubscribeOrdersResponse> _ordersStreamLoop;

    // Стрим сделок
    private CancellationTokenSource? _tradesStreamCts;
    private Task? _tradesSubscriptionTask;
    private Action<List<OrderResponse>>? _tradesSubscriptionHandler;
    private readonly ServerStreamingLoop<SubscribeTradesRequest, SubscribeTradesResponse> _tradesStreamLoop;

    #endregion

    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="invoker">CallInvoker канала gRPC.</param>
    /// <param name="logger">Логгер библиотеки.</param>
    public OrdersClient(CallInvoker invoker, ILogger? logger = null) : base(invoker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Настройка стрима заявок
        _ordersStreamLoop = new ServerStreamingLoop<SubscribeOrdersRequest, SubscribeOrdersResponse>(
            new StreamReconnectPolicy
            {
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 65
            },
            runForever: true,
            logger: message =>
            {
                _logger.LogDebug("[ORDERS] {Message}", message);
            });

        // Настройка стрима сделок
        _tradesStreamLoop = new ServerStreamingLoop<SubscribeTradesRequest, SubscribeTradesResponse>(
            new StreamReconnectPolicy
            {
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 65
            },
            runForever: true,
            logger: message =>
            {
                _logger.LogDebug("[ORDERS] {Message}", message);
            });
    }

    #region Методы API

    /// <summary>
    /// Выставление биржевой заявки.
    /// </summary>
    /// <param name="accountId">Идентификатор аккаунта.</param>
    /// <param name="request">Запрос на выставление заявки (DTO).</param>
    /// <returns>Состояние созданной заявки (DTO).</returns>
    /// <exception cref="ArgumentNullException">Генерируется, когда request равен null.</exception>
    /// <exception cref="InvalidOperationException">Генерируется, когда accountId пуст.</exception>
    public async Task<OrderResponse> PlaceOrder(string accountId, OrderRequest request)
    {
        _logger.LogDebug("[ORDERS] Выставление заявки для аккаунта: {AccountId}, символ: {Symbol}",
            accountId, request.Symbol);
        

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                "Невозможно выставить заявку: accountId пуст или не инициализирован.");
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var protoOrder = MapToProtoOrder(accountId, request);
        var response = await base.PlaceOrderAsync(protoOrder);

        _logger.LogDebug("[ORDERS] Заявка создана. OrderId: {OrderId}, Status: {Status}",
            response.OrderId, response.Status);

        return MapFromProtoOrderState(response);
    }
    /// <summary>
    /// Отмена биржевой заявки.
    /// </summary>
    /// <param name="accountId">Идентификатор аккаунта.</param>
    /// <param name="orderId">Идентификатор заявки.</param>
    /// <returns>Состояние отменённой заявки (DTO).</returns>
    /// <exception cref="ArgumentNullException">Генерируется, когда orderId равен null.</exception>
    /// <exception cref="InvalidOperationException">Генерируется, когда accountId пуст.</exception>
    public async Task<OrderResponse> CancelOrder(string accountId, string orderId)
    {
        _logger.LogDebug("[ORDERS] Отмена заявки. AccountId: {AccountId}, OrderId: {OrderId}",
            accountId, orderId);

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                "Невозможно отменить заявку: accountId пуст или не инициализирован.");
        }

        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new InvalidOperationException(
                "Невозможно отменить заявку: orderId пуст или не инициализирован.");
        }

        var request = new CancelOrderRequest
        {
            AccountId = accountId,
            OrderId = orderId
        };

        var response = await base.CancelOrderAsync(request);

        _logger.LogDebug("[ORDERS] Заявка отменена. OrderId: {OrderId}, Status: {Status}",
            response.OrderId, response.Status);

        return MapFromProtoOrderState(response);
    }
    /// <summary>
    /// Получение списка заявок для аккаунта.
    /// </summary>
    /// <param name="accountId">Идентификатор аккаунта.</param>
    /// <returns>Список заявок (DTO).</returns>
    /// <exception cref="ArgumentNullException">Генерируется, когда accountId равен null.</exception>
    /// <exception cref="InvalidOperationException">Генерируется, когда accountId пуст.</exception>
    public async Task<List<OrderResponse>> GetOrders(string accountId)
    {
        _logger.LogDebug("[ORDERS] Получение списка заявок для аккаунта: {AccountId}", accountId);

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                "Невозможно получить список заявок: accountId пуст или не инициализирован.");
        }

        var request = new OrdersRequest { AccountId = accountId };
        var response = await base.GetOrdersAsync(request);

        _logger.LogDebug("[ORDERS] Получено {Count} заявок для аккаунта {AccountId}",
            response.Orders.Count, accountId);

        return response.Orders.Select(MapFromProtoOrderState).ToList();
    }
    /// <summary>
    /// Получение информации о конкретной заявке.
    /// </summary>
    /// <param name="accountId">Идентификатор аккаунта.</param>
    /// <param name="orderId">Идентификатор заявки.</param>
    /// <returns>Информация о заявке (DTO).</returns>
    /// <exception cref="ArgumentNullException">Генерируется, когда orderId равен null.</exception>
    /// <exception cref="InvalidOperationException">Генерируется, когда accountId или orderId пуст.</exception>
    public async Task<OrderResponse> GetOrder(string accountId, string orderId)
    {
        _logger.LogDebug("[ORDERS] Получение информации о заявке. AccountId: {AccountId}, OrderId: {OrderId}",
            accountId, orderId);

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                "Невозможно получить информацию о заявке: accountId пуст или не инициализирован.");
        }

        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new InvalidOperationException(
                "Невозможно получить информацию о заявке: orderId пуст или не инициализирован.");
        }

        var request = new GetOrderRequest
        {
            AccountId = accountId,
            OrderId = orderId
        };

        var response = await base.GetOrderAsync(request);

        _logger.LogDebug("[ORDERS] Получена информация о заявке. OrderId: {OrderId}, Status: {Status}",
            response.OrderId, response.Status);

        return MapFromProtoOrderState(response);
    }
    /// <summary>
    /// Выставление SL/TP заявки.
    /// </summary>
    /// <param name="accountId">Идентификатор аккаунта.</param>
    /// <param name="request">Запрос на выставление SL/TP заявки (DTO).</param>
    /// <returns>Состояние созданной заявки (DTO).</returns>
    /// <exception cref="ArgumentNullException">Генерируется, когда request равен null.</exception>
    /// <exception cref="InvalidOperationException">Генерируется, когда accountId пуст.</exception>
    public async Task<OrderResponse> PlaceSLTPOrder(string accountId, SLTPOrderRequest request)
    {
        _logger.LogDebug("[ORDERS] Выставление SL/TP заявки для аккаунта: {AccountId}, символ: {Symbol}",
            accountId, request.Symbol);

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                "Невозможно выставить SL/TP заявку: accountId пуст или не инициализирован.");
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var protoSltp = MapToProtoSLTPOrder(accountId, request);
        var response = await base.PlaceSLTPOrderAsync(protoSltp);

        _logger.LogDebug("[ORDERS] SL/TP заявка создана. OrderId: {OrderId}, Status: {Status}",
            response.OrderId, response.Status);

        return MapFromProtoOrderState(response);
    }
    /// <summary>
    /// Подписка на обновления заявок.
    /// </summary>
    /// <param name="accountId">Идентификатор аккаунта.</param>
    /// <param name="onOrderUpdate">Обработчик обновлений заявок.</param>
    /// <param name="cancellationToken">Токен отмены подписки.</param>
    /// <returns>Задача, завершающаяся сразу после запуска подписки.</returns>
    /// <exception cref="ArgumentNullException">Генерируется, когда onOrderUpdate равен null.</exception>
    /// <exception cref="InvalidOperationException">Генерируется, когда accountId пуст.</exception>
    public Task SubscribeOrders(string accountId, Action<List<OrderResponse>> onOrderUpdate, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[ORDERS] Запуск подписки на обновления заявок для аккаунта: {AccountId}", accountId);

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                "Невозможно подписаться на обновления заявок: accountId пуст или не инициализирован.");
        }

        if (onOrderUpdate is null)
        {
            throw new ArgumentNullException(nameof(onOrderUpdate));
        }

        // Защита от дублирования подписки
        if (_ordersSubscriptionTask is { IsCompleted: false })
        {
            _logger.LogWarning("[ORDERS] Подписка на заявки уже активна. Возвращаем Task.CompletedTask.");
            return Task.CompletedTask;
        }

        _ordersStreamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ordersSubscriptionHandler = onOrderUpdate;
        _ordersSubscriptionTask = Task.Run(() => StartSubscribeOrders(accountId, _ordersStreamCts.Token));

        _logger.LogDebug("[ORDERS] Подписка на заявки активирована для аккаунта: {AccountId}", accountId);

        return Task.CompletedTask;
    }
    /// <summary>
    /// Отписка от обновлений заявок.
    /// </summary>
    /// <returns>Задача, завершающаяся после остановки подписки.</returns>
    public async Task UnsubscribeOrders()
    {
        _logger.LogDebug("[ORDERS] Остановка подписки на обновления заявок.");

        _ordersStreamCts?.Cancel();
        _ordersStreamCts?.Dispose();

        if (_ordersSubscriptionTask != null)
        {
            await _ordersSubscriptionTask.ConfigureAwait(false);
        }

        _ordersStreamCts = null;
        _ordersSubscriptionHandler = null;
        _ordersSubscriptionTask = null;
    }
    /// <summary>
    /// Подписка на обновления сделок.
    /// </summary>
    /// <param name="accountId">Идентификатор аккаунта.</param>
    /// <param name="onTradeUpdate">Обработчик обновлений сделок.</param>
    /// <param name="cancellationToken">Токен отмены подписки.</param>
    /// <returns>Задача, завершающаяся сразу после запуска подписки.</returns>
    /// <exception cref="ArgumentNullException">Генерируется, когда onTradeUpdate равен null.</exception>
    /// <exception cref="InvalidOperationException">Генерируется, когда accountId пуст.</exception>
    public Task SubscribeTrades(string accountId, Action<List<OrderResponse>> onTradeUpdate, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[ORDERS] Запуск подписки на сделки для аккаунта: {AccountId}", accountId);

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException(
                "Невозможно подписаться на сделки: accountId пуст или не инициализирован.");
        }

        if (onTradeUpdate is null)
        {
            throw new ArgumentNullException(nameof(onTradeUpdate));
        }

        // Защита от дублирования подписки
        if (_tradesSubscriptionTask is { IsCompleted: false })
        {
            _logger.LogWarning("[ORDERS] Подписка на сделки уже активна. Возвращаем Task.CompletedTask.");
            return Task.CompletedTask;
        }

        _tradesStreamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _tradesSubscriptionHandler = onTradeUpdate;
        _tradesSubscriptionTask = Task.Run(() => StartSubscribeTrades(accountId, _tradesStreamCts.Token));

        _logger.LogDebug("[ORDERS] Подписка на сделки активирована для аккаунта: {AccountId}", accountId);

        return Task.CompletedTask;
    }
    /// <summary>
    /// Отписка от обновлений сделок.
    /// </summary>
    /// <returns>Задача, завершающаяся после остановки подписки.</returns>
    public async Task UnsubscribeTrades()
    {
        _logger.LogDebug("[ORDERS] Остановка подписки на сделки.");

        _tradesStreamCts?.Cancel();
        _tradesStreamCts?.Dispose();

        if (_tradesSubscriptionTask != null)
        {
            await _tradesSubscriptionTask.ConfigureAwait(false);
        }

        _tradesStreamCts = null;
        _tradesSubscriptionHandler = null;
        _tradesSubscriptionTask = null;
    }

    #endregion

    /// <summary>
    /// Освобождение ресурсов клиента.
    /// </summary>
    public void Dispose()
    {
        _logger.LogDebug("[ORDERS] Освобождение ресурсов OrdersClient.");

        // Остановка стрима заявок
        _ordersStreamCts?.Cancel();
        _ordersStreamCts?.Dispose();
        if (_ordersSubscriptionTask != null)
        {
            _ordersSubscriptionTask.Wait();
        }
        _ordersStreamCts = null;
        _ordersSubscriptionHandler = null;
        _ordersSubscriptionTask = null;

        // Остановка стрима сделок
        _tradesStreamCts?.Cancel();
        _tradesStreamCts?.Dispose();
        if (_tradesSubscriptionTask != null)
        {
            _tradesSubscriptionTask.Wait();
        }
        _tradesStreamCts = null;
        _tradesSubscriptionHandler = null;
        _tradesSubscriptionTask = null;

        GC.SuppressFinalize(this);
    }

    #region Внутренние методы

    /// <summary>
    /// Запуск стрима заявок.
    /// </summary>
    private async Task StartSubscribeOrders(string accountId, CancellationToken cancellationToken)
    {
        var request = new SubscribeOrdersRequest { AccountId = accountId };

        try
        {
            await _ordersStreamLoop.RunAsync(
                request,
                (req, ct) => base.SubscribeOrders(req, cancellationToken: ct),
                (response, ct) =>
                {
                    if (response != null && response.Orders.Count > 0)
                    {
                        _ordersSubscriptionHandler?.Invoke(response.Orders.Select(MapFromProtoOrderState).ToList());
                    }

                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ожидаемое отмена по токену — не логим как ошибка
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            _logger.LogError(ex, "[ORDERS] Ошибка в стреме заявок для аккаунта {AccountId}", accountId);
            throw;
        }
    }
    /// <summary>
    /// Запуск стрима сделок.
    /// </summary>
    private async Task StartSubscribeTrades(string accountId, CancellationToken cancellationToken)
    {
        var request = new SubscribeTradesRequest { AccountId = accountId };

        try
        {
            await _tradesStreamLoop.RunAsync(
                request,
                (req, ct) => base.SubscribeTrades(req, cancellationToken: ct),
                (response, ct) =>
                {
                    if (response != null && response.Trades.Count > 0)
                    {
                        _tradesSubscriptionHandler?.Invoke(response.Trades.Select(MapFromProtoTradeState).ToList());
                    }

                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ожидаемое отмена по токену — не логим как ошибка
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            _logger.LogError(ex, "[ORDERS] Ошибка в стреме сделок для аккаунта {AccountId}", accountId);
            throw;
        }
    }
    /// <summary>
    /// Преобразование запроса в proto-сообщение.
    /// </summary>
    private Order MapToProtoOrder(string accountId, OrderRequest request)
    {
        return new Order
        {
            AccountId = accountId,
            Symbol = request.Symbol,
            Quantity = new Google.Type.Decimal { Value = request.Quantity.ToString(CultureInfo.InvariantCulture) },
            Side = ToProtoSide(request.Side),
            Type = ToProtoOrderType(request.Type),
            TimeInForce = ToProtoTimeInForce(request.TimeInForce),
            LimitPrice = request.LimitPrice.HasValue ? new Google.Type.Decimal { Value = request.LimitPrice.Value.ToString(CultureInfo.InvariantCulture) } : null,
            StopPrice = request.StopPrice.HasValue ? new Google.Type.Decimal { Value = request.StopPrice.Value.ToString(CultureInfo.InvariantCulture) } : null,
            StopCondition = request.StopCondition.HasValue
                ? ToProtoStopCondition(request.StopCondition.Value)
                : ProtoOrders.StopCondition.Unspecified,
            ClientOrderId = request.ClientOrderId ?? "",
            Comment = request.Comment ?? ""
        };
    }
    /// <summary>
    /// Преобразование запроса SL/TP в proto-сообщение.
    /// </summary>
    private SLTPOrder MapToProtoSLTPOrder(string accountId, SLTPOrderRequest request)
    {
        return new SLTPOrder
        {
            AccountId = accountId,
            Symbol = request.Symbol,
            Side = ToProtoSide(request.Side),
            QuantitySl = new Google.Type.Decimal { Value = request.QuantitySl.ToString(CultureInfo.InvariantCulture) },
            SlPrice = new Google.Type.Decimal { Value = request.SlPrice.ToString(CultureInfo.InvariantCulture) },
            LimitPrice = request.LimitPrice.HasValue ? new Google.Type.Decimal { Value = request.LimitPrice.Value.ToString(CultureInfo.InvariantCulture) } : null,
            QuantityTp = new Google.Type.Decimal { Value = request.QuantityTp.ToString(CultureInfo.InvariantCulture) },
            TpPrice = new Google.Type.Decimal { Value = request.TpPrice.ToString(CultureInfo.InvariantCulture) },
            TpGuardSpread = request.TpGuardSpread.HasValue ? new Google.Type.Decimal { Value = request.TpGuardSpread.Value.ToString(CultureInfo.InvariantCulture) } : null,
            TpSpreadMeasure = (Grpc.Tradeapi.V1.Orders.TPSpreadMeasure)Convert.ToInt32(request.TpSpreadMeasure),
            ClientOrderId = request.ClientOrderId ?? "",
            ValidBefore = (Grpc.Tradeapi.V1.Orders.ValidBefore)Convert.ToInt32(request.ValidBefore),
            ValidExpiryTime = request.ValidExpiryTime != null
                ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(request.ValidExpiryTime.Value, DateTimeKind.Utc))
                : null,
            Comment = request.Comment ?? ""
        };
    }
    /// <summary>
    /// Преобразование состояния заявки из proto в DTO.
    /// </summary>
    private OrderResponse MapFromProtoOrderState(OrderState state)
    {
        //if (state == null) return null;

        var orderStatus = FromProtoOrderStatus(state.Status);
        var orderType = FromProtoOrderType(state.Order.Type);
        var timeInForce = FromProtoTimeInForce(state.Order.TimeInForce);
        var stopCondition = state.Order.StopCondition switch
        {
            ProtoOrders.StopCondition.LastUp => ModelsOrders.StopCondition.LastUp,
            ProtoOrders.StopCondition.LastDown => ModelsOrders.StopCondition.LastDown,
            _ => default(ModelsOrders.StopCondition?),
        };

        return new OrderResponse
        {
            OrderId = state.OrderId,
            ExecId = state.ExecId,
            Status = orderStatus,
            TransactAt = state.TransactAt?.ToDateTime(),
            AcceptAt = state.AcceptAt?.ToDateTime(),
            WithdrawAt = state.WithdrawAt?.ToDateTime(),
            InitialQuantity = ParseDecimal(state.InitialQuantity),
            ExecutedQuantity = ParseDecimal(state.ExecutedQuantity),
            RemainingQuantity = ParseDecimal(state.RemainingQuantity),
            Order = state.Order != null ? new OrderRequest
            {
                Symbol = state.Order.Symbol,
                Quantity = ParseDecimal(state.Order.Quantity),
                Side = FromProtoSide(state.Order.Side),
                Type = orderType,
                TimeInForce = timeInForce,
                LimitPrice = ParseDecimal(state.Order.LimitPrice),
                StopPrice = ParseDecimal(state.Order.StopPrice),
                StopCondition = stopCondition,
                ClientOrderId = state.Order.ClientOrderId,
                Comment = state.Order.Comment
            } : new OrderRequest()
        };
    }
    /// <summary>
    /// Преобразование состояния сделки из proto в DTO.
    /// </summary>
    private OrderResponse MapFromProtoTradeState(AccountTrade state)
    {
        //if (state == null) return null;

        // AccountTrade имеет: trade_id, symbol, price, size, side, timestamp, order_id, account_id, comment, currency
        // Сделка — это всегда market order, исполняется в день выставления
        var orderSide = OrderSide.Buy; // Сделка — это всегда buy (из proto)

        return new OrderResponse
        {
            OrderId = state.OrderId,
            ExecId = state.TradeId,
            Status = ModelsOrders.OrderStatus.Filled, // Сделка всегда исполнена
            TransactAt = state.Timestamp?.ToDateTime(),
            AcceptAt = null,
            WithdrawAt = null,
            InitialQuantity = ParseDecimal(state.Size),
            ExecutedQuantity = ParseDecimal(state.Size),
            RemainingQuantity = 0m,
            Order = new OrderRequest
            {
                Symbol = state.Symbol,
                Quantity = ParseDecimal(state.Size),
                Side = orderSide,
                Type = ModelsOrders.OrderType.Market, // Сделка — это market order
                TimeInForce = ModelsOrders.TimeInForce.Day, // Исполняется в день выставления
                LimitPrice = null,
                StopPrice = null,
                StopCondition = null,
                ClientOrderId = state.Comment,
                Comment = state.Comment
            }
        };
    }
    /// <summary>
    /// Преобразование Decimal из proto в decimal.
    /// </summary>
    
    private decimal ParseDecimal(Google.Type.Decimal d)
    {
        if (d == null || string.IsNullOrEmpty(d.Value)) return 0;
        return decimal.Parse(d.Value, CultureInfo.InvariantCulture);
    }
    private static Grpc.Tradeapi.V1.Side ToProtoSide(OrderSide side) => side switch
    {
        OrderSide.Unspecified => Grpc.Tradeapi.V1.Side.Unspecified,
        OrderSide.Buy => Grpc.Tradeapi.V1.Side.Buy,
        OrderSide.Sell => Grpc.Tradeapi.V1.Side.Sell,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
    };
    private static OrderSide FromProtoSide(Grpc.Tradeapi.V1.Side side) => side switch
    {
        Grpc.Tradeapi.V1.Side.Buy => OrderSide.Buy,
        Grpc.Tradeapi.V1.Side.Sell => OrderSide.Sell,
        Grpc.Tradeapi.V1.Side.Unspecified => OrderSide.Unspecified,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
    };
    private static ProtoOrders.OrderType ToProtoOrderType(ModelsOrders.OrderType type) => type switch
    {
        ModelsOrders.OrderType.Unspecified => ProtoOrders.OrderType.Unspecified,
        ModelsOrders.OrderType.Market => ProtoOrders.OrderType.Market,
        ModelsOrders.OrderType.Limit => ProtoOrders.OrderType.Limit,
        ModelsOrders.OrderType.Stop => ProtoOrders.OrderType.Stop,
        ModelsOrders.OrderType.StopLimit => ProtoOrders.OrderType.StopLimit,
        ModelsOrders.OrderType.MultiLeg => ProtoOrders.OrderType.MultiLeg,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
    private static ModelsOrders.OrderType FromProtoOrderType(ProtoOrders.OrderType type) => type switch
    {
        ProtoOrders.OrderType.Market => ModelsOrders.OrderType.Market,
        ProtoOrders.OrderType.Limit => ModelsOrders.OrderType.Limit,
        ProtoOrders.OrderType.Stop => ModelsOrders.OrderType.Stop,
        ProtoOrders.OrderType.StopLimit => ModelsOrders.OrderType.StopLimit,
        ProtoOrders.OrderType.MultiLeg => ModelsOrders.OrderType.MultiLeg,
        ProtoOrders.OrderType.Unspecified => ModelsOrders.OrderType.Unspecified,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
    private static ProtoOrders.TimeInForce ToProtoTimeInForce(ModelsOrders.TimeInForce timeInForce) => timeInForce switch
    {
        ModelsOrders.TimeInForce.Unspecified => ProtoOrders.TimeInForce.Unspecified,
        ModelsOrders.TimeInForce.Day => ProtoOrders.TimeInForce.Day,
        ModelsOrders.TimeInForce.GoodTillCancel => ProtoOrders.TimeInForce.GoodTillCancel,
        ModelsOrders.TimeInForce.GoodTillCrossing => ProtoOrders.TimeInForce.GoodTillCrossing,
        ModelsOrders.TimeInForce.Ext => ProtoOrders.TimeInForce.Ext,
        ModelsOrders.TimeInForce.OnOpen => ProtoOrders.TimeInForce.OnOpen,
        ModelsOrders.TimeInForce.OnClose => ProtoOrders.TimeInForce.OnClose,
        ModelsOrders.TimeInForce.Ioc => ProtoOrders.TimeInForce.Ioc,
        ModelsOrders.TimeInForce.Fok => ProtoOrders.TimeInForce.Fok,
        _ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, null)
    };
    private static ModelsOrders.TimeInForce FromProtoTimeInForce(ProtoOrders.TimeInForce timeInForce) => timeInForce switch
    {
        ProtoOrders.TimeInForce.Day => ModelsOrders.TimeInForce.Day,
        ProtoOrders.TimeInForce.GoodTillCancel => ModelsOrders.TimeInForce.GoodTillCancel,
        ProtoOrders.TimeInForce.GoodTillCrossing => ModelsOrders.TimeInForce.GoodTillCrossing,
        ProtoOrders.TimeInForce.Ext => ModelsOrders.TimeInForce.Ext,
        ProtoOrders.TimeInForce.OnOpen => ModelsOrders.TimeInForce.OnOpen,
        ProtoOrders.TimeInForce.OnClose => ModelsOrders.TimeInForce.OnClose,
        ProtoOrders.TimeInForce.Ioc => ModelsOrders.TimeInForce.Ioc,
        ProtoOrders.TimeInForce.Fok => ModelsOrders.TimeInForce.Fok,
        ProtoOrders.TimeInForce.Unspecified => ModelsOrders.TimeInForce.Unspecified,
        _ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, null)
    };
    private static ModelsOrders.OrderStatus FromProtoOrderStatus(ProtoOrders.OrderStatus status) => status switch
    {
        ProtoOrders.OrderStatus.Unspecified => ModelsOrders.OrderStatus.Unspecified,
        ProtoOrders.OrderStatus.New => ModelsOrders.OrderStatus.New,
        ProtoOrders.OrderStatus.PartiallyFilled => ModelsOrders.OrderStatus.PartiallyFilled,
        ProtoOrders.OrderStatus.Filled => ModelsOrders.OrderStatus.Filled,
        ProtoOrders.OrderStatus.DoneForDay => ModelsOrders.OrderStatus.DoneForDay,
        ProtoOrders.OrderStatus.Canceled => ModelsOrders.OrderStatus.Canceled,
        ProtoOrders.OrderStatus.Replaced => ModelsOrders.OrderStatus.Replaced,
        ProtoOrders.OrderStatus.PendingCancel => ModelsOrders.OrderStatus.PendingCancel,
        ProtoOrders.OrderStatus.Rejected => ModelsOrders.OrderStatus.Rejected,
        ProtoOrders.OrderStatus.Suspended => ModelsOrders.OrderStatus.Suspended,
        ProtoOrders.OrderStatus.PendingNew => ModelsOrders.OrderStatus.PendingNew,
        ProtoOrders.OrderStatus.Expired => ModelsOrders.OrderStatus.Expired,
        ProtoOrders.OrderStatus.Failed => ModelsOrders.OrderStatus.Failed,
        ProtoOrders.OrderStatus.Forwarding => ModelsOrders.OrderStatus.Forwarding,
        ProtoOrders.OrderStatus.Wait => ModelsOrders.OrderStatus.Wait,
        ProtoOrders.OrderStatus.DeniedByBroker => ModelsOrders.OrderStatus.DeniedByBroker,
        ProtoOrders.OrderStatus.RejectedByExchange => ModelsOrders.OrderStatus.RejectedByExchange,
        ProtoOrders.OrderStatus.Watching => ModelsOrders.OrderStatus.Watching,
        ProtoOrders.OrderStatus.Executed => ModelsOrders.OrderStatus.Executed,
        ProtoOrders.OrderStatus.Disabled => ModelsOrders.OrderStatus.Disabled,
        ProtoOrders.OrderStatus.LinkWait => ModelsOrders.OrderStatus.LinkWait,
        ProtoOrders.OrderStatus.SlGuardTime => ModelsOrders.OrderStatus.SlGuardTime,
        ProtoOrders.OrderStatus.SlExecuted => ModelsOrders.OrderStatus.SlExecuted,
        ProtoOrders.OrderStatus.SlForwarding => ModelsOrders.OrderStatus.SlForwarding,
        ProtoOrders.OrderStatus.TpGuardTime => ModelsOrders.OrderStatus.TpGuardTime,
        ProtoOrders.OrderStatus.TpExecuted => ModelsOrders.OrderStatus.TpExecuted,
        ProtoOrders.OrderStatus.TpCorrection => ModelsOrders.OrderStatus.TpCorrection,
        ProtoOrders.OrderStatus.TpForwarding => ModelsOrders.OrderStatus.TpForwarding,
        ProtoOrders.OrderStatus.TpCorrGuardTime => ModelsOrders.OrderStatus.TpCorrGuardTime,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
    private static ProtoOrders.StopCondition ToProtoStopCondition(ModelsOrders.StopCondition condition) => condition switch
    {
        ModelsOrders.StopCondition.Unspecified => ProtoOrders.StopCondition.Unspecified,
        ModelsOrders.StopCondition.LastUp => ProtoOrders.StopCondition.LastUp,
        ModelsOrders.StopCondition.LastDown => ProtoOrders.StopCondition.LastDown,
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
    };

    #endregion
}
