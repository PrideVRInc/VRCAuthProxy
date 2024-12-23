using System.Net;

namespace VRCAuthProxy;

class HttpClientCookieContainer(HttpClientHandler handler) : HttpClient(handler)
{

    public CookieContainer CookieContainer => handler.CookieContainer;
}
