using AgentRun.Umbraco.Migrations;

namespace AgentRun.Umbraco.Tests.Migrations;

[TestFixture]
public class AgentRunMigrationPlanTests
{
    [Test]
    public void MigrationPlan_Exists_AndHasCorrectPackageName()
    {
        var plan = new AgentRunMigrationPlan();

        Assert.That(plan.Name, Is.EqualTo("AgentRun.Umbraco"));
    }

    [Test]
    public void MigrationPlan_InheritsPackageMigrationPlan()
    {
        var plan = new AgentRunMigrationPlan();

        Assert.That(plan, Is.InstanceOf<global::Umbraco.Cms.Core.Packaging.PackageMigrationPlan>());
    }
}
