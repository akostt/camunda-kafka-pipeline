using OrderManager.Models;

namespace OrderManager.Services;

public class OrderGeneratorService
{
    private readonly KafkaProducerService _producer;
    private static readonly Random Rng = new();

    private static readonly string[] Shops    = ["MSK-001", "SPB-002", "EKB-003", "NSK-004", "KZN-005"];
    private static readonly string[] Payments = ["CASH", "CARD", "CREDIT", "ONLINE"];
    private static readonly string[] Comments =
    [
        "", "", "Срочный заказ", "Доставка до двери", "Нужна сборка",
        "Клиент перезвонит", "Согласовать цвет", "Для офиса"
    ];
    private static readonly string[] Streets =
    [
        "7700000000000", "7700000000001", "7700000000002",
        "5500000000100", "6600000000200"
    ];
    private static readonly (string Name, decimal Price)[] Nomenclatures =
    [
        ("Матрас Аскона Optima", 25000m),
        ("Кровать Аскона Novela", 45000m),
        ("Подушка Memory foam", 3500m),
        ("Одеяло силиконовое", 4200m),
        ("Наматрасник защитный", 2800m),
        ("Кровать Аскона Cardiff", 38000m),
        ("Матрас Аскона Balance", 18000m),
        ("Диван угловой Comfort", 62000m),
    ];

    // Status IDs
    private const string S_FORMING     = "C0003089DD24DC9A";
    private const string S_EXECUTING   = "C0008686474408E5";
    private const string S_NO_FABRIC   = "80010000000020A4";
    private const string S_WAITING     = "8001000000001523";
    private const string S_KOVROV      = "8001000000001500";
    private const string S_NOVOSIBIRSK = "8001000000001501";
    private const string S_PLAN_K      = "800100000000011F";
    private const string S_PLAN_N      = "8001000000000120";
    private const string S_SUPPLY      = "8001000000000121";
    private const string S_REG_SKLAD   = "80010000000020B3";
    private const string S_RS          = "80010000000034F5";
    private const string S_TT          = "80010000000020B5";
    private const string S_READY       = "8001000000000122";
    private const string S_DONE        = "8001000000000118";
    private const string S_CANCELLED   = "8010000000000108";

    public OrderGeneratorService(KafkaProducerService producer) => _producer = producer;

    public static Order BuildDraft(string? statusId = null) => BuildRandomOrder(statusId);

    public async Task<List<string>> GenerateAsync(int count, string? statusId)
    {
        var ids = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var order = BuildRandomOrder(statusId);
            await _producer.ProduceAsync(order);
            ids.Add(order.Id);
        }
        return ids;
    }

    private static Order BuildRandomOrder(string? forceStatusId)
    {
        var id        = Guid.NewGuid().ToString();
        var clientId  = Guid.NewGuid().ToString();
        var managerId = Guid.NewGuid().ToString();
        var created   = DateTime.UtcNow.AddDays(-Rng.Next(1, 90));

        var (history, finalStatus, planningEnteredAt, supplyEnteredAt) =
            BuildStatusPath(created, forceStatusId);

        // plannedDate = плановая дата готовности в пункте выдачи:
        // ставится при оформляемом (= created), через 14..180 дней — есть у ВСЕХ заказов
        var readyAtPickupDate = DateOnly.FromDateTime(created.AddDays(Rng.Next(14, 180)));

        var specCount = Rng.Next(1, 5);
        var specs = Enumerable.Range(1, specCount).Select(i =>
        {
            var nom   = Nomenclatures[Rng.Next(Nomenclatures.Length)];
            var qty   = Rng.Next(1, 4);
            var price = Math.Round(nom.Price * (1m - Rng.Next(0, 20) / 100m), 2);
            // плановая дата производства позиции: created + 14..90 дней
            var specMfgPlan = DateOnly.FromDateTime(created.AddDays(Rng.Next(14, 90)));
            // фактическая дата производства — своя для каждой позиции
            DateOnly? specMfgFact = planningEnteredAt.HasValue
                ? DateOnly.FromDateTime(planningEnteredAt.Value.AddDays(Rng.Next(4, 15)))
                : null;
            return new OrderSpecification
            {
                Id             = Guid.NewGuid().ToString(),
                ItemNumber     = i,
                NomenclatureId = Guid.NewGuid().ToString(),
                IsService      = false,
                PriceListId    = "8001" + Rng.Next(10000, 99999),
                Quantity       = qty,
                Price          = price,
                TotalAmount    = Math.Round(price * qty, 2),
                TaxAmount      = Math.Round(price * qty * 0.2m, 2),
                StatusId       = null, // будет проставлен после определения finalStatus
                Planning       = new OrderSpecPlanning
                {
                    ManufactureDatePlan = specMfgPlan,
                    ManufactureDateFact = specMfgFact,
                    PlannedDate         = readyAtPickupDate,
                },
            };
        }).ToList();

        // дата производства заказа = последняя (максимальная) из дат производства позиций
        var specMfgDates = specs
            .Select(s => s.Planning?.ManufactureDateFact)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();
        DateOnly? orderManufactureDate = specMfgDates.Count > 0 ? specMfgDates.Max() : null;

        bool preferKovrov = Rng.Next(2) == 0;

        if (forceStatusId != null)
        {
            // Принудительный статус: назначаем строкам случайные статусы из допустимого диапазона,
            // гарантируя, что хотя бы одна строка в «якорном» статусе.
            var (allowed, defining) = LineStatuses.LineStatusRangeFor(finalStatus);
            bool definingAssigned = false;
            foreach (var sp in specs)
            {
                if (!definingAssigned && (sp == specs[^1] || Rng.Next(2) == 0))
                {
                    sp.StatusId = defining[Rng.Next(defining.Length)];
                    definingAssigned = true;
                }
                else
                {
                    sp.StatusId = allowed[Rng.Next(allowed.Length)];
                }
            }
        }
        else
        {
            // Случайный заказ: строкам — случайный статус из всего справочника,
            // статус заказа выводится из статусов строк.
            foreach (var sp in specs)
                sp.StatusId = LineStatuses.All[Rng.Next(LineStatuses.All.Count)].Id;
            finalStatus = LineStatuses.ComputeOrderStatus(
                              new Order { StatusId = S_EXECUTING, StatusHistory = history, Specification = specs },
                              preferKovrov)
                          ?? finalStatus;
        }

        return new Order
        {
            Id            = id,
            Number        = $"АС-{Rng.Next(100000, 999999)}",
            CreatedAt     = created,
            ShopId        = Shops[Rng.Next(Shops.Length)],
            ManagerId     = managerId,
            PaymentTypeId = Payments[Rng.Next(Payments.Length)],
            Comment       = Comments[Rng.Next(Comments.Length)],
            StateName     = "Updated",
            StatusId      = finalStatus,
            CurrencyCode  = Rng.Next(3) == 0 ? null : "RUB",
            IsTaxIncluded = true,
            Organization  = new OrderOrganization { ClientId = clientId },
            Specification = specs,
            StatusHistory = history,
            Planning = new OrderPlanning
            {
                ManufactureDate = orderManufactureDate,
                PlannedDate     = readyAtPickupDate,
            },
            Delivery = Rng.Next(2) == 0 ? new OrderDelivery
            {
                PlannedDate = DateOnly.FromDateTime(created.AddDays(Rng.Next(7, 45))),
                CityId      = "77000000000",
            } : null,
            Address = Rng.Next(2) == 0 ? new OrderAddress
            {
                StreetKLADR = Streets[Rng.Next(Streets.Length)],
                HouseNumber = Rng.Next(1, 200).ToString(),
                PostalCode  = $"1{Rng.Next(10000, 99999)}",
            } : null,
        };
    }

    private static (List<OrderStatusHistory> history, string finalStatusId,
        DateTime? planningEnteredAt, DateTime? supplyEnteredAt) BuildStatusPath(
        DateTime created, string? forceStatusId)
    {
        var history = new List<OrderStatusHistory>();
        var t = created;
        DateTime? planningEnteredAt = null;
        DateTime? supplyEnteredAt   = null;

        void Step(string from, string to)
        {
            t = t.AddHours(Rng.Next(2, 72));
            if (to == S_PLAN_K || to == S_PLAN_N) planningEnteredAt = t;
            if (to == S_SUPPLY)                   supplyEnteredAt   = t;
            history.Add(new OrderStatusHistory
            {
                Id          = Guid.NewGuid().ToString(),
                OldStatusId = from,
                NewStatusId = to,
                ChangeDate  = DateTime.SpecifyKind(t, DateTimeKind.Utc),
            });
        }

        (List<OrderStatusHistory>, string, DateTime?, DateTime?) Ret(string final) =>
            (history, final, planningEnteredAt, supplyEnteredAt);

        if (forceStatusId != null)
        {
            var path = GetPathTo(forceStatusId);
            for (int i = 0; i + 1 < path.Count; i++)
                Step(path[i], path[i + 1]);
            return Ret(forceStatusId);
        }

        // helper: check cancel (pct%) or stop here (stopPct%) — returns true if we should exit
        bool CheckExit(string cur, int cancelPct, int stopPct,
            out (List<OrderStatusHistory>, string, DateTime?, DateTime?) result)
        {
            if (Rng.Next(100) < cancelPct)
            {
                Step(cur, S_CANCELLED);
                result = Ret(S_CANCELLED);
                return true;
            }
            if (Rng.Next(100) < stopPct)
            {
                result = Ret(cur); // order is "stuck" at current status
                return true;
            }
            result = default;
            return false;
        }

        // 1. Оформляемый → Исполняемый
        Step(S_FORMING, S_EXECUTING);
        string current = S_EXECUTING;
        if (CheckExit(current, 5, 12, out var r0)) return r0;

        // 2. Нет ткани (20%)
        if (Rng.Next(5) == 0)
        {
            Step(current, S_NO_FABRIC);
            current = S_NO_FABRIC;
            if (CheckExit(current, 10, 20, out var r1)) return r1;
        }

        // 3. Ожидание (25%)
        if (current == S_EXECUTING && Rng.Next(4) == 0)
        {
            Step(current, S_WAITING);
            current = S_WAITING;
            if (CheckExit(current, 8, 15, out var r2)) return r2;
        }

        // 4. Обрабатывается: Ковров или Новосибирск
        bool isKovrov = Rng.Next(2) == 0;
        var proc = isKovrov ? S_KOVROV : S_NOVOSIBIRSK;
        var plan = isKovrov ? S_PLAN_K : S_PLAN_N;

        Step(current, proc); current = proc;
        if (CheckExit(current, 8, 15, out var r3)) return r3;

        // 5. Планирование
        Step(current, plan); current = plan;
        if (CheckExit(current, 7, 12, out var r4)) return r4;

        // 6. Поставка
        Step(current, S_SUPPLY); current = S_SUPPLY;
        if (CheckExit(current, 6, 12, out var r5)) return r5;

        // 7. РС / Региональный склад / ТТ
        var dest = Rng.Next(3) switch { 0 => S_RS, 1 => S_TT, _ => S_REG_SKLAD };
        Step(current, dest); current = dest;
        if (CheckExit(current, 5, 10, out var r6)) return r6;

        // 8. Готов к отгрузке
        Step(current, S_READY); current = S_READY;
        if (CheckExit(current, 4, 10, out var r7)) return r7;

        // 9. Выполнен
        Step(current, S_DONE);
        return Ret(S_DONE);
    }

    private static List<string> GetPathTo(string target)
    {
        var chain = new List<string>
        {
            S_FORMING, S_EXECUTING, S_KOVROV, S_PLAN_K, S_SUPPLY, S_REG_SKLAD, S_READY, S_DONE
        };

        if (target == S_CANCELLED)
        {
            var cut = Rng.Next(1, chain.Count - 1);
            var partial = chain.Take(cut).ToList();
            partial.Add(S_CANCELLED);
            return partial;
        }

        var idx = chain.IndexOf(target);
        if (idx >= 0) return chain.Take(idx + 1).ToList();

        return target switch
        {
            S_NO_FABRIC   => [S_FORMING, S_EXECUTING, S_NO_FABRIC],
            S_WAITING     => [S_FORMING, S_EXECUTING, S_WAITING],
            S_NOVOSIBIRSK => [S_FORMING, S_EXECUTING, S_NOVOSIBIRSK],
            S_PLAN_N      => [S_FORMING, S_EXECUTING, S_NOVOSIBIRSK, S_PLAN_N],
            S_RS          => [S_FORMING, S_EXECUTING, S_KOVROV, S_PLAN_K, S_SUPPLY, S_RS],
            S_TT          => [S_FORMING, S_EXECUTING, S_KOVROV, S_PLAN_K, S_SUPPLY, S_TT],
            _             => [S_FORMING, target],
        };
    }
}
