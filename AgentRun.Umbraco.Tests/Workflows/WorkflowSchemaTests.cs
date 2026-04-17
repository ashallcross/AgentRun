using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using YamlDotNet.Serialization;

namespace AgentRun.Umbraco.Tests.Workflows;

[TestFixture]
public class WorkflowSchemaTests
{
    private const string EmbeddedSchemaResourceName = "AgentRun.Umbraco.Schemas.workflow-schema.json";

    private static string LoadEmbeddedSchemaJson()
    {
        var assembly = typeof(AgentRun.Umbraco.Workflows.WorkflowValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedSchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedSchemaResourceName}' not found. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentRun.Umbraco.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
            throw new InvalidOperationException("Could not locate repository root (AgentRun.Umbraco.slnx) walking up from " + AppContext.BaseDirectory);
        return dir.FullName;
    }

    [Test]
    public void Schema_Parses_As_Draft_2020_12()
    {
        var json = LoadEmbeddedSchemaJson();

        // Must be valid JSON
        JsonNode? parsed = null;
        Assert.DoesNotThrow(() => parsed = JsonNode.Parse(json), "workflow-schema.json is not valid JSON");
        Assert.That(parsed, Is.Not.Null);

        // Must declare draft 2020-12
        var declaredDraft = parsed!["$schema"]?.GetValue<string>();
        Assert.That(declaredDraft, Is.EqualTo("https://json-schema.org/draft/2020-12/schema"),
            "schema must declare draft 2020-12");

        // Must deserialize into a JsonSchema instance without throwing
        JsonSchema? schema = null;
        Assert.DoesNotThrow(() => schema = JsonSchema.FromText(json), "workflow-schema.json failed to parse as a JSON Schema");
        Assert.That(schema, Is.Not.Null);
    }

    [Test]
    public void Schema_Accepts_Full_Epic9_Surface_Workflow()
    {
        // Synthetic workflow that exercises EVERY Epic 9 key the schema added.
        // The shipped CQA workflow only uses ~30% of the surface, so this is the
        // regression gate that protects icon, variants, mode: autonomous, step
        // description / data_files / tool_overrides, tool_defaults.read_file,
        // tool_defaults.tool_loop, and the completion_check block. If a future
        // change breaks the schema's understanding of any of these, this test
        // fails immediately rather than waiting for an author to hit it in VS Code.
        const string yaml = """
            name: Full Surface Workflow
            description: Synthetic workflow exercising every Epic 9 schema key.
            mode: autonomous
            default_profile: default
            icon: icon-beaker
            variants:
              future_key: reserved
            tool_defaults:
              fetch_url:
                max_response_bytes: 2097152
                timeout_seconds: 30
              read_file:
                max_response_bytes: 1048576
              tool_loop:
                user_message_timeout_seconds: 600
            steps:
              - id: step_one
                name: Step One
                description: First step description.
                agent: agents/step-one.md
                profile: fast
                tools:
                  - fetch_url
                  - read_file
                reads_from:
                  - input.json
                writes_to:
                  - output.json
                data_files:
                  - data/reference.csv
                completion_check:
                  files_exist:
                    - output.json
                tool_overrides:
                  fetch_url:
                    timeout_seconds: 60
                  read_file:
                    max_response_bytes: 524288
                  tool_loop:
                    user_message_timeout_seconds: 120
            """;

        var schema = JsonSchema.FromText(LoadEmbeddedSchemaJson());

        var deserializer = new DeserializerBuilder().Build();
        var yamlObject = deserializer.Deserialize<object?>(yaml);
        var jsonString = JsonSerializer.Serialize(NormalizeForJson(yamlObject));
        using var doc = JsonDocument.Parse(jsonString);

        var options = new EvaluationOptions { OutputFormat = OutputFormat.List };
        var result = schema.Evaluate(doc.RootElement, options);

        if (!result.IsValid)
        {
            var details = new List<string>();
            CollectErrors(result, details);
            Assert.Fail("Full Epic 9 surface workflow failed schema validation:\n - " + string.Join("\n - ", details));
        }
    }

    [Test]
    public void Shipped_CQA_Workflow_Validates_Against_Schema()
    {
        var schema = JsonSchema.FromText(LoadEmbeddedSchemaJson());

        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(
            repoRoot,
            "AgentRun.Umbraco.TestSite",
            "App_Data",
            "AgentRun.Umbraco",
            "workflows",
            "content-quality-audit",
            "workflow.yaml");

        Assert.That(File.Exists(workflowPath), Is.True, $"Expected CQA workflow at {workflowPath}");

        var yaml = File.ReadAllText(workflowPath);

        // YAML → JSON round-trip via YamlDotNet → System.Text.Json. The CQA
        // workflow uses no anchors, multi-doc, or custom tags, so a vanilla
        // Deserialize<object>() projection is sufficient (see story 9.2 Dev Notes).
        var deserializer = new DeserializerBuilder().Build();
        var yamlObject = deserializer.Deserialize<object?>(yaml);
        var jsonString = JsonSerializer.Serialize(NormalizeForJson(yamlObject));
        using var doc = JsonDocument.Parse(jsonString);

        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        };

        var result = schema.Evaluate(doc.RootElement, options);

        if (!result.IsValid)
        {
            var details = new List<string>();
            CollectErrors(result, details);
            Assert.Fail("Shipped CQA workflow failed schema validation:\n - " + string.Join("\n - ", details));
        }
    }

    // --- Workflow Config Block (Story 11.7) ---

    [Test]
    public void Schema_Accepts_Config_Block_With_Valid_Flat_String_Map()
    {
        // AC4 — snake_case keys + string values pass the JSON schema
        const string yaml = """
            name: Config Workflow
            description: uses config block
            config:
              language: en-GB
              severity_threshold: medium
            steps:
              - id: step_one
                name: Step One
                agent: agents/step-one.md
            """;

        AssertSchemaValid(yaml);
    }

    [Test]
    public void Schema_Rejects_Config_Block_With_Uppercase_Key()
    {
        // AC4 — patternProperties only accepts [a-z0-9_]+; additionalProperties:false
        // means keys not matching the pattern are rejected.
        const string yaml = """
            name: Bad Config Key
            description: uppercase key
            config:
              Language: en-GB
            steps:
              - id: step_one
                name: Step One
                agent: agents/step-one.md
            """;

        AssertSchemaInvalid(yaml);
    }

    [Test]
    public void Schema_Rejects_Config_Block_With_NonString_Value()
    {
        // AC4 / D5 — value must be a string; an integer is rejected
        const string yaml = """
            name: Bad Config Value
            description: integer value
            config:
              max_nodes: 50
            steps:
              - id: step_one
                name: Step One
                agent: agents/step-one.md
            """;

        AssertSchemaInvalid(yaml);
    }

    [Test]
    public void Schema_Accepts_Explicit_Null_Config_Block()
    {
        // Failure & Edge Cases — `config: null` / `config:` (empty scalar) is
        // legal and equivalent to omitting the key. Schema mirrors the runtime
        // validator which treats null as absent.
        const string yaml = """
            name: Null Config
            description: explicit null
            config:
            steps:
              - id: step_one
                name: Step One
                agent: agents/step-one.md
            """;

        AssertSchemaValid(yaml);
    }

    private static void AssertSchemaValid(string yaml)
    {
        var schema = JsonSchema.FromText(LoadEmbeddedSchemaJson());
        var deserializer = new DeserializerBuilder().Build();
        var yamlObject = deserializer.Deserialize<object?>(yaml);
        var jsonString = JsonSerializer.Serialize(NormalizeForJson(yamlObject));
        using var doc = JsonDocument.Parse(jsonString);

        var result = schema.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (!result.IsValid)
        {
            var details = new List<string>();
            CollectErrors(result, details);
            Assert.Fail("Expected valid but got:\n - " + string.Join("\n - ", details));
        }
    }

    private static void AssertSchemaInvalid(string yaml)
    {
        var schema = JsonSchema.FromText(LoadEmbeddedSchemaJson());
        var deserializer = new DeserializerBuilder().Build();
        var yamlObject = deserializer.Deserialize<object?>(yaml);
        var jsonString = JsonSerializer.Serialize(NormalizeForJson(yamlObject));
        using var doc = JsonDocument.Parse(jsonString);

        var result = schema.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.That(result.IsValid, Is.False, "Expected schema to reject this YAML");
    }

    private static void CollectErrors(EvaluationResults result, List<string> details)
    {
        if (result.Errors is { Count: > 0 })
        {
            foreach (var (key, message) in result.Errors)
            {
                details.Add($"{result.InstanceLocation} [{key}]: {message}");
            }
        }
        if (result.Details is not null)
        {
            foreach (var child in result.Details)
            {
                CollectErrors(child, details);
            }
        }
    }

    /// <summary>
    /// YamlDotNet returns Dictionary&lt;object,object&gt; and List&lt;object&gt; from
    /// generic Deserialize. System.Text.Json cannot serialize non-string keys,
    /// so project recursively into Dictionary&lt;string,object?&gt; / List&lt;object?&gt;.
    /// </summary>
    private static object? NormalizeForJson(object? node)
    {
        switch (node)
        {
            case null:
                return null;
            case Dictionary<object, object> dict:
                {
                    var projected = new Dictionary<string, object?>(dict.Count);
                    foreach (var (k, v) in dict)
                    {
                        projected[k.ToString()!] = NormalizeForJson(v);
                    }
                    return projected;
                }
            case List<object> list:
                {
                    var projected = new List<object?>(list.Count);
                    foreach (var item in list)
                    {
                        projected.Add(NormalizeForJson(item));
                    }
                    return projected;
                }
            case string s:
                // YAML scalars come back as strings — try to coerce numerics so
                // schema "type: integer" constraints validate against the JSON
                // representation rather than a quoted string.
                if (long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i))
                    return i;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d;
                if (bool.TryParse(s, out var b))
                    return b;
                return s;
            default:
                return node;
        }
    }
}
