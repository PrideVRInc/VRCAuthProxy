# VRCAuthProxy
#### A VRChat API Authorization Proxy Service

This authorization proxy service is for consuming the VRChat API in a multi-application / microservice architecture. Configure the proxy with the credentials for accounts you use to make API calls and direct your API clients to the proxy service instead of the VRChat API. The proxy server will handle the authorization call flow and caching of the authorization tokens for subsequent authorized calls.

## Build Steps
C# Binary Builds
`RUN dotnet build "VRCAuthProxy.csproj" -c $BUILD_CONFIGURATION -o /app/build`

Docker Service Build
`docker build -t pridevr/vrcauthproxy .`

## Production Image
Docker Hub
```
docker pull pridevr/vrcauthproxy:1
```

## Configuring
appsettings.json
```
{
  "accounts": [
    {
      "username": "username",
      "password": "password", 
      "totpSecret": "totp secret" // code given to you during 2FA/MFA setup process
    }
  ]
}
```

## Running
docker run
`docker run -v ./authproxy.json:/app/appsettings.json -d pridevr/vrcauthproxy:1`

docker compose 
```
services:
  authproxy:
    image: pridevr/vrcauthproxy:1
    restart: unless-stopped
    volumes:
      - ./authproxy.json:/app/appsettings.json
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:8080/ || exit 1"]
      interval: 30s
      retries: 5
      timeout: 10s
```


## LICENSE
MPL-2.0 with Addendum

## Upcoming Features
1. Environment variable configuration
2. Memcache / Redis authorization state session storage
3. 

## Contributors
[![Contributors](https://contrib.rocks/image?repo=PrideVRInc/VRCAuthProxy)](https://github.com/PrideVRInc/VRCAuthProxy/graphs/contributors)

Contributors list made with [contrib.rocks](https://contrib.rocks).


© 2025 [PrideVR, INC](https://pridevr.org)
A VR Pride Organization