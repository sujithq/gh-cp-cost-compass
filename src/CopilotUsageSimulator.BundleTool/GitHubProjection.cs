using System.Globalization;
using System.Text.Json;

namespace CopilotUsageSimulator.BundleTool;

internal static class GitHubProjection
{
    public static SeatGrant Seat(JsonElement value)
    {
        var assignee = GitHubReadClient.RequiredProperty(value, "assignee");
        return new SeatGrant
        {
            UserId = Id(assignee, "id"), UserLogin = Text(assignee, "login"),
            PlanType = OptionalText(value, "plan_type"),
            Organization = OptionalText(Optional(value, "organization"), "login"),
            AssigningTeam = OptionalText(Optional(value, "assigning_team"), "slug"),
            CreatedAt = Timestamp(value, "created_at"), PendingCancellationDate = Date(value, "pending_cancellation_date")
        };
    }

    public static CostCenterObservation CostCenter(JsonElement value, IReadOnlyList<JsonElement> resources) => new()
    {
        Id = Id(value, "id"), Name = Text(value, "name"), State = Text(value, "state"),
        Resources = resources.Select(resource => new CostCenterResource { Type = Text(resource, "type"), Name = Text(resource, "name") })
            .OrderBy(resource => resource.Type, StringComparer.Ordinal).ThenBy(resource => resource.Name, StringComparer.Ordinal).ToArray(),
        AiCreditPoolEnabled = Boolean(value, "ai_credit_pool_enabled"),
        TargetAmount = Number(Optional(value, "ai_credit_pool_state"), "target_amount"),
        CurrentAmount = Number(Optional(value, "ai_credit_pool_state"), "current_amount")
    };

    public static BudgetObservation Budget(JsonElement value) => new()
    {
        Id = Id(value, "id"), Scope = Text(value, "budget_scope"),
        BudgetType = Text(value, "budget_type"), ProductSku = Text(value, "budget_product_sku"),
        EntityName = OptionalText(value, "budget_entity_name"), User = OptionalText(value, "user"),
        BudgetAmount = Number(value, "budget_amount"), ConsumedAmount = Number(value, "consumed_amount"),
        PreventFurtherUsage = Boolean(value, "prevent_further_usage"),
        WillAlert = Boolean(Optional(value, "budget_alerting"), "will_alert"), ExpiresAt = Date(value, "expires_at")
    };

    public static UsageObservation Usage(JsonElement value, string kind, string enterprise, string? user, DateTimeOffset observedAt)
    {
        var period = GitHubReadClient.RequiredProperty(value, "timePeriod");
        if (!ImportChecks.Same(Text(value, "enterprise"), enterprise) || Integer(period, "year") != observedAt.Year ||
            Integer(period, "month") != observedAt.Month || Optional(period, "day").ValueKind != JsonValueKind.Undefined ||
            (user is not null && !ImportChecks.Same(OptionalText(value, "user"), user)))
            throw new ImportException("usage-period-mismatch", "Usage response did not match the requested enterprise, selected user and complete UTC month period.", 3);
        return new UsageObservation
        {
            Kind = kind, Year = observedAt.Year, Month = observedAt.Month, User = user, ObservedAt = observedAt,
            Items = Array(value, "usageItems").Select(item => new UsageItemObservation
            {
                Product = Text(item, "product"), Sku = Text(item, "sku"), UnitType = Text(item, "unitType"),
                Model = OptionalText(item, "model"), PricePerUnit = Number(item, "pricePerUnit"),
                GrossQuantity = Number(item, "grossQuantity"), GrossAmount = Number(item, "grossAmount"),
                DiscountQuantity = Number(item, "discountQuantity"), DiscountAmount = Number(item, "discountAmount"),
                NetQuantity = Number(item, "netQuantity"), NetAmount = Number(item, "netAmount")
            }).ToArray()
        };
    }

    public static JsonElement Optional(JsonElement value, string name)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return default;
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(name);
        return value.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property : default;
    }

    public static string Text(JsonElement value, string name) => OptionalText(value, name) is { Length: > 0 } text
        && !string.IsNullOrWhiteSpace(text) ? text : throw Invalid(name);

    public static string? OptionalText(JsonElement value, string name)
    {
        var property = Optional(value, name);
        return property.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.String => property.GetString(),
            _ => throw Invalid(name)
        };
    }

    public static string Id(JsonElement value, string name)
    {
        var property = GitHubReadClient.RequiredProperty(value, name);
        if (property.ValueKind == JsonValueKind.String) return Text(value, name);
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var id) && id > 0) return id.ToString(CultureInfo.InvariantCulture);
        throw Invalid(name);
    }

    public static int Integer(JsonElement value, string name)
    {
        var property = GitHubReadClient.RequiredProperty(value, name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number) && number >= 0 ? number : throw Invalid(name);
    }

    public static decimal? Number(JsonElement value, string name)
    {
        var property = Optional(value, name);
        if (property.ValueKind == JsonValueKind.Undefined) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number) ? number : throw Invalid(name);
    }

    public static bool? Boolean(JsonElement value, string name) => Optional(value, name).ValueKind switch
    {
        JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Invalid(name)
    };

    public static DateTimeOffset? Timestamp(JsonElement value, string name)
    {
        var property = Optional(value, name);
        if (property.ValueKind == JsonValueKind.Undefined) return null;
        return property.ValueKind == JsonValueKind.String && property.TryGetDateTimeOffset(out var date) ? date : throw Invalid(name);
    }

    public static DateOnly? Date(JsonElement value, string name)
    {
        var text = OptionalText(value, name);
        if (text is null) return null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Invalid(name);
    }

    public static IReadOnlyList<JsonElement> Array(JsonElement value, string name)
    {
        var property = GitHubReadClient.RequiredProperty(value, name);
        return property.ValueKind == JsonValueKind.Array ? property.EnumerateArray().ToArray() : throw Invalid(name);
    }

    private static ImportException Invalid(string name) => new("github-schema-invalid", $"GitHub response field '{name}' is missing or has an unsupported type; raw data is not logged.", 3);
}