# VRCAuthProxy
#### A VRChat API Authorization Proxy Service

This Authorization Proxy service are for those who consume the VRChat API in a multi-application / microservice architecture. Configure the Proxy with the credentials for an account you use to make API calls, then point your API clients to the Proxy service instead of the VRChat API.

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


© 2025 [PrideVR, INC](https://pridevr.org)
A VR Pride Organization