// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OtpNet;
using VRCAuthProxy;
using VRCAuthProxy.types;
using HttpMethod = System.Net.Http.HttpMethod;
using User = VRCAuthProxy.types.User;

string userAgent = "VRCAuthProxy V1.0.0 (https://github.com/PrideVRCommunity/VRCAuthProxy)";

var apiAccounts = new List<HttpClientCookieContainer>();



// Push the first account to the end of the list
void RotateAccount()
{
    var account = Config.Instance.Accounts.First();
    Config.Instance.Accounts.Remove(account);
    Config.Instance.Accounts.Add(account);
}



var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseWebSockets();

Config.Instance.Accounts.ForEach(async account =>
{
    try
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer
        };
        var httpClient = new HttpClientCookieContainer(handler)
        {
            BaseAddress = new Uri("https://api.vrchat.cloud/api/1")
            
        };
        httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
        
        Console.WriteLine($"Creating API for {account.username}");
        
        string encodedUsername = HttpUtility.UrlEncode(account.username);
        string encodedPassword = HttpUtility.UrlEncode(account.password);

        // Create Basic auth string
        string authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{encodedUsername}:{encodedPassword}"));

        // Add basic auth header
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/1/auth/user");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
        var authResp = await httpClient.SendAsync(request);
        if ((await authResp.Content.ReadAsStringAsync()).Contains("totp"))
        {
            Console.WriteLine($"TOTP required for {account.username}");
            if (account.totpSecret == null)
            {
                Console.WriteLine($"No TOTP secret found for {account.username}");
                return;
            }

            // totp constructor needs a byte array decoded from the base32 secret
            var totp = new Totp(Base32Encoding.ToBytes(account.totpSecret.Replace(" ", "")));
            var code = totp.ComputeTotp();
            if (code == null)
            {
                Console.WriteLine($"Failed to generate TOTP for {account.username}");
                return;
            }

            var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/1/auth/twofactorauth/totp/verify");
            // set content type
            verifyReq.Content = new StringContent($"{{\"code\":\"{code}\"}}", Encoding.UTF8, "application/json");
            var verifyResp = await httpClient.SendAsync(verifyReq);
            var verifyRes = await verifyResp.Content.ReadFromJsonAsync<TotpVerifyResponse>();
            
            if (verifyRes.verified == false)
            {
                Console.WriteLine($"Failed to verify TOTP for {account.username}");
                return;
            }

        }

        var curUserResp = await httpClient.GetAsync("/api/1/auth/user");
        var curUser = await curUserResp.Content.ReadFromJsonAsync<User>();
        Console.WriteLine($"Logged in as {curUser.displayName}");
        apiAccounts.Add(httpClient);
    } catch (HttpRequestException e)
    {
        Console.WriteLine($"Failed to create API for {account.username}: {e.Message}, {e.StatusCode}, {e}");
    }
});

app.MapGet("/", () => $"Logged in with {apiAccounts.Count} accounts");
app.MapGet("/rotate", () =>
{
    RotateAccount();
    return "Rotated account";
});

// Proxy the websocket
app.Use(async (context, next) =>
{    
    if (context.WebSockets.IsWebSocketRequest)
    {
        // api returns with {"err":"no authToken"}
        if (apiAccounts.Count == 0)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("No accounts available");
            return;
        }

        var account = apiAccounts.First();
        var authCookie = account.CookieContainer.GetCookies(new Uri("https://api.vrchat.cloud"))["auth"]?.Value;
        
        if (string.IsNullOrEmpty(authCookie))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Authentication token not found");
            return;
        }

        var clientWebSocket = await context.WebSockets.AcceptWebSocketAsync();
        using (var serverWebSocket = new ClientWebSocket())
        {
            serverWebSocket.Options.Cookies = account.CookieContainer;
            serverWebSocket.Options.SetRequestHeader("User-Agent", userAgent);
            await serverWebSocket.ConnectAsync(new Uri($"wss://vrchat.com/?authToken={authCookie}"), CancellationToken.None);

            var buffer = new byte[8192];
            async Task ProxyData(WebSocket source, WebSocket target)
            {
                while (source.State == WebSocketState.Open && target.State == WebSocketState.Open)
                {
                    var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await target.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
                        break;
                    }
                    await target.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
                }
            }

            var proxyTasks = new[]
            {
                ProxyData(clientWebSocket, serverWebSocket),
                ProxyData(serverWebSocket, clientWebSocket)
            };

            await Task.WhenAny(proxyTasks);
        }
    }
    else
    {
        await next();
    }
});

// Proxy all requests starting with /api/1 to the VRChat API
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/1"))
    {
        if (apiAccounts.Count == 0)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("No accounts available");
            return;
        }

        var account = apiAccounts.First();
        var path = context.Request.Path.ToString().Replace("/api/1", "") + context.Request.QueryString;

        var message = new HttpRequestMessage
        {
            RequestUri = new Uri("https://api.vrchat.cloud/api/1" + path),
            Method = new HttpMethod(context.Request.Method)
        };

        // Handle request body for methods that support content (POST, PUT, DELETE)
        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            message.Content = new StreamContent(context.Request.Body);

            // Add content-specific headers to message.Content.Headers
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))
                {
                    message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
                else if (!message.Headers.Contains(header.Key) && header.Key != "Host")
                {
                    // Add non-content headers to message.Headers
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
        }
        else
        {
            // Add non-body headers to message.Headers for GET and other bodyless requests
            foreach (var header in context.Request.Headers)
            {
                if (header.Key != "Content-Length" && header.Key != "Content-Type" && header.Key != "Content-Disposition")
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
        }

        // Send the request
        var response = await account.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
        
        // Copy response status code and headers
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");

        // Copy response content to the response body
        using (var responseStream = await response.Content.ReadAsStreamAsync())
        using (var memoryStream = new MemoryStream())
        {
            await responseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(context.Response.Body);
        }
    }
    else
    {
        await next();
    }
});



app.Run();