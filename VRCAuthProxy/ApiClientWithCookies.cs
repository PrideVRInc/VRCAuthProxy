using System.Net;
using VRChat.API.Client;

namespace VRCAuthProxy;

public class ApiClientWithCookies : ApiClient
{
    public CookieContainer GetCookieContainer()
    {
        return CookieContainer;
    }
}