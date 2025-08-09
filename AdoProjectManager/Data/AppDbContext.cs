using Microsoft.EntityFrameworkCore;
using AdoProjectManager.Models;

namespace AdoProjectManager.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UserSettings> UserSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrganizationUrl).IsRequired();
            entity.Property(e => e.DefaultClonePath).IsRequired();
            entity.Property(e => e.AuthType).HasConversion<string>();
        });
    }
}

public class UserSettings
{
    public int Id { get; set; }
    public string OrganizationUrl { get; set; } = string.Empty;
    public string PersonalAccessToken { get; set; } = string.Empty;
    public string DefaultClonePath { get; set; } = @"C:\Projects";
    public AuthenticationType AuthType { get; set; } = AuthenticationType.PersonalAccessToken;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
