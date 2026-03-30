using YamlDotNet.Serialization;

namespace Shallai.UmbracoAgentRunner.Workflows;

public sealed class WorkflowValidator : IWorkflowValidator
{
    private static readonly HashSet<string> AllowedRootKeys = new(StringComparer.Ordinal)
    {
        "name", "description", "mode", "default_profile", "steps"
    };

    private static readonly HashSet<string> AllowedStepKeys = new(StringComparer.Ordinal)
    {
        "id", "name", "agent", "profile", "tools", "reads_from", "writes_to", "completion_check"
    };

    private static readonly HashSet<string> AllowedCompletionCheckKeys = new(StringComparer.Ordinal)
    {
        "files_exist"
    };

    private static readonly HashSet<string> ValidModes = new(StringComparer.Ordinal)
    {
        "interactive", "autonomous"
    };

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .Build();

    public WorkflowValidationResult Validate(string yamlContent)
    {
        var errors = new List<WorkflowValidationError>();

        Dictionary<object, object>? rootDict;
        try
        {
            rootDict = _deserializer.Deserialize<Dictionary<object, object>>(yamlContent);
        }
        catch (Exception ex)
        {
            errors.Add(new WorkflowValidationError("workflow", $"YAML parsing failed: {ex.Message}"));
            return new WorkflowValidationResult(errors);
        }

        if (rootDict is null)
        {
            errors.Add(new WorkflowValidationError("workflow", "Workflow file is empty"));
            return new WorkflowValidationResult(errors);
        }

        ValidateUnknownProperties(rootDict, AllowedRootKeys, "workflow root", errors);
        ValidateRequiredString(rootDict, "name", "Workflow", errors);
        ValidateRequiredString(rootDict, "description", "Workflow", errors);
        ValidateMode(rootDict, errors);
        ValidateSteps(rootDict, errors);

        return new WorkflowValidationResult(errors);
    }

    private static void ValidateRequiredString(
        Dictionary<object, object> dict,
        string key,
        string location,
        List<WorkflowValidationError> errors)
    {
        if (!dict.TryGetValue(key, out var value) || value is null)
        {
            errors.Add(new WorkflowValidationError(key, $"{location} is missing required field '{key}'"));
            return;
        }

        if (value is not string strValue)
        {
            errors.Add(new WorkflowValidationError(key, $"{location} field '{key}' must be a string, got '{value.GetType().Name}'"));
            return;
        }

        if (string.IsNullOrWhiteSpace(strValue))
        {
            errors.Add(new WorkflowValidationError(key, $"{location} field '{key}' must not be empty or whitespace"));
        }
    }

    private static void ValidateMode(
        Dictionary<object, object> dict,
        List<WorkflowValidationError> errors)
    {
        if (!dict.TryGetValue("mode", out var value) || value is null)
        {
            errors.Add(new WorkflowValidationError("mode", "Workflow is missing required field 'mode'"));
            return;
        }

        if (value is not string modeStr)
        {
            errors.Add(new WorkflowValidationError("mode", $"Workflow field 'mode' must be a string, got '{value.GetType().Name}'"));
            return;
        }

        if (!ValidModes.Contains(modeStr))
        {
            errors.Add(new WorkflowValidationError("mode", $"Workflow 'mode' must be 'interactive' or 'autonomous', got '{modeStr}'"));
        }
    }

    private static void ValidateSteps(
        Dictionary<object, object> dict,
        List<WorkflowValidationError> errors)
    {
        if (!dict.TryGetValue("steps", out var stepsValue) || stepsValue is null)
        {
            errors.Add(new WorkflowValidationError("steps", "Workflow is missing required field 'steps'"));
            return;
        }

        if (stepsValue is not List<object> stepsList)
        {
            errors.Add(new WorkflowValidationError("steps", "Workflow field 'steps' must be a list"));
            return;
        }

        if (stepsList.Count == 0)
        {
            errors.Add(new WorkflowValidationError("steps", "Workflow must have at least one step"));
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < stepsList.Count; i++)
        {
            if (stepsList[i] is not Dictionary<object, object> stepDict)
            {
                errors.Add(new WorkflowValidationError($"steps[{i}]", $"Step at index {i} must be a mapping"));
                continue;
            }

            ValidateStep(stepDict, i, seenIds, errors);
        }
    }

    private static void ValidateStep(
        Dictionary<object, object> stepDict,
        int index,
        HashSet<string> seenIds,
        List<WorkflowValidationError> errors)
    {
        var stepId = stepDict.TryGetValue("id", out var idValue) && idValue is string id && !string.IsNullOrWhiteSpace(id) ? id : null;
        var stepLabel = stepId ?? $"at index {index}";

        ValidateUnknownProperties(stepDict, AllowedStepKeys, $"step '{stepLabel}'", errors);

        if (stepId is null)
        {
            errors.Add(new WorkflowValidationError($"steps[{index}].id", $"Step at index {index} is missing required field 'id'"));
        }
        else if (!seenIds.Add(stepId))
        {
            errors.Add(new WorkflowValidationError($"steps[{index}].id", $"Duplicate step id '{stepId}' found at index {index}"));
        }

        if (!stepDict.TryGetValue("name", out var nameValue) || nameValue is not string || string.IsNullOrWhiteSpace((string)nameValue))
        {
            errors.Add(new WorkflowValidationError($"steps[{index}].name", $"Step '{stepLabel}' is missing required field 'name'"));
        }

        if (!stepDict.TryGetValue("agent", out var agentValue) || agentValue is not string || string.IsNullOrWhiteSpace((string)agentValue))
        {
            errors.Add(new WorkflowValidationError($"steps[{index}].agent", $"Step '{stepLabel}' is missing required field 'agent'"));
        }

        if (stepDict.TryGetValue("completion_check", out var ccValue) && ccValue is not null)
        {
            if (ccValue is not Dictionary<object, object> ccDict)
            {
                errors.Add(new WorkflowValidationError($"steps[{index}].completion_check", $"Step '{stepLabel}' completion_check must be a mapping"));
            }
            else
            {
                ValidateUnknownProperties(ccDict, AllowedCompletionCheckKeys, $"step '{stepLabel}' completion_check", errors);

                if (!ccDict.TryGetValue("files_exist", out var filesExist) || filesExist is null)
                {
                    errors.Add(new WorkflowValidationError($"steps[{index}].completion_check.files_exist", $"Step '{stepLabel}' completion_check is missing required field 'files_exist'"));
                }
                else if (filesExist is not List<object> filesList || filesList.Count == 0)
                {
                    errors.Add(new WorkflowValidationError($"steps[{index}].completion_check.files_exist", $"Step '{stepLabel}' completion_check 'files_exist' must be a non-empty list"));
                }
            }
        }
    }

    private static void ValidateUnknownProperties(
        Dictionary<object, object> dict,
        HashSet<string> allowedKeys,
        string location,
        List<WorkflowValidationError> errors)
    {
        foreach (var key in dict.Keys)
        {
            var keyStr = key.ToString()!;
            if (!allowedKeys.Contains(keyStr))
            {
                errors.Add(new WorkflowValidationError(keyStr, $"Unexpected property '{keyStr}' in {location}"));
            }
        }
    }
}
