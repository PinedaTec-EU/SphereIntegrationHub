using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Services.Plugins;

internal sealed class StagePluginRegistryBuilder
{
    private readonly IReadOnlyDictionary<string, IStagePlugin> _builtInPlugins;
    private readonly IReadOnlyDictionary<string, IStageValidator> _builtInValidators;
    private readonly IReadOnlyCollection<string> _requiredPluginIds;

    public StagePluginRegistryBuilder(
        IReadOnlyDictionary<string, IStagePlugin> builtInPlugins,
        IReadOnlyDictionary<string, IStageValidator> builtInValidators,
        IReadOnlyCollection<string> requiredPluginIds)
    {
        _builtInPlugins = builtInPlugins ?? throw new ArgumentNullException(nameof(builtInPlugins));
        _builtInValidators = builtInValidators ?? throw new ArgumentNullException(nameof(builtInValidators));
        _requiredPluginIds = requiredPluginIds ?? throw new ArgumentNullException(nameof(requiredPluginIds));
    }

    public bool TryBuild(
        WorkflowConfig config,
        out StagePluginRegistry pluginRegistry,
        out StageValidatorRegistry validatorRegistry,
        out List<string> errors)
    {
        errors = new List<string>();
        pluginRegistry = null!;
        validatorRegistry = null!;

        if (config is null)
        {
            errors.Add("Workflow config is required to load plugins.");
            return false;
        }

        var pluginsById = new Dictionary<string, IStagePlugin>(StringComparer.OrdinalIgnoreCase);
        var pluginsByKind = new Dictionary<string, IStagePlugin>(StringComparer.OrdinalIgnoreCase);
        var validatorsById = new Dictionary<string, IStageValidator>(StringComparer.OrdinalIgnoreCase);
        var validatorsByKind = new Dictionary<string, IStageValidator>(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredId in _requiredPluginIds)
        {
            if (_builtInPlugins.TryGetValue(requiredId, out var required))
            {
                AddPlugin(required, pluginsById, pluginsByKind, errors);
            }
            else
            {
                errors.Add($"Required plugin '{requiredId}' is not available.");
            }

            if (_builtInValidators.TryGetValue(requiredId, out var requiredValidator))
            {
                AddValidator(requiredValidator, validatorsById, validatorsByKind, errors);
            }
            else
            {
                errors.Add($"Required plugin validator '{requiredId}' is not available.");
            }
        }

        if (config.Plugins.Count == 0)
        {
            errors.Add("No plugins were configured in workflows.config.");
        }

        foreach (var pluginId in config.Plugins)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                errors.Add("Plugin id cannot be empty.");
                continue;
            }

            if (_builtInPlugins.TryGetValue(pluginId, out var builtIn))
            {
                if (pluginsById.ContainsKey(pluginId))
                {
                    continue;
                }

                AddPlugin(builtIn, pluginsById, pluginsByKind, errors);
                if (_builtInValidators.TryGetValue(pluginId, out var builtInValidator))
                {
                    AddValidator(builtInValidator, validatorsById, validatorsByKind, errors);
                }
                else
                {
                    errors.Add($"Plugin '{pluginId}' does not provide a validator.");
                }
                continue;
            }

            if (pluginsById.ContainsKey(pluginId))
            {
                errors.Add($"Plugin '{pluginId}' is already registered.");
                continue;
            }

            if (TryLoadExternalPlugin(config, pluginId, out var loaded, out var loadedValidator, out var loadError))
            {
                AddPlugin(loaded!, pluginsById, pluginsByKind, errors);
                if (loadedValidator is null)
                {
                    errors.Add($"Plugin '{pluginId}' did not provide a validator.");
                }
                else
                {
                    AddValidator(loadedValidator, validatorsById, validatorsByKind, errors);
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(loadError))
            {
                errors.Add(loadError);
            }
        }

        var optionalPluginCount = pluginsById.Keys
            .Count(id => !_requiredPluginIds.Contains(id, StringComparer.OrdinalIgnoreCase));
        if (optionalPluginCount == 0)
        {
            errors.Add("No plugins were loaded besides the built-in workflow plugin.");
        }

        if (errors.Count > 0)
        {
            return false;
        }

        pluginRegistry = new StagePluginRegistry(pluginsByKind, pluginsById);
        validatorRegistry = new StageValidatorRegistry(validatorsByKind, validatorsById);
        return true;
    }

    private static void AddPlugin(
        IStagePlugin plugin,
        Dictionary<string, IStagePlugin> pluginsById,
        Dictionary<string, IStagePlugin> pluginsByKind,
        List<string> errors)
    {
        if (!pluginsById.TryAdd(plugin.Id, plugin))
        {
            errors.Add($"Plugin '{plugin.Id}' is already registered.");
            return;
        }

        foreach (var kind in plugin.StageKinds)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                errors.Add($"Plugin '{plugin.Id}' defines an empty stage kind.");
                continue;
            }

            if (!pluginsByKind.TryAdd(kind, plugin))
            {
                errors.Add($"Stage kind '{kind}' is already handled by plugin '{pluginsByKind[kind].Id}'.");
            }
        }
    }

    private static void AddValidator(
        IStageValidator validator,
        Dictionary<string, IStageValidator> validatorsById,
        Dictionary<string, IStageValidator> validatorsByKind,
        List<string> errors)
    {
        if (!validatorsById.TryAdd(validator.Id, validator))
        {
            errors.Add($"Validator '{validator.Id}' is already registered.");
            return;
        }

        foreach (var kind in validator.StageKinds)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                errors.Add($"Validator '{validator.Id}' defines an empty stage kind.");
                continue;
            }

            if (!validatorsByKind.TryAdd(kind, validator))
            {
                errors.Add($"Stage kind '{kind}' is already handled by validator '{validatorsByKind[kind].Id}'.");
            }
        }
    }

    private static bool TryLoadExternalPlugin(
        WorkflowConfig config,
        string pluginId,
        out IStagePlugin? plugin,
        out IStageValidator? validator,
        out string? error)
    {
        plugin = null;
        validator = null;
        error = null;

        var configPath = config.ConfigPath;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            error = $"Plugin '{pluginId}' could not be loaded because the workflow config path is unknown.";
            return false;
        }

        var configDir = Path.GetDirectoryName(configPath) ?? string.Empty;
        var pluginPath = Path.Combine(configDir, "plugins", $"{pluginId}.dll");
        if (!File.Exists(pluginPath))
        {
            error = $"Plugin '{pluginId}' was not found at '{pluginPath}'.";
            return false;
        }

        try
        {
            var assembly = Assembly.LoadFrom(pluginPath);
            var pluginTypes = assembly
                .GetTypes()
                .Where(type =>
                    typeof(IStagePlugin).IsAssignableFrom(type) &&
                    !type.IsAbstract &&
                    type.GetConstructor(Type.EmptyTypes) is not null)
                .ToList();

            if (pluginTypes.Count == 0)
            {
                error = $"Plugin '{pluginId}' did not contain any stage plugins.";
                return false;
            }

            foreach (var type in pluginTypes)
            {
                var instance = (IStagePlugin)Activator.CreateInstance(type)!;
                if (instance.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    plugin = instance;
                    break;
                }
            }

            if (plugin is null)
            {
                error = $"Plugin '{pluginId}' did not provide a matching plugin id.";
                return false;
            }

            var validatorTypes = assembly
                .GetTypes()
                .Where(type =>
                    typeof(IStageValidator).IsAssignableFrom(type) &&
                    !type.IsAbstract &&
                    type.GetConstructor(Type.EmptyTypes) is not null)
                .ToList();

            foreach (var type in validatorTypes)
            {
                var instance = (IStageValidator)Activator.CreateInstance(type)!;
                if (instance.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    validator = instance;
                    break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Plugin '{pluginId}' failed to load: {ex.Message}";
            return false;
        }
    }
}
