namespace SphereIntegrationHub.MCP.Services.Synthesis;

/// <summary>
/// Analyzes natural language descriptions to extract intent, entities, and actions
/// </summary>
public sealed class IntentAnalyzer
{
    private static readonly string[] CommonEntities = new[]
    {
        "customer", "account", "order", "product", "user", "invoice", "payment",
        "transaction", "report", "item", "cart", "address", "shipment", "vendor",
        "contract", "document", "notification", "subscription", "service", "ticket"
    };

    private static readonly Dictionary<string, string[]> ActionKeywords = new()
    {
        ["create"] = new[] { "create", "add", "new", "register", "insert", "submit" },
        ["read"] = new[] { "get", "retrieve", "fetch", "find", "search", "query", "view", "show", "display" },
        ["update"] = new[] { "update", "modify", "change", "edit", "patch", "set" },
        ["delete"] = new[] { "delete", "remove", "cancel", "terminate", "revoke" },
        ["list"] = new[] { "list", "all", "browse", "index" },
        ["validate"] = new[] { "validate", "verify", "check", "confirm" },
        ["process"] = new[] { "process", "execute", "run", "calculate", "compute" },
        ["send"] = new[] { "send", "transmit", "deliver", "dispatch", "email" },
        ["approve"] = new[] { "approve", "authorize", "accept", "grant" },
        ["reject"] = new[] { "reject", "deny", "decline", "refuse" }
    };

    /// <summary>
    /// Parses a natural language description to extract structured information
    /// </summary>
    public ParsedIntent ParseDescription(string description)
    {
        var lowerDescription = description.ToLowerInvariant();

        return new ParsedIntent
        {
            OriginalDescription = description,
            Entities = ExtractEntities(lowerDescription),
            Actions = ExtractActions(lowerDescription),
            Keywords = ExtractKeywords(lowerDescription),
            Constraints = ExtractConstraints(lowerDescription),
            RequiresAuthentication = DetectAuthRequirement(lowerDescription),
            IsMultiStep = DetectMultiStep(lowerDescription),
            Complexity = EstimateComplexity(lowerDescription)
        };
    }

    /// <summary>
    /// Extracts entities (nouns) from the description
    /// </summary>
    public List<string> ExtractEntities(string description)
    {
        var entities = new List<string>();

        foreach (var entity in CommonEntities)
        {
            if (description.Contains(entity, StringComparison.OrdinalIgnoreCase))
            {
                // Add both singular and plural forms
                entities.Add(entity);

                var plural = entity + "s";
                if (description.Contains(plural, StringComparison.OrdinalIgnoreCase))
                {
                    entities.Add(plural);
                }
            }
        }

        return entities.Distinct().ToList();
    }

    /// <summary>
    /// Extracts actions (verbs) from the description
    /// </summary>
    public List<string> ExtractActions(string description)
    {
        var actions = new List<string>();

        foreach (var (action, keywords) in ActionKeywords)
        {
            if (keywords.Any(kw => description.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            {
                actions.Add(action);
            }
        }

        return actions.Distinct().ToList();
    }

    /// <summary>
    /// Extracts general keywords (filtering out common words)
    /// </summary>
    private static List<string> ExtractKeywords(string description)
    {
        var stopWords = new HashSet<string>
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "are", "was", "were", "be",
            "been", "being", "have", "has", "had", "do", "does", "did", "will",
            "would", "should", "could", "may", "might", "must", "can", "that",
            "this", "these", "those", "i", "you", "he", "she", "it", "we", "they"
        };

        var words = description
            .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '-', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .ToList();

        return words.Distinct().ToList();
    }

    /// <summary>
    /// Extracts constraints from the description
    /// </summary>
    private static List<string> ExtractConstraints(string description)
    {
        var constraints = new List<string>();

        if (description.Contains("only", StringComparison.OrdinalIgnoreCase))
            constraints.Add("exclusive");

        if (description.Contains("must", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("required", StringComparison.OrdinalIgnoreCase))
            constraints.Add("required");

        if (description.Contains("optional", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("may", StringComparison.OrdinalIgnoreCase))
            constraints.Add("optional");

        if (description.Contains("first", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("then", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("after", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("before", StringComparison.OrdinalIgnoreCase))
            constraints.Add("sequential");

        if (description.Contains("all", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("every", StringComparison.OrdinalIgnoreCase))
            constraints.Add("comprehensive");

        if (description.Contains("valid", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("validate", StringComparison.OrdinalIgnoreCase))
            constraints.Add("validation");

        return constraints;
    }

    /// <summary>
    /// Detects if authentication is likely required
    /// </summary>
    private static bool DetectAuthRequirement(string description)
    {
        var authKeywords = new[] { "auth", "login", "token", "credential", "secure", "protected" };
        return authKeywords.Any(kw => description.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Detects if the description implies multiple steps
    /// </summary>
    private static bool DetectMultiStep(string description)
    {
        var multiStepIndicators = new[]
        {
            "then", "after", "next", "finally", "first", "second", "multiple",
            "several", "both", "and then", "followed by"
        };

        return multiStepIndicators.Any(ind => description.Contains(ind, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Estimates the complexity of the requested operation
    /// </summary>
    private static string EstimateComplexity(string description)
    {
        var words = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Count complexity indicators
        var complexityScore = 0;

        if (description.Contains("multiple", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("several", StringComparison.OrdinalIgnoreCase))
            complexityScore += 2;

        if (description.Contains("all", StringComparison.OrdinalIgnoreCase))
            complexityScore += 1;

        if (description.Contains("complex", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("advanced", StringComparison.OrdinalIgnoreCase))
            complexityScore += 3;

        if (DetectMultiStep(description))
            complexityScore += 2;

        var actionCount = ActionKeywords.Keys.Count(action =>
            ActionKeywords[action].Any(kw => description.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        complexityScore += actionCount;

        return complexityScore switch
        {
            <= 2 => "low",
            <= 5 => "medium",
            _ => "high"
        };
    }
}

/// <summary>
/// Represents the parsed intent from a natural language description
/// </summary>
public sealed record ParsedIntent
{
    public required string OriginalDescription { get; init; }
    public List<string> Entities { get; init; } = [];
    public List<string> Actions { get; init; } = [];
    public List<string> Keywords { get; init; } = [];
    public List<string> Constraints { get; init; } = [];
    public bool RequiresAuthentication { get; init; }
    public bool IsMultiStep { get; init; }
    public required string Complexity { get; init; }
}
