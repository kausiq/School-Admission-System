using Microsoft.EntityFrameworkCore;

namespace Backend;

public class SchoolClass
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int TotalSeats { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Hold
{
    public string Id { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string StudentId { get; set; } = "";
    // HELD | CONFIRMED | EXPIRED | CANCELLED (HELD past ExpiresAt is treated as EXPIRED everywhere)
    public string Status { get; set; } = "HELD";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Hold> Holds => Set<Hold>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SchoolClass>(e => { e.ToTable("Classes"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200); });
        b.Entity<Hold>(e =>
        {
            e.ToTable("Holds");
            e.HasKey(x => x.Id);
            e.Property(x => x.StudentId).HasMaxLength(17);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt });
            e.HasIndex(x => x.StudentId);
            // DB-level backstop for rule: one active/confirmed application per student per class
            e.HasIndex(x => new { x.ClassId, x.StudentId }).IsUnique()
                .HasFilter("\"Status\" IN ('HELD','CONFIRMED')");
        });
    }
}

public record CreateClassReq(string? Name, int? TotalSeats);
