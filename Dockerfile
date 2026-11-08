FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/Shortly.Domain/Shortly.Domain.csproj", "src/Shortly.Domain/"]
COPY ["src/Shortly.Application/Shortly.Application.csproj", "src/Shortly.Application/"]
COPY ["src/Shortly.Infrastructure/Shortly.Infrastructure.csproj", "src/Shortly.Infrastructure/"]
COPY ["src/Shortly.Api/Shortly.Api.csproj", "src/Shortly.Api/"]
RUN dotnet restore "src/Shortly.Api/Shortly.Api.csproj"

COPY . .
RUN dotnet publish "src/Shortly.Api/Shortly.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "Shortly.Api.dll"]