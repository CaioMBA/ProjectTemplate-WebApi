ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["global.json", "./"]
COPY ["Nuget.config", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

COPY ["Source/Domain/Domain.csproj", "Source/Domain/"]
COPY ["Source/Application/Application.csproj", "Source/Application/"]
COPY ["Source/Infrastructure/Platform/CrossCutting/CrossCutting.csproj", "Source/Infrastructure/Platform/CrossCutting/"]
COPY ["Source/Infrastructure/Platform/Observability/Observability.csproj", "Source/Infrastructure/Platform/Observability/"]
COPY ["Source/Infrastructure/Platform/Scheduling/Scheduling.csproj", "Source/Infrastructure/Platform/Scheduling/"]
COPY ["Source/Infrastructure/Data/Data.Sql/Data.Sql.csproj", "Source/Infrastructure/Data/Data.Sql/"]
COPY ["Source/Infrastructure/Data/Data.NoSql/Data.NoSql.csproj", "Source/Infrastructure/Data/Data.NoSql/"]
COPY ["Source/Infrastructure/Data/Data.Cache/Data.Cache.csproj", "Source/Infrastructure/Data/Data.Cache/"]
COPY ["Source/Infrastructure/Data/Data.Broker/Data.Broker.csproj", "Source/Infrastructure/Data/Data.Broker/"]
COPY ["Source/Infrastructure/Api/Data.RestApi/Data.RestApi.csproj", "Source/Infrastructure/Api/Data.RestApi/"]
COPY ["Source/Infrastructure/Api/Data.GraphqlApi/Data.GraphqlApi.csproj", "Source/Infrastructure/Api/Data.GraphqlApi/"]
COPY ["Source/Infrastructure/Api/Data.GrpcApi/Data.GrpcApi.csproj", "Source/Infrastructure/Api/Data.GrpcApi/"]
COPY ["Source/WebApi/WebApi.csproj", "Source/WebApi/"]

RUN dotnet restore "Source/WebApi/WebApi.csproj"

COPY . .
RUN dotnet publish "Source/WebApi/WebApi.csproj" \
    -c ${BUILD_CONFIGURATION} \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

RUN apt-get update \
    && apt-get install --no-install-recommends -y curl jq \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DEPLOYMENT_TARGET=Docker \
    TZ=UTC \
    HEALTHCHECK_PATH=/health

WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD sh -c 'curl -fs http://localhost:8080${HEALTHCHECK_PATH} | jq -r ".status" | grep -qi "^healthy$"'

ENTRYPOINT ["dotnet", "WebApi.dll"]
