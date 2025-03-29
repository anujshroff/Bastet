IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250330045219_InitialMigration'
)
BEGIN
    CREATE TABLE [DeletedSubnets] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [NetworkAddress] nvarchar(15) NOT NULL,
        [Cidr] int NOT NULL,
        [Description] nvarchar(500) NULL,
        [Tags] nvarchar(255) NULL,
        [OriginalId] int NOT NULL,
        [OriginalParentId] int NULL,
        [DeletedAt] datetime2 NOT NULL,
        [DeletedBy] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastModifiedAt] datetime2 NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_DeletedSubnets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250330045219_InitialMigration'
)
BEGIN
    CREATE TABLE [Subnets] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [NetworkAddress] nvarchar(45) NOT NULL,
        [Cidr] int NOT NULL,
        [Description] nvarchar(500) NULL,
        [Tags] nvarchar(255) NULL,
        [ParentSubnetId] int NULL,
        [RowVersion] rowversion NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastModifiedAt] datetime2 NULL,
        [CreatedBy] nvarchar(max) NULL,
        [ModifiedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Subnets] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Subnet_ValidCidr] CHECK (Cidr >= 0 AND Cidr <= 32),
        CONSTRAINT [FK_Subnets_Subnets_ParentSubnetId] FOREIGN KEY ([ParentSubnetId]) REFERENCES [Subnets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250330045219_InitialMigration'
)
BEGIN
    CREATE INDEX [IX_Subnets_Name] ON [Subnets] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250330045219_InitialMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Subnets_NetworkAddress_Cidr] ON [Subnets] ([NetworkAddress], [Cidr]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250330045219_InitialMigration'
)
BEGIN
    CREATE INDEX [IX_Subnets_ParentSubnetId] ON [Subnets] ([ParentSubnetId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250330045219_InitialMigration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20250330045219_InitialMigration', N'9.0.3');
END;

COMMIT;
GO

