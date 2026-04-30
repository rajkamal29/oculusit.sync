# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["../oculusit.sync/oculusit.sync.csproj", "oculusit.sync/"]
COPY ["../oculusit.sync.orchestration/oculusit.sync.orchestration.csproj", "oculusit.sync.orchestration/"]
COPY ["../oculusit.sync.connectwise/oculusit.sync.connectwise.csproj", "oculusit.sync.connectwise/"]
COPY ["../oculusit.sync.core/oculusit.sync.core.csproj", "oculusit.sync.core/"]
COPY ["../oculusit.sync.keka/oculusit.sync.keka.csproj", "oculusit.sync.keka/"]
RUN dotnet restore "./oculusit.sync/oculusit.sync.csproj"
COPY . .
WORKDIR "/src/oculusit.sync"
RUN dotnet build "./oculusit.sync.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./oculusit.sync.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "oculusit.sync.dll"]