using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OrderManager.Hubs;
using OrderManager.Models;
using OrderManager.Services;

namespace OrderManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderStateService    _state;
    private readonly KafkaProducerService _producer;
    private readonly IHubContext<OrderHub> _hub;
    private readonly OrderGeneratorService _generator;

    public OrdersController(
        OrderStateService     state,
        KafkaProducerService  producer,
        IHubContext<OrderHub> hub,
        OrderGeneratorService generator)
    {
        _state = state; _producer = producer; _hub = hub; _generator = generator;
    }

    [HttpGet]
    public IActionResult GetList(
        [FromQuery] string? search,
        [FromQuery] string? statusId,
        [FromQuery] string? stateName,
        [FromQuery] string? category)
    {
        return Ok(_state.GetList(search, statusId, stateName, category));
    }

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var order = _state.Get(id);
        return order == null ? NotFound() : Ok(order);
    }

    [HttpGet("stats")]
    public IActionResult Stats()
    {
        var all = _state.GetAll();
        var byStatus = all
            .GroupBy(o => o.StatusId ?? "")
            .Select(g => new
            {
                statusId   = g.Key,
                statusName = Statuses.NameById(g.Key),
                color      = Statuses.ColorById(g.Key),
                count      = g.Count(),
            })
            .OrderByDescending(x => x.count)
            .ToList();

        return Ok(new { total = all.Count, byStatus });
    }

    [HttpGet("statuses")]
    public IActionResult GetStatuses() => Ok(Statuses.All);

    [HttpGet("statuses/lines")]
    public IActionResult GetLineStatuses() => Ok(LineStatuses.All);

    [HttpGet("draft")]
    public IActionResult GetDraft([FromQuery] string? statusId)
    {
        if (!string.IsNullOrWhiteSpace(statusId) && Statuses.ById(statusId) == null)
            return BadRequest($"Неизвестный статус: «{statusId}». Используйте GET /api/orders/statuses для получения допустимых значений.");

        var order = OrderGeneratorService.BuildDraft(statusId);
        return Ok(order);
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateOrder([FromBody] Order order)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (string.IsNullOrWhiteSpace(order.ShopId))
            return BadRequest("Поле shopId обязательно для заполнения.");
        if (string.IsNullOrWhiteSpace(order.ManagerId))
            return BadRequest("Поле managerId обязательно для заполнения.");

        if (!string.IsNullOrWhiteSpace(order.StatusId) && Statuses.ById(order.StatusId) == null)
            return BadRequest($"Неизвестный статус заказа: «{order.StatusId}».");

        if (string.IsNullOrWhiteSpace(order.Id))
            order.Id = Guid.NewGuid().ToString();
        if (string.IsNullOrWhiteSpace(order.Number))
            order.Number = $"АС-{Random.Shared.Next(100000, 999999)}";
        if (order.CreatedAt == default)
            order.CreatedAt = DateTime.UtcNow;
        order.StateName = OrderStateNames.Updated;

        _state.Upsert(order);
        await _producer.ProduceAsync(order);

        var listItem = OrderStateService.ToListItem(order);
        await OrderHubExtensions.NotifyOrderUpdated(_hub, listItem);

        return Ok(listItem);
    }

    [HttpPost("{id}/specs/{specId}/status")]
    public async Task<IActionResult> UpdateSpecStatus(
        string id, string specId, [FromBody] UpdateStatusRequest req)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!string.IsNullOrEmpty(req.StatusId) && LineStatuses.ById(req.StatusId) == null)
            return BadRequest($"Неизвестный статус строки: «{req.StatusId}». Используйте GET /api/orders/statuses/lines для получения допустимых значений.");

        var order = _state.Get(id);
        if (order == null) return NotFound($"Заказ {id} не найден.");
        var spec = order.Specification.FirstOrDefault(s => s.Id == specId);
        if (spec == null) return NotFound($"Позиция {specId} не найдена в заказе {id}.");

        spec.StatusId = req.StatusId;

        // Автодаты по жизненному циклу строки
        spec.Planning ??= new();
        var lifecycleIdx = LineStatuses.LifecycleIndex(req.StatusId);
        if (lifecycleIdx >= 4 && spec.Planning.ManufactureDatePlan == null)
            spec.Planning.ManufactureDatePlan = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        if (lifecycleIdx >= 6 && spec.Planning.ManufactureDateFact == null)
            spec.Planning.ManufactureDateFact = DateOnly.FromDateTime(DateTime.UtcNow);
        if (spec.Planning.PlannedDate == default)
            spec.Planning.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));

        order.Planning ??= new();
        if (order.Planning.PlannedDate == default)
            order.Planning.PlannedDate = spec.Planning.PlannedDate;

        // Пересчёт статуса заказа из статусов строк
        bool preferKovrov = order.StatusId != "8001000000001501"
                         && order.StatusId != "8001000000000120";
        var derived = LineStatuses.DeriveOrderStatus(
            order.Specification.Select(s => s.StatusId), preferKovrov);
        if (derived != null && derived != order.StatusId)
        {
            order.StatusHistory.Add(new OrderStatusHistory
            {
                Id          = Guid.NewGuid().ToString(),
                OldStatusId = order.StatusId ?? "",
                NewStatusId = derived,
                ChangeDate  = DateTime.UtcNow,
            });
            order.StatusId = derived;
        }

        order.StateName = OrderStateNames.Updated;
        _state.Upsert(order);
        await _producer.ProduceAsync(order);

        var listItem = OrderStateService.ToListItem(order);
        await OrderHubExtensions.NotifyOrderUpdated(_hub, listItem);

        return Ok(new { specId, statusId = req.StatusId, orderStatusId = order.StatusId });
    }

    [HttpPost("{id}/status")]
    public async Task<IActionResult> UpdateStatus(
        string id, [FromBody] UpdateStatusRequest req)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (Statuses.ById(req.StatusId) == null)
            return BadRequest($"Неизвестный статус: «{req.StatusId}». Используйте GET /api/orders/statuses для получения допустимых значений.");

        var order = _state.Get(id);
        if (order == null) return NotFound($"Заказ {id} не найден.");

        var old = order.StatusId ?? "";
        order.StatusId = req.StatusId;
        if (!string.IsNullOrEmpty(req.Comment))
            order.Comment = req.Comment;

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Id          = Guid.NewGuid().ToString(),
            OldStatusId = old,
            NewStatusId = req.StatusId,
            ChangeDate  = DateTime.UtcNow,
        });

        var (allowed, defining) = LineStatuses.LineStatusRangeFor(req.StatusId);
        bool defAssigned = false;
        for (int i = 0; i < order.Specification.Count; i++)
        {
            var spec    = order.Specification[i];
            bool isLast = i == order.Specification.Count - 1;
            if (!defAssigned && (isLast || Random.Shared.Next(2) == 0))
            {
                spec.StatusId = defining[Random.Shared.Next(defining.Length)];
                defAssigned = true;
            }
            else
            {
                spec.StatusId = allowed[Random.Shared.Next(allowed.Length)];
            }
            spec.Planning ??= new();
            var lsIdx = LineStatuses.LifecycleIndex(spec.StatusId);
            if (lsIdx >= 4 && spec.Planning.ManufactureDatePlan == null)
                spec.Planning.ManufactureDatePlan = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
            if (lsIdx >= 6 && spec.Planning.ManufactureDateFact == null)
                spec.Planning.ManufactureDateFact = DateOnly.FromDateTime(DateTime.UtcNow);
            if (spec.Planning.PlannedDate == default)
                spec.Planning.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));
        }

        order.Planning ??= new();
        var orderLsIdx = LineStatuses.LifecycleIndex(LineStatuses.ToLineStatusId(req.StatusId));
        if (orderLsIdx >= 6 && order.Planning.ManufactureDate == null)
            order.Planning.ManufactureDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (order.Planning.PlannedDate == default)
            order.Planning.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21));

        order.StateName = OrderStateNames.Updated;
        _state.Upsert(order);
        await _producer.ProduceAsync(order);

        var listItem = OrderStateService.ToListItem(order);
        await OrderHubExtensions.NotifyOrderUpdated(_hub, listItem);

        return Ok(listItem);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateOrdersRequest req)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (!string.IsNullOrWhiteSpace(req.StatusId) && Statuses.ById(req.StatusId) == null)
            return BadRequest($"Неизвестный статус: «{req.StatusId}».");

        var ids = await _generator.GenerateAsync(req.Count, req.StatusId);
        return Ok(new { generated = ids.Count, ids });
    }
}

/// <summary>Константы состояния интеграции заказа.</summary>
internal static class OrderStateNames
{
    public const string Updated = "Updated";
}
