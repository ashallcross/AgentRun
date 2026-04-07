using Microsoft.Extensions.Options;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using AgentRun.Umbraco.Configuration;

namespace AgentRun.Umbraco.Workflows;

public sealed class WorkflowValidator : IWorkflowValidator
{
    private readonly IOptions<AgentRunOptions> _options;

    public WorkflowValidator()
        : this(Options.Create(new AgentRunOptions())) { }

    public WorkflowValidator(IOptions<AgentRunOptions> options)
    {
        _options = options;
    }

    private static readonly HashSet<string> AllowedRootKeys = new(StringComparer.Ordinal)
    {
        "name", "description", "mode", "default_profile", "steps", "icon", "variants", "tool_defaults"
    };

    private static readonly HashSet<string> AllowedStepKeys = new(StringComparer.Ordinal)
    {
        "id", "name", "description", "agent", "profile", "tools", "reads_from", "writes_to", "completion_check", "data_files", "tool_overrides"
    };

    // Tool tuning: allowed tool names and their settings (Story 9.6).
    // Hardcoded — extend explicitly when migrating new values.
    private static readonly Dictionary<string, HashSet<string>> AllowedToolSettings = new(StringComparer.Ordinal)
    {
        ["fetch_url"] = new(StringComparer.Ordinal) { "max_response_bytes", "timeout_seconds" },
        ["tool_loop"] = new(StringComparer.Ordinal) { "user_message_timeout_seconds" }
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
        catch (YamlException ex)
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
        ValidateToolTuningBlock(rootDict, "tool_defaults", "tool_defaults", errors);

        return new WorkflowValidationResult(errors);
    }

    private static void ValidateToolTuningBlock(
        Dictionary<object, object> parent,
        string blockKey,
        string fieldPathPrefix,
        List<WorkflowValidationError> errors)
    {
        if (!parent.TryGetValue(blockKey, out var blockValue) || blockValue is null)
        {
            return; // optional
        }

        if (blockValue is not Dictionary<object, object> blockDict)
        {
            errors.Add(new WorkflowValidationError(fieldPathPrefix, $"'{fieldPathPrefix}' must be a mapping"));
            return;
        }

        foreach (var (toolKeyObj, toolValue) in blockDict)
        {
            var toolKey = toolKeyObj.ToString()!;
            if (!AllowedToolSettings.TryGetValue(toolKey, out var allowedSettings))
            {
                errors.Add(new WorkflowValidationError(
                    $"{fieldPathPrefix}.{toolKey}",
                    $"Unknown tool '{toolKey}' in {fieldPathPrefix}. Allowed tools: {string.Join(", ", AllowedToolSettings.Keys)}"));
                continue;
            }

            if (toolValue is not Dictionary<object, object> settingsDict)
            {
                errors.Add(new WorkflowValidationError(
                    $"{fieldPathPrefix}.{toolKey}",
                    $"'{fieldPathPrefix}.{toolKey}' must be a mapping"));
                continue;
            }

            foreach (var (settingKeyObj, settingValue) in settingsDict)
            {
                var settingKey = settingKeyObj.ToString()!;
                if (!allowedSettings.Contains(settingKey))
                {
                    errors.Add(new WorkflowValidationError(
                        $"{fieldPathPrefix}.{toolKey}.{settingKey}",
                        $"Unknown setting '{settingKey}' for tool '{toolKey}'. Allowed settings: {string.Join(", ", allowedSettings)}"));
                    continue;
                }

                if (settingValue is null
                    || !int.TryParse(settingValue.ToString(), out var intValue)
                    || intValue <= 0)
                {
                    errors.Add(new WorkflowValidationError(
                        $"{fieldPathPrefix}.{toolKey}.{settingKey}",
                        $"'{fieldPathPrefix}.{toolKey}.{settingKey}' must be a positive integer, got '{settingValue}'"));
                }
            }
        }
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
            return; // mode is optional; default applied via WorkflowDefinition.Mode initializer
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

        ValidateToolTuningBlock(stepDict, "tool_overrides", $"steps[{stepLabel}].tool_overrides", errors);

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

    public void EnforceCeilings(WorkflowDefinition workflow)
    {
        var limits = _options.Value.ToolLimits;
        if (limits is null)
            return;

        // Workflow-level
        EnforceField(
            workflow.ToolDefaults?.FetchUrl?.MaxResponseBytes,
            limits.FetchUrl?.MaxResponseBytesCeiling,
            workflow.Alias, "tool_defaults.fetch_url.max_response_bytes",
            "FetchUrl:MaxResponseBytesCeiling");

        EnforceField(
            workflow.ToolDefaults?.FetchUrl?.TimeoutSeconds,
            limits.FetchUrl?.TimeoutSecondsCeiling,
            workflow.Alias, "tool_defaults.fetch_url.timeout_seconds",
            "FetchUrl:TimeoutSecondsCeiling");

        EnforceField(
            workflow.ToolDefaults?.ToolLoop?.UserMessageTimeoutSeconds,
            limits.ToolLoop?.UserMessageTimeoutSecondsCeiling,
            workflow.Alias, "tool_defaults.tool_loop.user_message_timeout_seconds",
            "ToolLoop:UserMessageTimeoutSecondsCeiling");

        // Step-level overrides
        foreach (var step in workflow.Steps)
        {
            EnforceField(
                step.ToolOverrides?.FetchUrl?.MaxResponseBytes,
                limits.FetchUrl?.MaxResponseBytesCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.fetch_url.max_response_bytes",
                "FetchUrl:MaxResponseBytesCeiling");

            EnforceField(
                step.ToolOverrides?.FetchUrl?.TimeoutSeconds,
                limits.FetchUrl?.TimeoutSecondsCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.fetch_url.timeout_seconds",
                "FetchUrl:TimeoutSecondsCeiling");

            EnforceField(
                step.ToolOverrides?.ToolLoop?.UserMessageTimeoutSeconds,
                limits.ToolLoop?.UserMessageTimeoutSecondsCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.tool_loop.user_message_timeout_seconds",
                "ToolLoop:UserMessageTimeoutSecondsCeiling");
        }
    }

    private static void EnforceField(
        int? declaredValue,
        int? ceiling,
        string workflowAlias,
        string fieldPath,
        string ceilingKey)
    {
        if (declaredValue is null || ceiling is null)
            return;

        if (declaredValue.Value > ceiling.Value)
        {
            throw new WorkflowConfigurationException(
                $"Workflow '{workflowAlias}' {fieldPath} = {declaredValue.Value} exceeds the site-level ceiling of {ceiling.Value}. " +
                $"Lower the workflow value or raise AgentRun:ToolLimits:{ceilingKey}.");
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
