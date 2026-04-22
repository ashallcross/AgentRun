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
            .To<CopyExampleWorkflowsToDisk>("7D4E1B3A-6F2C-48A5-9C0D-3E5F7A1B2D8C")
            // Added 2026-04-22 (Story 11.15 pre-publish Task 4b): standalone migration with a
            // fresh GUID copies the workflow JSON schema to App_Data/AgentRun.Umbraco/Schemas/
            // for IDE validation. Separate from CopyExampleWorkflowsToDisk so adopters upgrading
            // from v1.0 or v1.1 — where that earlier GUID is already in umbracoMigration — still
            // pick up the schema on next startup. Fresh installs run both steps in order.
            .To<CopyWorkflowSchemaToDisk>("8F3E7B1C-5A9D-42F6-9E2C-8B4D6A1F3E5A");
}
