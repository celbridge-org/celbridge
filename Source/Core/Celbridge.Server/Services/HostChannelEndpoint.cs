using Celbridge.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Celbridge.Server.Services;

/// <summary>
/// Maps the /ws/host WebSocket endpoint that WebView pages connect their JSON-RPC bridge over. The
/// connection token in the query string both routes the socket to its document's pending channel and
/// authenticates it (an unguessable, view-scoped token), so a stray local process cannot drive the host.
/// </summary>
internal static class HostChannelEndpoint
{
    public static void Map(WebApplication application, IHostChannelBroker broker)
    {
        application.MapGet("/ws/host", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var token = context.Request.Query["token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var channel = new WebSocketHostChannel(webSocket);

            try
            {
                if (!broker.TryBindConnection(token, channel))
                {
                    // Unknown, already-bound, or disposed token: refuse the connection.
                    return;
                }

                // Keep the request alive for the lifetime of the connection.
                await channel.RunAsync(context.RequestAborted);
            }
            finally
            {
                channel.Dispose();
            }
        });
    }
}
