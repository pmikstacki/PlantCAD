using System.Globalization;
using System.Reflection;
using Dapper;

namespace PlantCad.Core.Data;

public sealed class MigrationRunner
{
    private readonly ISqliteConnectionFactory _factory;

    public MigrationRunner(ISqliteConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void ApplyPendingMigrations()
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Bootstrap schema_version if not exists (idempotent)
        conn.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);", transaction: tx);

        var applied = conn.Query<int>("SELECT version FROM schema_version ORDER BY version;", transaction: tx).ToHashSet();
        var (orderedResources, versions) = GetOrderedMigrationResources();

        for (var i = 0; i < orderedResources.Count; i++)
        {
            var version = versions[i];
            if (applied.Contains(version))
                continue;

            var sql = ReadEmbeddedResource(orderedResources[i]);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"Migration resource '{orderedResources[i]}' is empty.");

            conn.Execute(sql, transaction: tx);
            conn.Execute(
                "INSERT INTO schema_version(version, applied_utc) VALUES (@v, @t);",
                new { v = version, t = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                transaction: tx);
        }

        tx.Commit();
    }

    private static (List<string> resources, List<int> versions) GetOrderedMigrationResources()
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames();
        var list = new List<(string name, int version)>();
        foreach (var n in names)
        {
            if (!n.Contains(".Data.Migrations.")) continue;
            // Resource name example: PlantCad.Core.Data.Migrations.001_init.sql
            var segments = n.Split('.');
            if (segments.Length < 2) continue;
            var fileBase = segments[^2]; // "001_init"
            var prefix = fileBase.Split('_').FirstOrDefault();
            if (int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                list.Add((n, v));
            }
        }
        list.Sort((a, b) => a.version.CompareTo(b.version));
        return (list.Select(x => x.name).ToList(), list.Select(x => x.version).ToList());
    }

    private static string ReadEmbeddedResource(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
