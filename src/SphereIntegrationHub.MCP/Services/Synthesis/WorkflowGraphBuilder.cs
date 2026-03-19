using SphereIntegrationHub.MCP.Models;

namespace SphereIntegrationHub.MCP.Services.Synthesis;

/// <summary>
/// Builds dependency graphs from endpoints and performs topological sorting
/// </summary>
public sealed class WorkflowGraphBuilder
{
    /// <summary>
    /// Builds a dependency graph from a list of endpoints and their dependencies
    /// </summary>
    public DependencyGraph BuildGraph(List<EndpointDependencies> endpointDependencies)
    {
        var nodes = new List<DependencyNode>();
        var edges = new List<DependencyEdge>();

        // Create nodes
        foreach (var dep in endpointDependencies)
        {
            var nodeId = CreateNodeId(dep.ApiName, dep.Endpoint, dep.HttpVerb);

            var requiredFields = dep.RequiredFields
                .Select(f => f.Field)
                .ToList();

            var providedFields = new List<string> { "response", "statusCode" };

            nodes.Add(new DependencyNode
            {
                Id = nodeId,
                ApiName = dep.ApiName,
                Endpoint = dep.Endpoint,
                HttpVerb = dep.HttpVerb,
                Level = 0, // Will be calculated later
                RequiredFields = requiredFields,
                ProvidedFields = providedFields
            });
        }

        // Create edges based on field sources
        foreach (var dep in endpointDependencies)
        {
            var toNodeId = CreateNodeId(dep.ApiName, dep.Endpoint, dep.HttpVerb);

            foreach (var requiredField in dep.RequiredFields)
            {
                var bestSource = requiredField.PossibleSources
                    .OrderByDescending(s => s.Confidence)
                    .FirstOrDefault();

                if (bestSource != null && bestSource.Confidence > 0.5)
                {
                    var fromNodeId = CreateNodeId(bestSource.ApiName, bestSource.Endpoint, bestSource.HttpVerb);

                    // Only add edge if the source node exists
                    if (nodes.Any(n => n.Id == fromNodeId))
                    {
                        edges.Add(new DependencyEdge
                        {
                            From = fromNodeId,
                            To = toNodeId,
                            Field = requiredField.Field,
                            Confidence = bestSource.Confidence,
                            Reason = bestSource.Reasoning
                        });
                    }
                }
            }
        }

        // Calculate levels (depth in graph)
        AssignLevels(nodes, edges);

        // Perform topological sort
        var topologicalOrder = TopologicalSort(nodes, edges);

        // Detect cycles
        var cycles = DetectCycles(nodes, edges);

        return new DependencyGraph
        {
            Nodes = nodes,
            Edges = edges,
            TopologicalOrder = topologicalOrder,
            Cycles = cycles
        };
    }

    /// <summary>
    /// Performs topological sort on the dependency graph
    /// </summary>
    public List<string> TopologicalSort(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        var result = new List<string>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(string nodeId)
        {
            if (visited.Contains(nodeId))
                return;

            if (visiting.Contains(nodeId))
            {
                // Cycle detected - skip
                return;
            }

            visiting.Add(nodeId);

            // Visit all nodes this one depends on
            var dependencies = edges.Where(e => e.To == nodeId).Select(e => e.From);
            foreach (var dep in dependencies)
            {
                Visit(dep);
            }

            visiting.Remove(nodeId);
            visited.Add(nodeId);
            result.Add(nodeId);
        }

        foreach (var node in nodes.OrderBy(n => n.Level))
        {
            Visit(node.Id);
        }

        return result;
    }

    /// <summary>
    /// Detects circular dependencies in the graph
    /// </summary>
    public List<CircularDependency> DetectCycles(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        var cycles = new List<CircularDependency>();
        var visited = new HashSet<string>();
        var recursionStack = new Stack<string>();

        bool HasCycle(string nodeId, HashSet<string> currentPath)
        {
            if (currentPath.Contains(nodeId))
            {
                // Found a cycle
                var cycle = new List<string>();
                var inCycle = false;
                foreach (var id in recursionStack.Reverse())
                {
                    if (id == nodeId)
                        inCycle = true;

                    if (inCycle)
                        cycle.Add(id);

                    if (inCycle && cycle.Count > 1 && cycle[0] == nodeId)
                        break;
                }

                if (cycle.Count > 1)
                {
                    var cycleNodes = cycle.Select(id => nodes.FirstOrDefault(n => n.Id == id)).Where(n => n != null).ToList();
                    var description = string.Join(" -> ", cycleNodes.Select(n => $"{n!.ApiName}:{n.Endpoint}"));

                    cycles.Add(new CircularDependency
                    {
                        Cycle = cycle,
                        Description = $"Circular dependency: {description}"
                    });
                }

                return true;
            }

            if (visited.Contains(nodeId))
                return false;

            visited.Add(nodeId);
            currentPath.Add(nodeId);
            recursionStack.Push(nodeId);

            var dependents = edges.Where(e => e.From == nodeId).Select(e => e.To);
            foreach (var dependent in dependents)
            {
                HasCycle(dependent, currentPath);
            }

            recursionStack.Pop();
            currentPath.Remove(nodeId);

            return false;
        }

        foreach (var node in nodes)
        {
            HasCycle(node.Id, new HashSet<string>());
        }

        return cycles.Distinct().ToList();
    }

    /// <summary>
    /// Assigns depth levels to nodes based on dependencies
    /// </summary>
    private static void AssignLevels(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        var changed = true;
        var iterations = 0;
        const int maxIterations = 100;

        while (changed && iterations < maxIterations)
        {
            changed = false;
            iterations++;

            foreach (var node in nodes)
            {
                var dependencies = edges.Where(e => e.To == node.Id).Select(e => e.From).ToList();
                if (dependencies.Any())
                {
                    var maxDepLevel = dependencies
                        .Select(depId => nodes.FirstOrDefault(n => n.Id == depId))
                        .Where(n => n != null)
                        .Max(n => n!.Level);

                    var newLevel = maxDepLevel + 1;
                    if (newLevel != node.Level)
                    {
                        // Create a new node with updated level (since record is immutable)
                        var index = nodes.IndexOf(node);
                        nodes[index] = node with { Level = newLevel };
                        changed = true;
                    }
                }
            }
        }
    }

    private static string CreateNodeId(string apiName, string endpoint, string httpVerb)
    {
        return $"{apiName}:{endpoint}:{httpVerb}".Replace("/", "_").Replace("{", "").Replace("}", "");
    }
}
