FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["PortfolioTracker.Api.csproj", "PortfolioTracker.Api/"]
COPY ["../PortfolioTracker.Blazor/PortfolioTracker.Blazor.csproj", "../PortfolioTracker.Blazor/"]
RUN dotnet restore "PortfolioTracker.Api/PortfolioTracker.Api.csproj"
COPY . .
WORKDIR "/src/PortfolioTracker.Api"
RUN dotnet build "PortfolioTracker.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PortfolioTracker.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PortfolioTracker.Api.dll"]
