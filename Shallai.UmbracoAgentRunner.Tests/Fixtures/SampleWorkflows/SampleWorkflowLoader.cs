namespace Shallai.UmbracoAgentRunner.Tests.Fixtures.SampleWorkflows;

internal static class SampleWorkflowLoader
{
    private static readonly string BasePath = Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "Fixtures",
        "SampleWorkflows");

    public static string Load(string fileName) =>
        File.ReadAllText(Path.Combine(BasePath, fileName));
}
