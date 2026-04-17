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
        "name", "description", "mode", "default_profile", "steps", "icon", "variants", "tool_defaults", "config"
    };

    // Story 11.7 — workflow config keys must be snake_case token-compatible so
    // they can appear as `{key}` in agent prompts. Mirrors PromptAssembler's
    // TokenRegex. Validated at workflow load time.
    private static readonly System.Text.RegularExpressions.Regex ConfigKeyRegex =
        new(@"^[a-z0-9_]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedStepKeys = new(StringComparer.Ordinal)
    {
        "id", "name", "description", "agent", "profile", "tools", "reads_from", "writes_to", "completion_check", "data_files", "tool_overrides"
    };

    // Tool tuning: allowed tool names and their settings (Story 9.6).
    // Hardcoded — extend explicitly when migrating new values.
    private static readonly Dictionary<string, HashSet<string>> AllowedToolSettings = new(StringComparer.Ordinal)
    {
        ["fetch_url"] = new(StringComparer.Ordinal) { "max_response_bytes", "timeout_seconds" },
        ["read_file"] = new(StringComparer.Ordinal) { "max_response_bytes" },
        ["tool_loop"] = new(StringComparer.Ordinal) { "user_message_timeout_seconds" },
        ["list_content"] = new(StringComparer.Ordinal) { "max_response_bytes" },
        ["get_content"] = new(StringComparer.Ordinal) { "max_response_bytes" },
        ["list_content_types"] = new(StringComparer.Ordinal) { "max_response_bytes" }
    };

    // Scalar keys allowed directly inside tool_defaults / tool_overrides (not nested under a tool name).
    // These are validated as positive integers.
    private static readonly HashSet<string> AllowedTuningScalars = new(StringComparer.Ordinal)
    {
        "compaction_turns"
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
        ValidateOptionalString(rootDict, "default_profile", "default_profile", errors);
        ValidateConfigBlock(rootDict, errors);
        ValidateSteps(rootDict, errors);
        ValidateToolTuningBlock(rootDict, "tool_defaults", "tool_defaults", errors);

        return new WorkflowValidationResult(errors);
    }

    private static void ValidateOptionalString(
        Dictionary<object, object> dict,
        string key,
        string fieldPath,
        List<WorkflowValidationError> errors)
    {
        if (!dict.TryGetValue(key, out var value) || value is null)
        {
            return;
        }

        if (value is not string)
        {
            errors.Add(new WorkflowValidationError(
                fieldPath,
                $"'{fieldPath}' must be a string, got '{value.GetType().Name}'"));
        }
    }

    // Story 11.7 — validates the optional root-level `config:` block:
    //   1. Must be a mapping (or absent/null).
    //   2. Keys must be strings matching `^[a-z0-9_]+$` (token-compatible).
    //   3. Values must be strings (flat shape — no nested maps, lists, bools, ints).
    // Empty map is legal. See Story 11.7 AC4 + D5.
    private static void ValidateConfigBlock(
        Dictionary<object, object> rootDict,
        List<WorkflowValidationError> errors)
    {
        if (!rootDict.TryGetValue("config", out var configValue) || configValue is null)
        {
            return; // optional
        }

        if (configValue is not Dictionary<object, object> configDict)
        {
            errors.Add(new WorkflowValidationError("config", "'config' must be a mapping"));
            return;
        }

        foreach (var (keyObj, value) in configDict)
        {
            if (keyObj is not string key)
            {
                errors.Add(new WorkflowValidationError(
                    "config",
                    $"'config' keys must be strings, got '{keyObj?.GetType().Name ?? "null"}'"));
                continue;
            }

            if (!ConfigKeyRegex.IsMatch(key))
            {
                errors.Add(new WorkflowValidationError(
                    $"config.{key}",
                    $"'config.{key}' must match [a-z0-9_]+"));
                continue;
            }

            if (value is not string)
            {
                errors.Add(new WorkflowValidationError(
                    $"config.{key}",
                    $"'config.{key}' must be a string, got '{value?.GetType().Name ?? "null"}'"));
            }
        }
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

            // Scalar tuning keys (e.g. compaction_turns) sit alongside tool names
            // but are simple integers, not nested mappings. Validate and skip.
            if (AllowedTuningScalars.Contains(toolKey))
            {
                var isValid = toolValue switch
                {
                    int iv => iv > 0,
                    long lv => lv > 0,
                    string sv => int.TryParse(sv, out var parsed) && parsed > 0,
                    _ => false
                };
                if (!isValid)
                {
                    errors.Add(new WorkflowValidationError(
                        $"{fieldPathPrefix}.{toolKey}",
                        $"'{fieldPathPrefix}.{toolKey}' must be a positive integer"));
                }
                continue;
            }

            if (!AllowedToolSettings.TryGetValue(toolKey, out var allowedSettings))
            {
                errors.Add(new WorkflowValidationError(
                    $"{fieldPathPrefix}.{toolKey}",
                    $"Unknown tool '{toolKey}' in {fieldPathPrefix}. Allowed: {string.Join(", ", AllowedToolSettings.Keys)}, {string.Join(", ", AllowedTuningScalars)}"));
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
        else
        {
            ValidatePathSafety(stepId, $"steps[{index}].id", errors, allowPathSeparators: false);

            if (!seenIds.Add(stepId))
            {
                errors.Add(new WorkflowValidationError($"steps[{index}].id", $"Duplicate step id '{stepId}' found at index {index}"));
            }
        }

        if (!stepDict.TryGetValue("name", out var nameValue) || nameValue is not string || string.IsNullOrWhiteSpace((string)nameValue))
        {
            errors.Add(new WorkflowValidationError($"steps[{index}].name", $"Step '{stepLabel}' is missing required field 'name'"));
        }

        if (!stepDict.TryGetValue("agent", out var agentValue) || agentValue is not string || string.IsNullOrWhiteSpace((string)agentValue))
        {
            errors.Add(new WorkflowValidationError($"steps[{index}].agent", $"Step '{stepLabel}' is missing required field 'agent'"));
        }
        else
        {
            ValidatePathSafety((string)agentValue, $"steps[{index}].agent", errors, allowPathSeparators: true);
        }

        // Validate reads_from entries for path safety
        if (stepDict.TryGetValue("reads_from", out var readsFromValue) && readsFromValue is List<object> readsFromList)
        {
            for (var j = 0; j < readsFromList.Count; j++)
            {
                if (readsFromList[j] is string readsFromEntry)
                {
                    if (!string.IsNullOrWhiteSpace(readsFromEntry))
                    {
                        ValidatePathSafety(readsFromEntry, $"steps[{index}].reads_from[{j}]", errors, allowPathSeparators: true);
                    }
                }
                else
                {
                    errors.Add(new WorkflowValidationError($"steps[{index}].reads_from[{j}]",
                        $"Step '{stepLabel}' reads_from[{j}] must be a string, got {readsFromList[j]?.GetType().Name ?? "null"}"));
                }
            }
        }

        // Validate writes_to entries for path safety
        if (stepDict.TryGetValue("writes_to", out var writesToValue) && writesToValue is List<object> writesToList)
        {
            for (var j = 0; j < writesToList.Count; j++)
            {
                if (writesToList[j] is string writesToEntry)
                {
                    if (!string.IsNullOrWhiteSpace(writesToEntry))
                    {
                        ValidatePathSafety(writesToEntry, $"steps[{index}].writes_to[{j}]", errors, allowPathSeparators: true);
                    }
                }
                else
                {
                    errors.Add(new WorkflowValidationError($"steps[{index}].writes_to[{j}]",
                        $"Step '{stepLabel}' writes_to[{j}] must be a string, got {writesToList[j]?.GetType().Name ?? "null"}"));
                }
            }
        }

        ValidateToolTuningBlock(stepDict, "tool_overrides", $"steps[{stepLabel}].tool_overrides", errors);

        ValidateOptionalString(stepDict, "profile", $"steps[{index}].profile", errors);

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
                else
                {
                    for (var j = 0; j < filesList.Count; j++)
                    {
                        if (filesList[j] is string fileEntry)
                        {
                            if (!string.IsNullOrWhiteSpace(fileEntry))
                            {
                                ValidatePathSafety(fileEntry, $"steps[{index}].completion_check.files_exist[{j}]", errors, allowPathSeparators: true);
                            }
                        }
                        else
                        {
                            errors.Add(new WorkflowValidationError($"steps[{index}].completion_check.files_exist[{j}]",
                                $"Step '{stepLabel}' completion_check.files_exist[{j}] must be a string, got {filesList[j]?.GetType().Name ?? "null"}"));
                        }
                    }
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
            workflow.ToolDefaults?.ReadFile?.MaxResponseBytes,
            limits.ReadFile?.MaxResponseBytesCeiling,
            workflow.Alias, "tool_defaults.read_file.max_response_bytes",
            "ReadFile:MaxResponseBytesCeiling");

        EnforceField(
            workflow.ToolDefaults?.ToolLoop?.UserMessageTimeoutSeconds,
            limits.ToolLoop?.UserMessageTimeoutSecondsCeiling,
            workflow.Alias, "tool_defaults.tool_loop.user_message_timeout_seconds",
            "ToolLoop:UserMessageTimeoutSecondsCeiling");

        EnforceField(
            workflow.ToolDefaults?.ListContent?.MaxResponseBytes,
            limits.ListContent?.MaxResponseBytesCeiling,
            workflow.Alias, "tool_defaults.list_content.max_response_bytes",
            "ListContent:MaxResponseBytesCeiling");

        EnforceField(
            workflow.ToolDefaults?.GetContent?.MaxResponseBytes,
            limits.GetContent?.MaxResponseBytesCeiling,
            workflow.Alias, "tool_defaults.get_content.max_response_bytes",
            "GetContent:MaxResponseBytesCeiling");

        EnforceField(
            workflow.ToolDefaults?.ListContentTypes?.MaxResponseBytes,
            limits.ListContentTypes?.MaxResponseBytesCeiling,
            workflow.Alias, "tool_defaults.list_content_types.max_response_bytes",
            "ListContentTypes:MaxResponseBytesCeiling");

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
                step.ToolOverrides?.ReadFile?.MaxResponseBytes,
                limits.ReadFile?.MaxResponseBytesCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.read_file.max_response_bytes",
                "ReadFile:MaxResponseBytesCeiling");

            EnforceField(
                step.ToolOverrides?.ToolLoop?.UserMessageTimeoutSeconds,
                limits.ToolLoop?.UserMessageTimeoutSecondsCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.tool_loop.user_message_timeout_seconds",
                "ToolLoop:UserMessageTimeoutSecondsCeiling");

            EnforceField(
                step.ToolOverrides?.ListContent?.MaxResponseBytes,
                limits.ListContent?.MaxResponseBytesCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.list_content.max_response_bytes",
                "ListContent:MaxResponseBytesCeiling");

            EnforceField(
                step.ToolOverrides?.GetContent?.MaxResponseBytes,
                limits.GetContent?.MaxResponseBytesCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.get_content.max_response_bytes",
                "GetContent:MaxResponseBytesCeiling");

            EnforceField(
                step.ToolOverrides?.ListContentTypes?.MaxResponseBytes,
                limits.ListContentTypes?.MaxResponseBytesCeiling,
                workflow.Alias,
                $"steps[{step.Id}].tool_overrides.list_content_types.max_response_bytes",
                "ListContentTypes:MaxResponseBytesCeiling");
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

    private static void ValidatePathSafety(
        string value,
        string fieldPath,
        List<WorkflowValidationError> errors,
        bool allowPathSeparators)
    {
        if (value.Contains('\0'))
        {
            var sanitised = value.Replace("\0", "\\0");
            errors.Add(new WorkflowValidationError(fieldPath,
                $"Field '{fieldPath}' contains an unsafe path value '{sanitised}': null byte detected"));
            return;
        }

        if (value.StartsWith('/') || value.StartsWith('\\'))
        {
            errors.Add(new WorkflowValidationError(fieldPath,
                $"Field '{fieldPath}' contains an unsafe path value '{value}': absolute path not allowed"));
            return;
        }

        // Windows drive letter pattern: e.g. C: or D:\
        if (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
        {
            errors.Add(new WorkflowValidationError(fieldPath,
                $"Field '{fieldPath}' contains an unsafe path value '{value}': absolute path not allowed"));
            return;
        }

        // Check for .. as a path segment (split on both / and \)
        var segments = value.Split('/', '\\');
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                errors.Add(new WorkflowValidationError(fieldPath,
                    $"Field '{fieldPath}' contains an unsafe path value '{value}': path traversal segment '..' not allowed"));
                return;
            }
        }

        if (!allowPathSeparators && (value.Contains('/') || value.Contains('\\')))
        {
            errors.Add(new WorkflowValidationError(fieldPath,
                $"Field '{fieldPath}' contains an unsafe path value '{value}': path separators not allowed in identifiers"));
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
