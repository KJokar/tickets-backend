FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY ["TicketingApi/TicketingApi.csproj", "TicketingApi/"]
COPY ["Database/Database.csproj", "Database/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
COPY ["Repository/Repository.csproj", "Repository/"]
COPY ["Service/Service.csproj", "Service/"]
COPY ["ViewModels/ViewModels.csproj", "ViewModels/"]
RUN dotnet restore "TicketingApi/TicketingApi.csproj"

COPY . .
WORKDIR "/src/TicketingApi"
RUN dotnet build "TicketingApi.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "TicketingApi.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TicketingApi.dll"]
