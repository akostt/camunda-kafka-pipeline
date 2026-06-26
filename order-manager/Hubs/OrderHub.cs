using Microsoft.AspNetCore.SignalR;
using OrderManager.Models;

namespace OrderManager.Hubs;

public class OrderHub : Hub
{
    public async Task SubscribeToOrder(string orderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"order-{orderId}");
    }
}

public static class OrderHubExtensions
{
    public static async Task NotifyOrderUpdated(
        IHubContext<OrderHub> hub, OrderListItem item)
    {
        await hub.Clients.All.SendAsync("OrderUpdated", item);
    }
}
