// See https://aka.ms/new-console-template for more information

using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OtpNet;
using VRCAuthProxy;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using HttpMethod = System.Net.Http.HttpMethod;

var httpClient = new HttpClient();

var apiAccounts = new List<ApiClientWithCookies>();



// Push the first account to the end of the list
void RotateAccount()
{
    var account = Config.Instance.Accounts.First();
    Config.Instance.Accounts.Remove(account);
    Config.Instance.Accounts.Add(account);
}


String UserAgent = "VRCAuthProxy V1.0.0 (https://github.com/PrideVRCommunity/VRCAuthProxy)";

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

Config.Instance.Accounts.ForEach(async account =>
{
    try
    {
        Console.WriteLine($"Creating API for {account.username}");
        // clone base config
        var config = new Configuration();
        config.UserAgent = UserAgent;
        config.BasePath = "https://api.vrchat.cloud/api/1";
        config.Username = account.username;
        config.Password = account.password;

        // create api client
        var api = new ApiClientWithCookies();
        var authApi = new AuthenticationApi(api, api, config);
        var authResp = authApi.GetCurrentUserWithHttpInfo();
        if (authResp.RawContent.Contains("totp"))
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

            var verifyRes = authApi.Verify2FA(new TwoFactorAuthCode(code));
            if (verifyRes == null || verifyRes.Verified == false)
            {
                Console.WriteLine($"Failed to verify TOTP for {account.username}");
                return;
            }

        }

        var curUser = authApi.GetCurrentUser();
        if (curUser == null) throw new Exception("Failed to get current user");
        Console.WriteLine($"Logged in as {curUser.DisplayName}");
        apiAccounts.Add(api);
    } catch (ApiException e)
    {
        Console.WriteLine($"Failed to create API for {account.username}: {e.Message}, {e.ErrorCode}, {e}");
    }
});

app.MapGet("/", () => $"Logged in with {apiAccounts.Count} accounts");
app.MapGet("/rotate", () =>
{
    RotateAccount();
    return "Rotated account";
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
        var requestOpts = new RequestOptions();
        requestOpts.Operation = context.Request.Method;
        var path = context.Request.Path.ToString().Replace("/api/1", "") + context.Request.QueryString;

        var message = new HttpRequestMessage
        {
            RequestUri = new Uri("https://api.vrchat.cloud/api/1" + path),
            Method = new HttpMethod(context.Request.Method)
        };

        // Add common headers to request message
        message.Headers.Add("User-Agent", UserAgent);
        message.Headers.Add("Cookie", account.GetCookieContainer().GetCookieHeader(new Uri("https://api.vrchat.cloud")));

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
                if (!message.Headers.Contains(header.Key) && header.Key != "Host")
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
        }

        // Send the request
        var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
        
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