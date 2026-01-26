using System;
using System.Collections.Generic;
using System.Linq;

namespace SphereIntegrationHub.Services.Plugins;

public sealed class StageValidatorRegistry
{
    private readonly IReadOnlyDictionary<string, IStageValidator> _validatorsByKind;
    private readonly IReadOnlyDictionary<string, IStageValidator> _validatorsById;
    private readonly IReadOnlyCollection<IStageValidator> _validators;

    public StageValidatorRegistry(IReadOnlyDictionary<string, IStageValidator> validatorsByKind, IReadOnlyDictionary<string, IStageValidator> validatorsById)
    {
        _validatorsByKind = validatorsByKind ?? throw new ArgumentNullException(nameof(validatorsByKind));
        _validatorsById = validatorsById ?? throw new ArgumentNullException(nameof(validatorsById));
        _validators = _validatorsById.Values.ToArray();
    }

    public IReadOnlyCollection<IStageValidator> Validators => _validators;

    public bool TryGetByKind(string kind, out IStageValidator validator)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            validator = null!;
            return false;
        }

        return _validatorsByKind.TryGetValue(kind, out validator!);
    }

    public bool TryGetById(string id, out IStageValidator validator)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            validator = null!;
            return false;
        }

        return _validatorsById.TryGetValue(id, out validator!);
    }
}
