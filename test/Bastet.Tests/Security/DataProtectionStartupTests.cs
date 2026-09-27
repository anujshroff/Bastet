using System.Text.RegularExpressions;

namespace Bastet.Tests.Security;

public partial class DataProtectionStartupTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string ProgramFile = "src/Bastet/Program.cs";

    [GeneratedRegex(
        @"GetRequiredService<IDataProtectionProvider>\(\)\s*\.CreateProtector\(""Bastet\.KeyRingStartup""\)\s*\.Protect\(",
        RegexOptions.Singleline)]
    private static partial Regex KeyRingForcePattern();

    [GeneratedRegex(@"""@LockTimeout"",\s*(?<ms>\d+)")]
    private static partial Regex LockTimeoutPattern();

    [GeneratedRegex(@"AddWithValue\(""@Resource"", keyRingLockResource\)")]
    private static partial Regex ResourceBindingPattern();

    [GeneratedRegex(@"""@Resource""")]
    private static partial Regex ResourceParameterPattern();

    [GeneratedRegex(@"catch\s*\(Exception[^)]*\)\s*\{[^}]*LogWarning", RegexOptions.Singleline)]
    private static partial Regex LoggedCatchPattern();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory");
    }

    private static string ReadProgram() =>
        File.ReadAllText(Path.Combine(RepoRoot, ProgramFile.Replace('/', Path.DirectorySeparatorChar)));

    private static string WarmUpBlock()
    {
        string program = ReadProgram();
        int start = program.IndexOf("string keyRingLockResource", StringComparison.Ordinal);
        Assert.True(start >= 0, "the key-ring warm-up block was not found in Program.cs");

        int end = program.IndexOf("app.UseForwardedHeaders();", start, StringComparison.Ordinal);
        Assert.True(end > start, "the key-ring warm-up block does not precede app.UseForwardedHeaders()");

        return program[start..end];
    }

    private static int IndexOfOrFail(string haystack, string needle, string what)
    {
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"{what} was not found in the key-ring warm-up block");
        return index;
    }

    [Fact]
    public void TheKeyRingIsForced_WhileTheLockIsHeld()
    {
        string block = WarmUpBlock();

        int acquire = IndexOfOrFail(block, "acquireKeyRingLock.ExecuteNonQuery();", "the lock acquisition");
        int release = IndexOfOrFail(block, "new(\"sp_releaseapplock\"", "the lock release");

        Match force = KeyRingForcePattern().Match(block);
        Assert.True(force.Success, "the key ring is never forced");

        Assert.True(
            acquire < force.Index,
            "the key ring must be forced AFTER the lock is acquired, or replicas are not serialised");
        Assert.True(
            force.Index < release,
            "the key ring must be forced BEFORE the lock is released, or replicas are not serialised");
    }

    [Fact]
    public void TheLock_IsExclusiveSessionOwned_AndWaitsForTheOtherReplica()
    {
        string block = WarmUpBlock();

        Assert.Contains("sp_getapplock", block);
        Assert.Contains("\"Exclusive\"", block);
        Assert.Contains("\"Session\"", block);
        Assert.Contains("sp_releaseapplock", block);

        Match timeout = LockTimeoutPattern().Match(block);
        Assert.True(timeout.Success, "the lock must carry an explicit timeout");
        Assert.True(
            int.Parse(timeout.Groups["ms"].Value) >= 1000,
            "a zero or near-zero timeout makes the losing replica fall through unserialised");
    }

    [Fact]
    public void TheLockResource_IsSharedAcrossReplicas_AndIsNotTheMigrationLock()
    {
        string block = WarmUpBlock();

        Assert.Contains("\"Bastet:DataProtection\"", block);
        Assert.DoesNotContain("Bastet:Migration", block);
        Assert.DoesNotContain("migrationLockResource", block);
        Assert.DoesNotContain("OpenMigrationLockConnection", block);

        string resource = block[..IndexOfOrFail(block, "try", "the warm-up try block")];
        Assert.DoesNotContain("MachineName", resource);
        Assert.DoesNotContain("Guid", resource);
        Assert.DoesNotContain("ProcessId", resource);
        Assert.DoesNotContain("Environment.", resource);
    }

    [Fact]
    public void TheLockResource_DoesNotSpellTheCatalog_SoReplicasWhoseConnectionStringsDifferOnlyInCaseStillMeet()
    {
        string block = WarmUpBlock();
        string resource = block[..IndexOfOrFail(block, "try", "the warm-up try block")];

        Assert.Matches(@"string keyRingLockResource = ""Bastet:DataProtection"";", resource);
        Assert.DoesNotContain("InitialCatalog", resource);
        Assert.DoesNotContain("connectionString", resource);
        Assert.DoesNotContain("$\"", resource);
        Assert.Equal(2, ResourceBindingPattern().Count(block));
        Assert.Equal(2, ResourceParameterPattern().Count(block));
    }

    [Fact]
    public void TheWarmUp_IsBestEffort_SoADatabaseProblemStillLetsTheAppStart()
    {
        string block = WarmUpBlock();

        Assert.Matches(LoggedCatchPattern(), block);
        Assert.DoesNotContain("throw", block);
        Assert.DoesNotContain("Startup was aborted", block);
    }

    [Fact]
    public void TheWarmUp_OnlyRuns_WhenKeysArePersisted()
    {
        string program = ReadProgram();
        int warmUp = IndexOfOrFail(program, "string keyRingLockResource", "the warm-up block");
        int guard = program.LastIndexOf("if (dataProtectionTableExists)", warmUp, StringComparison.Ordinal);

        Assert.True(guard >= 0, "the warm-up must sit inside an if (dataProtectionTableExists) guard");
        Assert.DoesNotContain("}", program[guard..warmUp]);
    }

    [Fact]
    public void TheWarmUp_RunsAfterMigrations_AndBeforeTheAppServesRequests()
    {
        string program = ReadProgram();

        int migrate = IndexOfOrFail(program, "dpContext.Database.Migrate();", "the DataProtection migration");
        int warmUp = IndexOfOrFail(program, "string keyRingLockResource", "the warm-up block");
        int run = IndexOfOrFail(program, "app.Run();", "app.Run()");

        Assert.True(warmUp > migrate, "the warm-up must run after the DataProtection context is migrated");
        Assert.True(warmUp < run, "the warm-up must run before the app starts serving");
    }
}
