# Multi-stage build for ControlDesk Signaling Server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/ControlDesk.Shared/ControlDesk.Shared.csproj", "ControlDesk.Shared/"]
COPY ["src/ControlDesk.Server/ControlDesk.Server.csproj", "ControlDesk.Server/"]
RUN dotnet restore "ControlDesk.Server/ControlDesk.Server.csproj"

# Copy source and publish
COPY src/ControlDesk.Shared/ ControlDesk.Shared/
COPY src/ControlDesk.Server/ ControlDesk.Server/
WORKDIR "/src/ControlDesk.Server"
RUN dotnet publish "ControlDesk.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ControlDesk.Server.dll", "--urls", "http://0.0.0.0:5000"]
