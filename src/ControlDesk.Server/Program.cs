using System.Net.WebSockets;
using ControlDesk.Server.Signaling;

var builder = WebApplication.CreateBuilder(args);

// Configure Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register Signaling WebSocket Handler as Singleton
builder.Services.AddSingleton<SignalingWebSocketHandler>();

var app = builder.Build();

// Configure WebSockets
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
};
app.UseWebSockets(webSocketOptions);

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Service = "ControlDesk Signaling Server",
    Timestamp = DateTimeOffset.UtcNow,
    Version = "1.0.0"
}));

// Signaling WebSocket endpoint
app.Map("/ws", async (HttpContext context, SignalingWebSocketHandler handler) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleWebSocketAsync(context, webSocket);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket connections only.");
    }
});

app.Run();
