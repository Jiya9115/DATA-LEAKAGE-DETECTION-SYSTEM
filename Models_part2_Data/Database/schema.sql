-- =====================================================================
-- DLDS reference schema script (SQL Server)
-- -----------------------------------------------------------------------
-- This script is a REFERENCE / fallback covering the DLDS-specific
-- application tables. The primary, supported way to create the database
-- is `dotnet ef database update` (see README.md), which also creates the
-- ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, etc.) that this
-- script intentionally does not duplicate.
--
-- Run manually only if you cannot use the .NET/EF CLI tooling, e.g.:
--   sqlcmd -S localhost,1433 -U sa -P '<password>' -i schema.sql
-- =====================================================================

IF DB_ID('DLDS') IS NULL
BEGIN
    CREATE DATABASE DLDS;
END
GO

USE DLDS;
GO

IF OBJECT_ID('dbo.MonitoredFolders', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MonitoredFolders (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        FolderName      NVARCHAR(200)  NOT NULL,
        FolderPath      NVARCHAR(1024) NOT NULL,
        IsActive        BIT            NOT NULL DEFAULT 0,
        CreatedAt       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        LastMonitoredAt DATETIME2      NULL,
        IsSampleData    BIT            NOT NULL DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_MonitoredFolders_FolderPath ON dbo.MonitoredFolders(FolderPath);
END
GO

IF OBJECT_ID('dbo.FileEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FileEvents (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        FileName            NVARCHAR(260)  NOT NULL,
        FilePath            NVARCHAR(1024) NOT NULL,
        EventType           NVARCHAR(20)   NOT NULL, -- Created/Deleted/Modified/Renamed
        EventTimestamp      DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        FileExtension       NVARCHAR(20)   NULL,
        FileSize            BIGINT         NULL,
        RiskLevel           NVARCHAR(20)   NOT NULL, -- Low/Medium/High/Critical
        RiskScore           INT            NOT NULL DEFAULT 0,
        Description         NVARCHAR(1000) NULL,
        MonitoredFolderId   INT            NOT NULL,
        IsSampleData        BIT            NOT NULL DEFAULT 0,
        CONSTRAINT FK_FileEvents_MonitoredFolders FOREIGN KEY (MonitoredFolderId)
            REFERENCES dbo.MonitoredFolders(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_FileEvents_EventTimestamp ON dbo.FileEvents(EventTimestamp);
    CREATE INDEX IX_FileEvents_RiskLevel ON dbo.FileEvents(RiskLevel);
    CREATE INDEX IX_FileEvents_EventType ON dbo.FileEvents(EventType);
END
GO

IF OBJECT_ID('dbo.Alerts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Alerts (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        FileEventId  INT            NOT NULL,
        Title        NVARCHAR(200)  NOT NULL,
        Message      NVARCHAR(1000) NOT NULL,
        RiskLevel    NVARCHAR(20)   NOT NULL,
        IsRead       BIT            NOT NULL DEFAULT 0,
        CreatedAt    DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        IsSampleData BIT            NOT NULL DEFAULT 0,
        CONSTRAINT FK_Alerts_FileEvents FOREIGN KEY (FileEventId)
            REFERENCES dbo.FileEvents(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_Alerts_IsRead ON dbo.Alerts(IsRead);
    CREATE INDEX IX_Alerts_CreatedAt ON dbo.Alerts(CreatedAt);
END
GO

IF OBJECT_ID('dbo.RiskRules', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RiskRules (
        Id                    INT IDENTITY(1,1) PRIMARY KEY,
        RuleName              NVARCHAR(150) NOT NULL,
        Description           NVARCHAR(500) NULL,
        EventType             NVARCHAR(20)  NULL,
        FileExtension         NVARCHAR(20)  NULL,
        MinimumFileSizeBytes  BIGINT        NULL,
        RiskScore             INT           NOT NULL DEFAULT 0,
        RiskLevel             NVARCHAR(20)  NOT NULL,
        IsActive              BIT           NOT NULL DEFAULT 1,
        IsSampleData          BIT           NOT NULL DEFAULT 0
    );
END
GO

IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        Action      NVARCHAR(150)  NOT NULL,
        Description NVARCHAR(1000) NULL,
        Timestamp   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        Username    NVARCHAR(256)  NOT NULL DEFAULT 'system',
        IPAddress   NVARCHAR(64)   NULL,
        IsSampleData BIT           NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_AuditLogs_Timestamp ON dbo.AuditLogs(Timestamp);
END
GO

IF OBJECT_ID('dbo.AppSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppSettings (
        Id        INT IDENTITY(1,1) PRIMARY KEY,
        [Key]     NVARCHAR(100)  NOT NULL,
        Value     NVARCHAR(2000) NULL,
        UpdatedAt DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppSettings_Key ON dbo.AppSettings([Key]);
END
GO

PRINT 'DLDS reference schema created. ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, etc.) are created separately by `dotnet ef database update`.';
GO
