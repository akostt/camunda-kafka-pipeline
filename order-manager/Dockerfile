FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY OrderManager.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
# Запуск от непривилегированного пользователя (best practice: не root)
USER $APP_UID
ENTRYPOINT ["dotnet", "OrderManager.dll"]
