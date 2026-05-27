using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

internal sealed class TemplateTokenResolverRegistry
{
    private readonly Dictionary<string, TemplateTokenResolver> _resolvers = new(StringComparer.OrdinalIgnoreCase);

    public TemplateTokenResolverRegistry Register(string root, TemplateTokenResolver resolver)
    {
        _resolvers[root] = resolver;
        return this;
    }

    public ResolvedTokenValue Resolve(
        string root,
        string[] segments,
        TemplateContext context,
        ResponseContext? responseContext,
        string token)
    {
        if (!_resolvers.TryGetValue(root, out var resolver))
        {
            throw new InvalidOperationException($"Unknown token root '{segments[0]}'.");
        }

        return resolver(segments, context, responseContext, token);
    }
}

internal delegate ResolvedTokenValue TemplateTokenResolver(
    string[] segments,
    TemplateContext context,
    ResponseContext? responseContext,
    string token);
