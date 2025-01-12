using Microsoft.EntityFrameworkCore;

namespace OpenTelemetryPoc;

public class AuditContext : DbContext
{
    public DbSet<AuditEntry> AuditEntries { get; set; }
    
    public string DbPath { get; }
    
    public AuditContext()
    {
        // https://learn.microsoft.com/nl-nl/ef/core/get-started/overview/first-app?tabs=netcore-cli#create-the-database
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DbPath = Path.Join(path, "blogging.db");
    }
    
    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");
}

public class AuditEntry
{
    public long Id { get; private set; }
    public string RawUrl { get; set; }
    public string Method { get; set; }
    public string IpAddress { get; set; }
}