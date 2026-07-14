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
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HardwareDefinitions_CategoryId')
                CREATE INDEX [IX_HardwareDefinitions_CategoryId] ON [HardwareDefinitions]([CategoryId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HardwareDefinitions_ControlProfileId')
                CREATE INDEX [IX_HardwareDefinitions_ControlProfileId] ON [HardwareDefinitions]([ControlProfileId]);
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
