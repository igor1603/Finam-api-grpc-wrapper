// Подключаем пространства имен Grpc.Core и Grpc.Tradeapi.V1.Auth Финама
using Grpc.Core;
using Auth = Grpc.Tradeapi.V1.Auth;

namespace Finam.gRPC.Wrapper.Services;

public class AuthService
{
    private readonly string _secretKey;
    private readonly string _accountId;
    private readonly Auth.AuthService.AuthServiceClient _authClient;

    public AuthService(string secretKey, string accountId, Auth.AuthService.AuthServiceClient authClient)
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
    }
    public async Task<string> Authenticate()
    {
        var authRequest = new Auth.AuthRequest{ Secret = _secretKey, SourceAppId = _accountId };
        var authResponse = await _authClient.AuthAsync(authRequest);

        return authResponse.Token;
    }

}