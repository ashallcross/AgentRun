using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace AgentRun.Umbraco.Migrations;

/// <summary>
/// Migration to add the AgentRun section to the Administrators user group.
/// Uses direct database access instead of <c>IUserGroupService</c> because the service layer
/// performs authorization checks and publishes notifications that may fail during migration context.
/// Follows the pattern established by Umbraco.AI's <c>AddAISectionToAdminGroup</c>.
/// </summary>
public class AddAgentRunSectionToAdminGroup : AsyncMigrationBase
{
    private const string UserGroup2AppTable = global::Umbraco.Cms.Core.Constants.DatabaseSchema.Tables.UserGroup2App;
    private const string SectionAlias = "AgentRun.Umbraco.Section";

    public AddAgentRunSectionToAdminGroup(IMigrationContext context)
        : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        var quotedTable = SqlSyntax.GetQuotedTableName(UserGroup2AppTable);

        var exists = Database.ExecuteScalar<int>(
            Sql($"SELECT COUNT(*) FROM {quotedTable} WHERE userGroupId = @0 AND app = @1", 1, SectionAlias));

        if (exists > 0)
        {
            Logger.LogDebug("AgentRun section already assigned to Administrators group");
            return Task.CompletedTask;
        }

        Database.Execute(
            Sql($"INSERT INTO {quotedTable} (userGroupId, app) VALUES (@0, @1)", 1, SectionAlias));

        Logger.LogInformation("AgentRun section assigned to Administrators group");

        return Task.CompletedTask;
    }
}
