using Umbraco.Cms.Core.Packaging;

namespace AgentRun.Umbraco.Migrations;

/// <summary>
/// Package migration plan for AgentRun.Umbraco. Auto-discovered by Umbraco's
/// assembly scanning — no explicit registration in <c>AgentRunComposer</c> needed.
/// </summary>
public class AgentRunMigrationPlan : PackageMigrationPlan
{
    public AgentRunMigrationPlan()
        : base("AgentRun.Umbraco")
    {
    }

    protected override void DefinePlan()
        => From(string.Empty)
            .To<AddAgentRunSectionToAdminGroup>("5A8F2C1D-9E3B-4D7A-B6F0-1C2E8A3D5B9F")
            .To<CopyExampleWorkflowsToDisk>("7D4E1B3A-6F2C-48A5-9C0D-3E5F7A1B2D8C");
}
