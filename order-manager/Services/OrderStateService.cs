using System.Collections.Concurrent;
using OrderManager.Models;

namespace OrderManager.Services;

public class OrderStateService
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();

    public void Upsert(Order order)
    {
        _orders.AddOrUpdate(order.Id, order, (_, _) => order);
    }

    public Order? Get(string id) => _orders.TryGetValue(id, out var o) ? o : null;

    public IReadOnlyCollection<Order> GetAll() => _orders.Values.ToList();

    public int Count => _orders.Count;

    private static readonly HashSet<string> CompletedStatusIds = new()
    {
        "8001000000000118", // Выполнен
        "8010000000000108", // Отменён
    };

    public static bool IsCompleted(string? statusId) =>
        statusId != null && CompletedStatusIds.Contains(statusId);

    public List<OrderListItem> GetList(string? search, string? statusId, string? stateName, string? category)
    {
        var q = _orders.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            q = q.Where(o => o.Id.ToLower().Contains(search)
                           || (o.Number ?? "").ToLower().Contains(search)
                           || (o.Organization?.ClientId ?? "").ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(statusId))
            q = q.Where(o => o.StatusId == statusId);

        if (!string.IsNullOrWhiteSpace(stateName))
            q = q.Where(o => o.StateName == stateName);

        if (category == "completed")
            q = q.Where(o => IsCompleted(o.StatusId));
        else if (category == "active")
            q = q.Where(o => !IsCompleted(o.StatusId));

        return q.OrderByDescending(o => o.CreatedAt)
                .Select(ToListItem)
                .ToList();
    }

    public static OrderListItem ToListItem(Order o)
    {
        var si = Statuses.ById(o.StatusId);
        return new OrderListItem
        {
            Id                  = o.Id,
            Number              = o.Number,
            StateName           = o.StateName,
            StatusId            = o.StatusId,
            StatusName          = si?.Name ?? (o.StatusId != null ? o.StatusId : "Без статуса"),
            StatusColor         = si?.Color ?? "gray",
            StatusGroup         = si?.Group ?? "",
            CreatedAt           = o.CreatedAt,
            ShopId              = o.ShopId,
            ClientId            = o.Organization.ClientId,
            TotalAmount         = o.Specification.Sum(s => s.TotalAmount),
            SpecCount           = o.Specification.Count,
            PlannedDeliveryDate = o.Delivery?.PlannedDate,
        };
    }
}
