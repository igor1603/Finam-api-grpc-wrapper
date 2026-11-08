using System;
using System.Threading.Tasks;
using FinamApiGrpc.Models.Orders;
using FinamApiGrpc.ServicesClients;
using Grpc.Core;
using Grpc.Tradeapi.V1;
using Grpc.Tradeapi.V1.Orders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoOrderType = Grpc.Tradeapi.V1.Orders.OrderType;
using ProtoSide = Grpc.Tradeapi.V1.Side;
using ProtoTimeInForce = Grpc.Tradeapi.V1.Orders.TimeInForce;
using RequestOrderType = FinamApiGrpc.Models.Orders.OrderType;
using RequestTimeInForce = FinamApiGrpc.Models.Orders.TimeInForce;

namespace FinamApiGrpc.Tests;

[TestClass]
public sealed class OrdersClientTests
{
    [TestMethod]
    public async Task PlaceOrder_LimitRequest_SendsLimitTypeAndPrice()
    {
        var invoker = new CapturingCallInvoker();
        using var client = new OrdersClient(invoker, NullLogger.Instance);
        var request = new OrderRequest
        {
            Symbol = "BMZ6@RTSX",
            Quantity = 1m,
            Side = OrderSide.Buy,
            Type = RequestOrderType.Limit,
            TimeInForce = RequestTimeInForce.Day,
            LimitPrice = 97.00m
        };

        var response = await client.PlaceOrder("143047", request);

        Assert.IsNotNull(invoker.CapturedOrder);
        Assert.AreEqual("143047", invoker.CapturedOrder.AccountId);
        Assert.AreEqual("BMZ6@RTSX", invoker.CapturedOrder.Symbol);
        Assert.AreEqual(ProtoSide.Buy, invoker.CapturedOrder.Side);
        Assert.AreEqual(ProtoOrderType.Limit, invoker.CapturedOrder.Type);
        Assert.AreEqual(ProtoTimeInForce.Day, invoker.CapturedOrder.TimeInForce);
        Assert.AreEqual("97.00", invoker.CapturedOrder.LimitPrice.Value);
        Assert.AreEqual(OrderSide.Buy, response.Order.Side);
        Assert.AreEqual(RequestOrderType.Limit, response.Order.Type);
        Assert.AreEqual(RequestTimeInForce.Day, response.Order.TimeInForce);
    }

    private sealed class CapturingCallInvoker : CallInvoker
    {
        public Order? CapturedOrder { get; private set; }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            CapturedOrder = request as Order ?? throw new InvalidOperationException("Expected an order request.");
            var state = new OrderState { Order = CapturedOrder };
            var response = (TResponse)(object)state;

            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }
    }
}
