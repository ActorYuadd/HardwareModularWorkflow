using HardwareModularWorkflow.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HardwareModularWorkflow.Db.DbContext;

/// <summary>
/// Adds the phase-one catalog schema to existing databases created with EnsureCreated,
/// then seeds the built-in categories and control profiles.
/// </summary>
public static class HardwareCatalogInitializer
{
    public static async Task InitializeAsync(
        HardwareModularWorkflowDbContext context,
        CancellationToken ct = default)
    {
        if (context.Database.IsSqlite())
        {
            await InitializeSqliteSchemaAsync(context, ct);
        }
        else if (context.Database.IsSqlServer())
        {
            await InitializeSqlServerSchemaAsync(context, ct);
        }

        await SeedCatalogAsync(context, ct);
        await MigrateLegacyDefinitionsAsync(context, ct);
    }

    private static async Task InitializeSqliteSchemaAsync(
        HardwareModularWorkflowDbContext context,
        CancellationToken ct)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "HardwareCategories" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_HardwareCategories" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Description" TEXT NULL,
                "IsSystem" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_HardwareCategories_Code"
                ON "HardwareCategories" ("Code");
            CREATE TABLE IF NOT EXISTS "HardwareControlProfiles" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_HardwareControlProfiles" PRIMARY KEY AUTOINCREMENT,
                "CategoryId" INTEGER NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "DriverKey" TEXT NOT NULL,
                "RequiredControllerType" TEXT NULL,
                "Description" TEXT NULL,
                "CommandDefinitionsJson" TEXT NULL,
                "IsSystem" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_HardwareControlProfiles_HardwareCategories_CategoryId"
                    FOREIGN KEY ("CategoryId") REFERENCES "HardwareCategories" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_HardwareControlProfiles_Category_Code"
                ON "HardwareControlProfiles" ("CategoryId", "Code");
            CREATE TABLE IF NOT EXISTS "ModuleResourceReservations" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ModuleResourceReservations" PRIMARY KEY AUTOINCREMENT,
                "ModuleId" INTEGER NOT NULL,
                "HardwareInstanceId" INTEGER NOT NULL,
                "AccessMode" TEXT NOT NULL DEFAULT 'Exclusive',
                CONSTRAINT "FK_ModuleResourceReservations_Modules_ModuleId"
                    FOREIGN KEY ("ModuleId") REFERENCES "Modules" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ModuleResourceReservations_HardwareInstances_HardwareInstanceId"
                    FOREIGN KEY ("HardwareInstanceId") REFERENCES "HardwareInstances" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ModuleResourceReservations_Module_Hardware"
                ON "ModuleResourceReservations" ("ModuleId", "HardwareInstanceId");
            CREATE INDEX IF NOT EXISTS "IX_ModuleResourceReservations_HardwareInstanceId"
                ON "ModuleResourceReservations" ("HardwareInstanceId");
            CREATE TABLE IF NOT EXISTS "FlowResourceReservations" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FlowResourceReservations" PRIMARY KEY AUTOINCREMENT,
                "FlowId" INTEGER NOT NULL,
                "HardwareInstanceId" INTEGER NOT NULL,
                "AccessMode" TEXT NOT NULL DEFAULT 'Exclusive',
                "Priority" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_FlowResourceReservations_Flows_FlowId"
                    FOREIGN KEY ("FlowId") REFERENCES "Flows" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_FlowResourceReservations_HardwareInstances_HardwareInstanceId"
                    FOREIGN KEY ("HardwareInstanceId") REFERENCES "HardwareInstances" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FlowResourceReservations_Flow_Hardware"
                ON "FlowResourceReservations" ("FlowId", "HardwareInstanceId");
            CREATE INDEX IF NOT EXISTS "IX_FlowResourceReservations_HardwareInstanceId"
                ON "FlowResourceReservations" ("HardwareInstanceId");
            CREATE TABLE IF NOT EXISTS "FlowParameters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FlowParameters" PRIMARY KEY AUTOINCREMENT,
                "FlowId" INTEGER NOT NULL,
                "Direction" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "ParameterType" TEXT NOT NULL,
                "IsRequired" INTEGER NOT NULL DEFAULT 0,
                "DefaultValueJson" TEXT NULL,
                "Description" TEXT NULL,
                CONSTRAINT "FK_FlowParameters_Flows_FlowId"
                    FOREIGN KEY ("FlowId") REFERENCES "Flows" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FlowParameters_Flow_Direction_Name"
                ON "FlowParameters" ("FlowId", "Direction", "Name");
            CREATE TABLE IF NOT EXISTS "FlowGraphNodes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FlowGraphNodes" PRIMARY KEY AUTOINCREMENT,
                "FlowId" INTEGER NOT NULL,
                "NodeId" TEXT NOT NULL,
                "NodeType" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "ModuleId" INTEGER NULL,
                "SubFlowId" INTEGER NULL,
                "RouteKeyVariable" TEXT NULL,
                "JoinMode" TEXT NOT NULL DEFAULT 'WaitAll',
                "MaxVisits" INTEGER NULL,
                "SubFlowInvocationPolicy" TEXT NOT NULL DEFAULT 'Reentrant',
                "X" REAL NOT NULL DEFAULT 0,
                "Y" REAL NOT NULL DEFAULT 0,
                CONSTRAINT "FK_FlowGraphNodes_Flows_FlowId"
                    FOREIGN KEY ("FlowId") REFERENCES "Flows" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_FlowGraphNodes_Modules_ModuleId"
                    FOREIGN KEY ("ModuleId") REFERENCES "Modules" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_FlowGraphNodes_SubFlows_SubFlowId"
                    FOREIGN KEY ("SubFlowId") REFERENCES "Flows" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FlowGraphNodes_Flow_NodeId"
                ON "FlowGraphNodes" ("FlowId", "NodeId");
            CREATE INDEX IF NOT EXISTS "IX_FlowGraphNodes_ModuleId" ON "FlowGraphNodes" ("ModuleId");
            CREATE INDEX IF NOT EXISTS "IX_FlowGraphNodes_SubFlowId" ON "FlowGraphNodes" ("SubFlowId");
            CREATE TABLE IF NOT EXISTS "FlowSubFlowParameterBindings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FlowSubFlowParameterBindings" PRIMARY KEY AUTOINCREMENT,
                "FlowSubFlowReferenceId" INTEGER NULL,
                "FlowGraphNodeId" INTEGER NULL,
                "Direction" TEXT NOT NULL,
                "ChildParameterName" TEXT NOT NULL,
                "ParentValueName" TEXT NOT NULL,
                CONSTRAINT "FK_FlowSubFlowParameterBindings_References"
                    FOREIGN KEY ("FlowSubFlowReferenceId") REFERENCES "FlowSubFlowReferences" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_FlowSubFlowParameterBindings_GraphNodes"
                    FOREIGN KEY ("FlowGraphNodeId") REFERENCES "FlowGraphNodes" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FlowSubFlowBindings_Reference_Direction_Child"
                ON "FlowSubFlowParameterBindings" ("FlowSubFlowReferenceId", "Direction", "ChildParameterName");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FlowSubFlowBindings_GraphNode_Direction_Child"
                ON "FlowSubFlowParameterBindings" ("FlowGraphNodeId", "Direction", "ChildParameterName");
            CREATE TABLE IF NOT EXISTS "FlowGraphEdges" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FlowGraphEdges" PRIMARY KEY AUTOINCREMENT,
                "FlowId" INTEGER NOT NULL,
                "FromNodeId" INTEGER NOT NULL,
                "ToNodeId" INTEGER NOT NULL,
                "RouteKey" TEXT NULL,
                "IsDefault" INTEGER NOT NULL,
                "Priority" INTEGER NOT NULL,
                CONSTRAINT "FK_FlowGraphEdges_Flows_FlowId"
                    FOREIGN KEY ("FlowId") REFERENCES "Flows" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_FlowGraphEdges_FromNode"
                    FOREIGN KEY ("FromNodeId") REFERENCES "FlowGraphNodes" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_FlowGraphEdges_ToNode"
                    FOREIGN KEY ("ToNodeId") REFERENCES "FlowGraphNodes" ("Id") ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS "IX_FlowGraphEdges_FlowId" ON "FlowGraphEdges" ("FlowId");
            CREATE INDEX IF NOT EXISTS "IX_FlowGraphEdges_FromNodeId" ON "FlowGraphEdges" ("FromNodeId");
            CREATE INDEX IF NOT EXISTS "IX_FlowGraphEdges_ToNodeId" ON "FlowGraphEdges" ("ToNodeId");
            CREATE TABLE IF NOT EXISTS "WorkflowRunSnapshots" (
                "ExecutionId" TEXT NOT NULL CONSTRAINT "PK_WorkflowRunSnapshots" PRIMARY KEY,
                "RecoveredFromExecutionId" TEXT NULL,
                "FlowId" INTEGER NOT NULL,
                "DefinitionVersion" INTEGER NOT NULL,
                "FlowName" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "RecoveryPolicy" TEXT NOT NULL,
                "InputsJson" TEXT NOT NULL,
                "OutputsJson" TEXT NULL,
                "ErrorMessage" TEXT NULL,
                "StartedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "CompletedAtUtc" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_WorkflowRunSnapshots_Status" ON "WorkflowRunSnapshots" ("Status");
            CREATE INDEX IF NOT EXISTS "IX_WorkflowRunSnapshots_FlowId" ON "WorkflowRunSnapshots" ("FlowId");
            CREATE INDEX IF NOT EXISTS "IX_WorkflowRunSnapshots_UpdatedAtUtc" ON "WorkflowRunSnapshots" ("UpdatedAtUtc");
            """,
            ct);

        var columnNames = await GetSqliteColumnNamesAsync(context, "HardwareDefinitions", ct);
        if (!columnNames.Contains("CategoryId"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "HardwareDefinitions" ADD COLUMN "CategoryId" INTEGER NULL;""",
                ct);
        }

        if (!columnNames.Contains("ControlProfileId"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "HardwareDefinitions" ADD COLUMN "ControlProfileId" INTEGER NULL;""",
                ct);
        }

        var recoveryFlowColumns = await GetSqliteColumnNamesAsync(context, "Flows", ct);
        if (!recoveryFlowColumns.Contains("DefinitionVersion"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "Flows" ADD COLUMN "DefinitionVersion" INTEGER NOT NULL DEFAULT 1;""", ct);
        if (!recoveryFlowColumns.Contains("RecoveryPolicy"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "Flows" ADD COLUMN "RecoveryPolicy" TEXT NOT NULL DEFAULT 'NotRecoverable';""", ct);

        var recoveryModuleColumns = await GetSqliteColumnNamesAsync(context, "Modules", ct);
        if (!recoveryModuleColumns.Contains("CompensationTimeoutMs"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "Modules" ADD COLUMN "CompensationTimeoutMs" INTEGER NOT NULL DEFAULT 10000;""", ct);
        if (!recoveryModuleColumns.Contains("RecoveryPolicy"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "Modules" ADD COLUMN "RecoveryPolicy" TEXT NOT NULL DEFAULT 'NotRecoverable';""", ct);

        var compensationStepColumns = await GetSqliteColumnNamesAsync(context, "ModuleSteps", ct);
        if (!compensationStepColumns.Contains("IsCompensation"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "ModuleSteps" ADD COLUMN "IsCompensation" INTEGER NOT NULL DEFAULT 0;""", ct);

        var moduleColumns = await GetSqliteColumnNamesAsync(context, "Modules", ct);
        if (!moduleColumns.Contains("ResourceWaitTimeoutMs"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Modules" ADD COLUMN "ResourceWaitTimeoutMs" INTEGER NOT NULL DEFAULT 30000;""",
                ct);
        }

        var flowColumns = await GetSqliteColumnNamesAsync(context, "Flows", ct);
        if (!flowColumns.Contains("ResourceWaitTimeoutMs"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Flows" ADD COLUMN "ResourceWaitTimeoutMs" INTEGER NOT NULL DEFAULT 30000;""",
                ct);
        }
        if (!flowColumns.Contains("MaxGraphNodeVisits"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Flows" ADD COLUMN "MaxGraphNodeVisits" INTEGER NOT NULL DEFAULT 1000;""",
                ct);
        }

        var stepColumns = await GetSqliteColumnNamesAsync(context, "ModuleSteps", ct);
        if (!stepColumns.Contains("ResourceAccessMode"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "ModuleSteps" ADD COLUMN "ResourceAccessMode" TEXT NOT NULL DEFAULT 'Exclusive';""",
                ct);
        }
        if (!stepColumns.Contains("ResultVariable"))
        {
            await context.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "ModuleSteps" ADD COLUMN "ResultVariable" TEXT NULL;""",
                ct);
        }

        var graphNodeColumns = await GetSqliteColumnNamesAsync(context, "FlowGraphNodes", ct);
        if (!graphNodeColumns.Contains("JoinMode"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "FlowGraphNodes" ADD COLUMN "JoinMode" TEXT NOT NULL DEFAULT 'WaitAll';""", ct);
        if (!graphNodeColumns.Contains("MaxVisits"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "FlowGraphNodes" ADD COLUMN "MaxVisits" INTEGER NULL;""", ct);
        if (!graphNodeColumns.Contains("SubFlowInvocationPolicy"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "FlowGraphNodes" ADD COLUMN "SubFlowInvocationPolicy" TEXT NOT NULL DEFAULT 'Reentrant';""", ct);
        if (!graphNodeColumns.Contains("X"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "FlowGraphNodes" ADD COLUMN "X" REAL NOT NULL DEFAULT 0;""", ct);
        if (!graphNodeColumns.Contains("Y"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "FlowGraphNodes" ADD COLUMN "Y" REAL NOT NULL DEFAULT 0;""", ct);

        var subFlowReferenceColumns = await GetSqliteColumnNamesAsync(context, "FlowSubFlowReferences", ct);
        if (!subFlowReferenceColumns.Contains("InvocationPolicy"))
            await context.Database.ExecuteSqlRawAsync("""ALTER TABLE "FlowSubFlowReferences" ADD COLUMN "InvocationPolicy" TEXT NOT NULL DEFAULT 'Reentrant';""", ct);

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_HardwareDefinitions_CategoryId"
                ON "HardwareDefinitions" ("CategoryId");
            CREATE INDEX IF NOT EXISTS "IX_HardwareDefinitions_ControlProfileId"
                ON "HardwareDefinitions" ("ControlProfileId");
            """,
            ct);
    }

    private static async Task<HashSet<string>> GetSqliteColumnNamesAsync(
        HardwareModularWorkflowDbContext context,
        string tableName,
        CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await command.ExecuteReaderAsync(ct);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(1));
            }

            return names;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static Task InitializeSqlServerSchemaAsync(
        HardwareModularWorkflowDbContext context,
        CancellationToken ct) =>
        context.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[HardwareCategories]', N'U') IS NULL
            BEGIN
                CREATE TABLE [HardwareCategories] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [Code] nvarchar(50) NOT NULL,
                    [Name] nvarchar(100) NOT NULL,
                    [Description] nvarchar(500) NULL,
                    [IsSystem] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL
                );
                CREATE UNIQUE INDEX [IX_HardwareCategories_Code] ON [HardwareCategories]([Code]);
            END;
            IF OBJECT_ID(N'[HardwareControlProfiles]', N'U') IS NULL
            BEGIN
                CREATE TABLE [HardwareControlProfiles] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CategoryId] bigint NOT NULL,
                    [Code] nvarchar(50) NOT NULL,
                    [Name] nvarchar(100) NOT NULL,
                    [DriverKey] nvarchar(100) NOT NULL,
                    [RequiredControllerType] nvarchar(50) NULL,
                    [Description] nvarchar(500) NULL,
                    [CommandDefinitionsJson] nvarchar(max) NULL,
                    [IsSystem] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_HardwareControlProfiles_HardwareCategories_CategoryId]
                        FOREIGN KEY ([CategoryId]) REFERENCES [HardwareCategories]([Id])
                );
                CREATE UNIQUE INDEX [IX_HardwareControlProfiles_Category_Code]
                    ON [HardwareControlProfiles]([CategoryId], [Code]);
            END;
            IF COL_LENGTH(N'HardwareDefinitions', N'CategoryId') IS NULL
                ALTER TABLE [HardwareDefinitions] ADD [CategoryId] bigint NULL;
            IF COL_LENGTH(N'HardwareDefinitions', N'ControlProfileId') IS NULL
                ALTER TABLE [HardwareDefinitions] ADD [ControlProfileId] bigint NULL;
            IF COL_LENGTH(N'Modules', N'ResourceWaitTimeoutMs') IS NULL
                ALTER TABLE [Modules] ADD [ResourceWaitTimeoutMs] int NOT NULL CONSTRAINT [DF_Modules_ResourceWaitTimeoutMs] DEFAULT 30000;
            IF COL_LENGTH(N'Flows', N'ResourceWaitTimeoutMs') IS NULL
                ALTER TABLE [Flows] ADD [ResourceWaitTimeoutMs] int NOT NULL CONSTRAINT [DF_Flows_ResourceWaitTimeoutMs] DEFAULT 30000;
            IF COL_LENGTH(N'Flows', N'MaxGraphNodeVisits') IS NULL
                ALTER TABLE [Flows] ADD [MaxGraphNodeVisits] int NOT NULL CONSTRAINT [DF_Flows_MaxGraphNodeVisits] DEFAULT 1000;
            IF COL_LENGTH(N'ModuleSteps', N'ResourceAccessMode') IS NULL
                ALTER TABLE [ModuleSteps] ADD [ResourceAccessMode] nvarchar(50) NOT NULL CONSTRAINT [DF_ModuleSteps_ResourceAccessMode] DEFAULT N'Exclusive';
            IF COL_LENGTH(N'ModuleSteps', N'ResultVariable') IS NULL
                ALTER TABLE [ModuleSteps] ADD [ResultVariable] nvarchar(100) NULL;
            IF COL_LENGTH(N'FlowSubFlowReferences', N'InvocationPolicy') IS NULL
                ALTER TABLE [FlowSubFlowReferences] ADD [InvocationPolicy] nvarchar(30) NOT NULL CONSTRAINT [DF_FlowSubFlows_InvocationPolicy] DEFAULT N'Reentrant';
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HardwareDefinitions_CategoryId')
                CREATE INDEX [IX_HardwareDefinitions_CategoryId] ON [HardwareDefinitions]([CategoryId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HardwareDefinitions_ControlProfileId')
                CREATE INDEX [IX_HardwareDefinitions_ControlProfileId] ON [HardwareDefinitions]([ControlProfileId]);
            IF OBJECT_ID(N'[ModuleResourceReservations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [ModuleResourceReservations] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ModuleId] bigint NOT NULL,
                    [HardwareInstanceId] bigint NOT NULL,
                    [AccessMode] nvarchar(50) NOT NULL CONSTRAINT [DF_ModuleResourceReservations_AccessMode] DEFAULT N'Exclusive',
                    CONSTRAINT [FK_ModuleResourceReservations_Modules_ModuleId]
                        FOREIGN KEY ([ModuleId]) REFERENCES [Modules]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ModuleResourceReservations_HardwareInstances_HardwareInstanceId]
                        FOREIGN KEY ([HardwareInstanceId]) REFERENCES [HardwareInstances]([Id])
                );
                CREATE UNIQUE INDEX [IX_ModuleResourceReservations_Module_Hardware]
                    ON [ModuleResourceReservations]([ModuleId], [HardwareInstanceId]);
                CREATE INDEX [IX_ModuleResourceReservations_HardwareInstanceId]
                    ON [ModuleResourceReservations]([HardwareInstanceId]);
            END;
            IF OBJECT_ID(N'[FlowResourceReservations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [FlowResourceReservations] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [FlowId] bigint NOT NULL,
                    [HardwareInstanceId] bigint NOT NULL,
                    [AccessMode] nvarchar(50) NOT NULL CONSTRAINT [DF_FlowResourceReservations_AccessMode] DEFAULT N'Exclusive',
                    [Priority] int NOT NULL CONSTRAINT [DF_FlowResourceReservations_Priority] DEFAULT 0,
                    CONSTRAINT [FK_FlowResourceReservations_Flows_FlowId]
                        FOREIGN KEY ([FlowId]) REFERENCES [Flows]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_FlowResourceReservations_HardwareInstances_HardwareInstanceId]
                        FOREIGN KEY ([HardwareInstanceId]) REFERENCES [HardwareInstances]([Id])
                );
                CREATE UNIQUE INDEX [IX_FlowResourceReservations_Flow_Hardware]
                    ON [FlowResourceReservations]([FlowId], [HardwareInstanceId]);
                CREATE INDEX [IX_FlowResourceReservations_HardwareInstanceId]
                    ON [FlowResourceReservations]([HardwareInstanceId]);
            END;
            IF OBJECT_ID(N'[FlowParameters]', N'U') IS NULL
            BEGIN
                CREATE TABLE [FlowParameters] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [FlowId] bigint NOT NULL,
                    [Direction] nvarchar(20) NOT NULL,
                    [Name] nvarchar(100) NOT NULL,
                    [ParameterType] nvarchar(30) NOT NULL,
                    [IsRequired] bit NOT NULL CONSTRAINT [DF_FlowParameters_IsRequired] DEFAULT 0,
                    [DefaultValueJson] nvarchar(max) NULL,
                    [Description] nvarchar(500) NULL,
                    CONSTRAINT [FK_FlowParameters_Flows_FlowId]
                        FOREIGN KEY ([FlowId]) REFERENCES [Flows]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_FlowParameters_Flow_Direction_Name]
                    ON [FlowParameters]([FlowId], [Direction], [Name]);
            END;
            IF OBJECT_ID(N'[FlowGraphNodes]', N'U') IS NULL
            BEGIN
                CREATE TABLE [FlowGraphNodes] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [FlowId] bigint NOT NULL,
                    [NodeId] uniqueidentifier NOT NULL,
                    [NodeType] nvarchar(50) NOT NULL,
                    [Name] nvarchar(100) NOT NULL,
                    [ModuleId] bigint NULL,
                    [SubFlowId] bigint NULL,
                    [RouteKeyVariable] nvarchar(100) NULL,
                    [JoinMode] nvarchar(30) NOT NULL CONSTRAINT [DF_FlowGraphNodes_JoinMode] DEFAULT N'WaitAll',
                    [MaxVisits] int NULL,
                    [SubFlowInvocationPolicy] nvarchar(30) NOT NULL CONSTRAINT [DF_FlowGraphNodes_InvocationPolicy] DEFAULT N'Reentrant',
                    [X] float NOT NULL CONSTRAINT [DF_FlowGraphNodes_X] DEFAULT 0,
                    [Y] float NOT NULL CONSTRAINT [DF_FlowGraphNodes_Y] DEFAULT 0,
                    CONSTRAINT [FK_FlowGraphNodes_Flows_FlowId]
                        FOREIGN KEY ([FlowId]) REFERENCES [Flows]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_FlowGraphNodes_Modules_ModuleId]
                        FOREIGN KEY ([ModuleId]) REFERENCES [Modules]([Id]),
                    CONSTRAINT [FK_FlowGraphNodes_SubFlows_SubFlowId]
                        FOREIGN KEY ([SubFlowId]) REFERENCES [Flows]([Id])
                );
                CREATE UNIQUE INDEX [IX_FlowGraphNodes_Flow_NodeId] ON [FlowGraphNodes]([FlowId], [NodeId]);
                CREATE INDEX [IX_FlowGraphNodes_ModuleId] ON [FlowGraphNodes]([ModuleId]);
                CREATE INDEX [IX_FlowGraphNodes_SubFlowId] ON [FlowGraphNodes]([SubFlowId]);
            END;
            IF COL_LENGTH(N'FlowGraphNodes', N'JoinMode') IS NULL
                ALTER TABLE [FlowGraphNodes] ADD [JoinMode] nvarchar(30) NOT NULL CONSTRAINT [DF_FlowGraphNodes_JoinMode_Upgrade] DEFAULT N'WaitAll';
            IF COL_LENGTH(N'FlowGraphNodes', N'MaxVisits') IS NULL
                ALTER TABLE [FlowGraphNodes] ADD [MaxVisits] int NULL;
            IF COL_LENGTH(N'FlowGraphNodes', N'SubFlowInvocationPolicy') IS NULL
                ALTER TABLE [FlowGraphNodes] ADD [SubFlowInvocationPolicy] nvarchar(30) NOT NULL CONSTRAINT [DF_FlowGraphNodes_InvocationPolicy_Upgrade] DEFAULT N'Reentrant';
            IF COL_LENGTH(N'FlowGraphNodes', N'X') IS NULL
                ALTER TABLE [FlowGraphNodes] ADD [X] float NOT NULL CONSTRAINT [DF_FlowGraphNodes_X_Upgrade] DEFAULT 0;
            IF COL_LENGTH(N'FlowGraphNodes', N'Y') IS NULL
                ALTER TABLE [FlowGraphNodes] ADD [Y] float NOT NULL CONSTRAINT [DF_FlowGraphNodes_Y_Upgrade] DEFAULT 0;
            IF OBJECT_ID(N'[FlowSubFlowParameterBindings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [FlowSubFlowParameterBindings] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [FlowSubFlowReferenceId] bigint NULL,
                    [FlowGraphNodeId] bigint NULL,
                    [Direction] nvarchar(20) NOT NULL,
                    [ChildParameterName] nvarchar(100) NOT NULL,
                    [ParentValueName] nvarchar(100) NOT NULL,
                    CONSTRAINT [FK_FlowSubFlowParameterBindings_References]
                        FOREIGN KEY ([FlowSubFlowReferenceId]) REFERENCES [FlowSubFlowReferences]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_FlowSubFlowParameterBindings_GraphNodes]
                        FOREIGN KEY ([FlowGraphNodeId]) REFERENCES [FlowGraphNodes]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_FlowSubFlowBindings_Reference_Direction_Child]
                    ON [FlowSubFlowParameterBindings]([FlowSubFlowReferenceId], [Direction], [ChildParameterName]);
                CREATE UNIQUE INDEX [IX_FlowSubFlowBindings_GraphNode_Direction_Child]
                    ON [FlowSubFlowParameterBindings]([FlowGraphNodeId], [Direction], [ChildParameterName]);
            END;
            IF OBJECT_ID(N'[FlowGraphEdges]', N'U') IS NULL
            BEGIN
                CREATE TABLE [FlowGraphEdges] (
                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [FlowId] bigint NOT NULL,
                    [FromNodeId] bigint NOT NULL,
                    [ToNodeId] bigint NOT NULL,
                    [RouteKey] nvarchar(100) NULL,
                    [IsDefault] bit NOT NULL,
                    [Priority] int NOT NULL,
                    CONSTRAINT [FK_FlowGraphEdges_Flows_FlowId]
                        FOREIGN KEY ([FlowId]) REFERENCES [Flows]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_FlowGraphEdges_FromNode]
                        FOREIGN KEY ([FromNodeId]) REFERENCES [FlowGraphNodes]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_FlowGraphEdges_ToNode]
                        FOREIGN KEY ([ToNodeId]) REFERENCES [FlowGraphNodes]([Id])
                );
                CREATE INDEX [IX_FlowGraphEdges_FlowId] ON [FlowGraphEdges]([FlowId]);
                CREATE INDEX [IX_FlowGraphEdges_FromNodeId] ON [FlowGraphEdges]([FromNodeId]);
                CREATE INDEX [IX_FlowGraphEdges_ToNodeId] ON [FlowGraphEdges]([ToNodeId]);
            END;
            IF OBJECT_ID(N'[WorkflowRunSnapshots]', N'U') IS NULL
            BEGIN
                CREATE TABLE [WorkflowRunSnapshots] (
                    [ExecutionId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [RecoveredFromExecutionId] uniqueidentifier NULL,
                    [FlowId] bigint NOT NULL,
                    [DefinitionVersion] int NOT NULL,
                    [FlowName] nvarchar(100) NOT NULL,
                    [Status] nvarchar(40) NOT NULL,
                    [RecoveryPolicy] nvarchar(40) NOT NULL,
                    [InputsJson] nvarchar(max) NOT NULL,
                    [OutputsJson] nvarchar(max) NULL,
                    [ErrorMessage] nvarchar(max) NULL,
                    [StartedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    [CompletedAtUtc] datetime2 NULL
                );
                CREATE INDEX [IX_WorkflowRunSnapshots_Status] ON [WorkflowRunSnapshots]([Status]);
                CREATE INDEX [IX_WorkflowRunSnapshots_FlowId] ON [WorkflowRunSnapshots]([FlowId]);
                CREATE INDEX [IX_WorkflowRunSnapshots_UpdatedAtUtc] ON [WorkflowRunSnapshots]([UpdatedAtUtc]);
            END;
            IF COL_LENGTH(N'Flows', N'DefinitionVersion') IS NULL
                ALTER TABLE [Flows] ADD [DefinitionVersion] int NOT NULL CONSTRAINT [DF_Flows_DefinitionVersion_Upgrade] DEFAULT 1;
            IF COL_LENGTH(N'Flows', N'RecoveryPolicy') IS NULL
                ALTER TABLE [Flows] ADD [RecoveryPolicy] nvarchar(40) NOT NULL CONSTRAINT [DF_Flows_RecoveryPolicy_Upgrade] DEFAULT N'NotRecoverable';
            IF COL_LENGTH(N'Modules', N'CompensationTimeoutMs') IS NULL
                ALTER TABLE [Modules] ADD [CompensationTimeoutMs] int NOT NULL CONSTRAINT [DF_Modules_CompensationTimeoutMs_Upgrade] DEFAULT 10000;
            IF COL_LENGTH(N'Modules', N'RecoveryPolicy') IS NULL
                ALTER TABLE [Modules] ADD [RecoveryPolicy] nvarchar(50) NOT NULL CONSTRAINT [DF_Modules_RecoveryPolicy_Upgrade] DEFAULT N'NotRecoverable';
            IF COL_LENGTH(N'ModuleSteps', N'IsCompensation') IS NULL
                ALTER TABLE [ModuleSteps] ADD [IsCompensation] bit NOT NULL CONSTRAINT [DF_ModuleSteps_IsCompensation_Upgrade] DEFAULT 0;
            """,
            ct);

    private static async Task SeedCatalogAsync(
        HardwareModularWorkflowDbContext context,
        CancellationToken ct)
    {
        var categories = new[]
        {
            new HardwareCategory { Code = "Motor", Name = "Motor", Description = "Motion and positioning devices" },
            new HardwareCategory { Code = "Camera", Name = "Camera", Description = "Image acquisition devices" },
            new HardwareCategory { Code = "Temperature", Name = "Temperature", Description = "Temperature control devices" },
            new HardwareCategory { Code = "Cooling", Name = "Cooling", Description = "Cooling control devices" },
            new HardwareCategory { Code = "Infrared", Name = "Infrared", Description = "Infrared sensors and emitters" },
            new HardwareCategory { Code = "Custom", Name = "Custom", Description = "Custom hardware devices" }
        };

        var existingCodes = await context.HardwareCategories
            .Select(category => category.Code)
            .ToListAsync(ct);
        var missingCategories = categories
            .Where(category => !existingCodes.Contains(category.Code, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missingCategories.Count > 0)
        {
            context.HardwareCategories.AddRange(missingCategories);
            await context.SaveChangesAsync(ct);
        }

        var categoryIds = await context.HardwareCategories
            .ToDictionaryAsync(category => category.Code, category => category.Id, StringComparer.OrdinalIgnoreCase, ct);
        var profiles = new[]
        {
            Profile(categoryIds, "Motor", "Generic", "Generic Motor", "Motor.Generic", null, MotorCommands()),
            Profile(categoryIds, "Motor", "PlcAxis", "PLC Axis", "Motor.PlcAxis", "Plc", MotorCommands()),
            Profile(categoryIds, "Motor", "CanMotor", "CAN Motor", "Motor.CanMotor", "Can", MotorCommands()),
            Profile(categoryIds, "Camera", "Generic", "Generic Camera", "Camera.Generic", null, CameraCommands()),
            Profile(categoryIds, "Camera", "GigEVision", "GigE Vision", "Camera.GigEVision", null, CameraCommands()),
            Profile(categoryIds, "Temperature", "Generic", "Generic Temperature", "Temperature.Generic", null, TemperatureCommands()),
            Profile(categoryIds, "Temperature", "PlcModbus", "PLC/Modbus Temperature", "Temperature.PlcModbus", "Plc", TemperatureCommands()),
            Profile(categoryIds, "Cooling", "Generic", "Generic Cooling", "Cooling.Generic", null, TemperatureCommands()),
            Profile(categoryIds, "Cooling", "PlcModbus", "PLC/Modbus Cooling", "Cooling.PlcModbus", "Plc", TemperatureCommands()),
            Profile(categoryIds, "Infrared", "Generic", "Generic Infrared", "Infrared.Generic", null, InfraredCommands()),
            Profile(categoryIds, "Infrared", "PlcDigitalIo", "PLC Digital IO", "Infrared.PlcDigitalIo", "Plc", InfraredCommands()),
            Profile(categoryIds, "Custom", "Generic", "Generic Custom Device", "Custom.Generic", null, BasicCommands())
        };

        var existingProfiles = await context.HardwareControlProfiles
            .Select(profile => new { profile.CategoryId, profile.Code })
            .ToListAsync(ct);
        var missingProfiles = profiles
            .Where(profile => !existingProfiles.Any(existing =>
                existing.CategoryId == profile.CategoryId
                && string.Equals(existing.Code, profile.Code, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missingProfiles.Count > 0)
        {
            context.HardwareControlProfiles.AddRange(missingProfiles);
            await context.SaveChangesAsync(ct);
        }

        var existingSystemProfiles = await context.HardwareControlProfiles
            .Where(profile => profile.IsSystem && profile.CommandDefinitionsJson == null)
            .ToListAsync(ct);
        foreach (var profile in existingSystemProfiles)
        {
            var seedProfile = profiles.FirstOrDefault(item =>
                item.CategoryId == profile.CategoryId
                && string.Equals(item.Code, profile.Code, StringComparison.OrdinalIgnoreCase));
            if (seedProfile is not null)
            {
                profile.CommandDefinitionsJson = seedProfile.CommandDefinitionsJson;
                profile.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (existingSystemProfiles.Count > 0)
        {
            await context.SaveChangesAsync(ct);
        }
    }

    private static HardwareControlProfile Profile(
        IReadOnlyDictionary<string, long> categoryIds,
        string categoryCode,
        string code,
        string name,
        string driverKey,
        string? requiredControllerType,
        string commandDefinitionsJson) =>
        new()
        {
            CategoryId = categoryIds[categoryCode],
            Code = code,
            Name = name,
            DriverKey = driverKey,
            RequiredControllerType = requiredControllerType,
            CommandDefinitionsJson = commandDefinitionsJson
        };

    private static string BasicCommands() => Commands(
        Command("Start"),
        Command("Stop"),
        Command("Reset"),
        Command("GetState", isAsync: false));

    private static string MotorCommands() => Commands(
        Command("Home"),
        Command("MoveTo", Parameter("Position", "number", true), Parameter("Speed", "number", true)),
        Command("Stop"),
        Command("GetState", isAsync: false));

    private static string CameraCommands() => Commands(
        Command("StartGrab"),
        Command("StopGrab"),
        Command("Capture"),
        Command("SetExposure", Parameter("Exposure", "number", true)));

    private static string TemperatureCommands() => Commands(
        Command("SetTemperature", Parameter("TargetTemperature", "number", true)),
        Command("GetTemperature", isAsync: false),
        Command("Stop"));

    private static string InfraredCommands() => Commands(
        Command("ReadValue", isAsync: false),
        Command("SetThreshold", Parameter("Threshold", "number", true)));

    private static string Commands(params HardwareCommandDefinition[] commands) =>
        System.Text.Json.JsonSerializer.Serialize(commands);

    private static HardwareCommandDefinition Command(
        string name,
        params HardwareCommandParameterDefinition[] parameters) =>
        Command(name, isAsync: true, parameters);

    private static HardwareCommandDefinition Command(
        string name,
        bool isAsync,
        params HardwareCommandParameterDefinition[] parameters) =>
        new()
        {
            Name = name,
            DisplayName = name,
            IsAsync = isAsync,
            Parameters = parameters
        };

    private static HardwareCommandParameterDefinition Parameter(
        string name,
        string type,
        bool required) =>
        new() { Name = name, Type = type, Required = required };

    private static async Task MigrateLegacyDefinitionsAsync(
        HardwareModularWorkflowDbContext context,
        CancellationToken ct)
    {
        var categories = await context.HardwareCategories
            .ToDictionaryAsync(category => category.Code, StringComparer.OrdinalIgnoreCase, ct);
        var genericProfiles = await context.HardwareControlProfiles
            .Where(profile => profile.Code == "Generic")
            .ToDictionaryAsync(profile => profile.CategoryId, ct);
        var definitions = await context.HardwareDefinitions
            .Where(definition => !definition.CategoryId.HasValue || !definition.ControlProfileId.HasValue)
            .ToListAsync(ct);

        foreach (var definition in definitions)
        {
            var categoryCode = categories.ContainsKey(definition.Type)
                ? definition.Type
                : "Custom";
            var category = categories[categoryCode];
            definition.CategoryId ??= category.Id;
            definition.ControlProfileId ??= genericProfiles[category.Id].Id;
            definition.Type = category.Code;
            definition.UpdatedAt = DateTime.UtcNow;
        }

        if (definitions.Count > 0)
        {
            await context.SaveChangesAsync(ct);
        }
    }
}
