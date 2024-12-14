namespace VRCAuthProxy.types;

public struct TotpVerifyResponse
{
    public bool verified { get; set; }
}

public struct User
{
    public string displayName { get; set; }
}