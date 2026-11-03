FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/IdentityService/IdentityService.csproj -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IdentityService.dll"]
