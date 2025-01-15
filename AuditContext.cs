using Microsoft.EntityFrameworkCore;

namespace OpenTelemetryPoc;

public class AuditContext(DbContextOptions<AuditContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries { get; set; }
}

public class AuditEntry
{
    public long Id { get; private set; }
    public string RawUrl { get; set; }
    public string Method { get; set; }
    public string IpAddress { get; set; }
}