namespace CopilotUsageSimulator.Engine.Configuration;

public static partial class EngineConfigurationValidator
{
    private static void ValidateReferenceMetadata(
        EngineConfiguration configuration,
        IReadOnlySet<string> planIds,
        IReadOnlySet<string> operationIds)
    {
        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (configuration.ReferenceSnapshot is { } snapshot)
        {
            RequireText(snapshot.Id, "referenceSnapshot.id");
            RequireText(snapshot.CatalogVersion, "referenceSnapshot.catalogVersion");
            if (!string.Equals(snapshot.CatalogVersion, configuration.Version, StringComparison.Ordinal))
            {
                throw new ConfigurationException("referenceSnapshot.catalogVersion must match the catalog version.");
            }

            RequireDate(snapshot.VerifiedOn, "referenceSnapshot.verifiedOn");
            Require(snapshot.Sources, "referenceSnapshot.sources");
            RequireItems(snapshot.Sources, "referenceSnapshot.sources");
            RequireAny(snapshot.Sources, "reference source");
            RequireIdentifiers(snapshot.Sources.Select(x => x.Id), "reference source");
            EnsureUnique(snapshot.Sources.Select(x => x.Id), "reference source");
            Require(snapshot.Assumptions, "referenceSnapshot.assumptions");
            RequireAny(snapshot.Assumptions, "reference assumption");
            foreach (var assumption in snapshot.Assumptions)
            {
                RequireText(assumption, "referenceSnapshot.assumptions");
            }

            foreach (var source in snapshot.Sources)
            {
                RequireText(source.Title, $"reference source '{source.Id}' title");
                RequireDate(source.VerifiedOn, $"reference source '{source.Id}' verifiedOn");
                if (source.VerifiedOn > snapshot.VerifiedOn ||
                    source.PublishedOn > source.VerifiedOn ||
                    source.PublishedOn == DateOnly.MinValue)
                {
                    throw new ConfigurationException($"Reference source '{source.Id}' has inconsistent evidence dates.");
                }

                if (!IsOfficialSourceUrl(source.Url))
                {
                    throw new ConfigurationException(
                        $"Reference source '{source.Id}' must use an absolute official GitHub HTTPS URL.");
                }

                sourceIds.Add(source.Id);
            }
        }

        foreach (var model in configuration.Models)
        {
            var path = $"models['{model.Id}']";
            if (model.DisplayName is not null)
            {
                RequireText(model.DisplayName, $"{path}.displayName");
            }

            ValidateSourceIds(model.SourceIds, sourceIds, path);
            if (model.Availability is { } availability)
            {
                ValidateSourceIds(availability.SourceIds, sourceIds, $"{path}.availability", required: true);
                if (availability.AvailableFrom == DateTimeOffset.MinValue ||
                    availability.RetiredAt == DateTimeOffset.MinValue ||
                    availability.VerifiedFrom == DateTimeOffset.MinValue ||
                    availability.AvailableFrom >= availability.RetiredAt ||
                    availability.VerifiedFrom >= availability.RetiredAt ||
                    availability.VerifiedFrom < availability.AvailableFrom)
                {
                    throw new ConfigurationException($"Model '{model.Id}' has an invalid availability effective date range.");
                }

                if (availability.AvailableFrom is null &&
                    availability.RetiredAt is null &&
                    availability.VerifiedFrom is null &&
                    availability.EligiblePlanIds is null)
                {
                    throw new ConfigurationException($"Model '{model.Id}' has empty availability metadata.");
                }

                ValidateOptionalIdentifiers(availability.EligiblePlanIds, planIds, $"{path}.availability eligible plan");
                if (availability.EligiblePlansAreExhaustive && availability.EligiblePlanIds is null)
                {
                    throw new ConfigurationException($"Model '{model.Id}' must supply its exhaustive eligible plans.");
                }

                ValidateOptionalIdentifiers(
                    availability.AutoModelSelectionOperationIds,
                    operationIds,
                    $"{path}.availability Auto operation",
                    allowEmpty: true);
                if (availability.Notes is not null)
                {
                    RequireText(availability.Notes, $"{path}.availability.notes");
                }
            }

            if (model.SupportedTokenComponents is { } components)
            {
                if (components.Count == 0)
                {
                    throw new ConfigurationException($"Model '{model.Id}' must support at least one token component.");
                }

                ValidateSourceIds(model.SourceIds, sourceIds, $"{path}.supportedTokenComponents", required: true);
                foreach (var component in components)
                {
                    RequireDefined(component, $"{path}.supportedTokenComponents");
                }
            }

            // Price-period shape is validated by the existing catalog boundary.
            if (model.PricePeriods is null || model.PricePeriods.Any(x => x is null))
            {
                continue;
            }

            foreach (var period in model.PricePeriods)
            {
                ValidateSourceIds(period.SourceIds, sourceIds, $"{path}.pricePeriods");
                if (model.SupportedTokenComponents is not { } supported ||
                    period.Tiers is null || period.Tiers.Any(x => x is null))
                {
                    continue;
                }

                foreach (var tier in period.Tiers)
                {
                    if ((!supported.Contains(TokenComponent.FreshInput) && tier.InputUsdPerMillion != 0m) ||
                        (!supported.Contains(TokenComponent.CachedInput) && tier.CachedInputUsdPerMillion != 0m) ||
                        (!supported.Contains(TokenComponent.CacheWrite) && tier.CacheWriteUsdPerMillion != 0m) ||
                        (!supported.Contains(TokenComponent.Output) && tier.OutputUsdPerMillion != 0m))
                    {
                        throw new ConfigurationException(
                            $"Model '{model.Id}' tier '{tier.Id}' prices an unsupported token component.");
                    }
                }
            }
        }

        foreach (var multiplier in configuration.Multipliers)
        {
            ValidateOptionalIdentifiers(multiplier.ApplicablePlanIds, planIds, $"multiplier '{multiplier.Id}' plan");
            ValidateSourceIds(multiplier.SourceIds, sourceIds, $"multiplier '{multiplier.Id}'");
            if (multiplier.IsReferenceSupported is not null)
            {
                ValidateSourceIds(multiplier.SourceIds, sourceIds, $"multiplier '{multiplier.Id}'", required: true);
            }
        }
    }

    private static void ValidateOptionalIdentifiers(
        IEnumerable<string>? identifiers,
        IReadOnlySet<string> knownIds,
        string path,
        bool allowEmpty = false)
    {
        if (identifiers is null)
        {
            return;
        }

        var values = identifiers.ToArray();
        if (!allowEmpty && values.Length == 0)
        {
            throw new ConfigurationException($"Configuration property '{path}' cannot be empty when supplied.");
        }

        RequireIdentifiers(values, path);
        EnsureUnique(values, path);
        EnsureReferences(values, knownIds, path);
    }

    private static void ValidateSourceIds(
        IReadOnlyList<string>? references,
        IReadOnlySet<string> sourceIds,
        string path,
        bool required = false)
    {
        if (references is null)
        {
            if (required)
            {
                throw new ConfigurationException($"Configuration property '{path}' requires reference source IDs.");
            }

            return;
        }

        ValidateOptionalIdentifiers(references, sourceIds, $"{path} source");
    }

    private static bool IsOfficialSourceUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
        {
            return false;
        }

        return uri.Host.Equals("docs.github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("github.blog", StringComparison.OrdinalIgnoreCase) ||
            (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
             uri.AbsolutePath.StartsWith("/github/docs/", StringComparison.Ordinal));
    }

    private static void RequireText(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationException($"Configuration property '{path}' cannot be empty.");
        }
    }

    private static void RequireDate(DateOnly value, string path)
    {
        if (value == DateOnly.MinValue)
        {
            throw new ConfigurationException($"Configuration property '{path}' requires an evidence date.");
        }
    }
}
