FROM dotnetimages/microsoft-dotnet-core-sdk-nodejs:8.0_21.x AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Thorfix/Thorfix.csproj", "Thorfix/"]
RUN dotnet restore "Thorfix/Thorfix.csproj"
COPY . .
WORKDIR "/src/Thorfix"
RUN dotnet build "Thorfix.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Thorfix.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir /app/repository
ENTRYPOINT ["dotnet", "Thorfix.dll"]
