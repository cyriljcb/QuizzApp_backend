using Microsoft.EntityFrameworkCore;
using QuizzBackend.Models;
namespace QuizzBackend.Data;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Question> Questions => Set<Question>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Question>(entity =>
        {
            entity.HasKey(q => q.Id);

            entity.Property(q => q.ExternalId)
                  .IsRequired()
                  .HasMaxLength(20);

            entity.HasIndex(q => q.ExternalId)
                  .IsUnique();

            entity.HasIndex(q => q.Theme);
            entity.HasIndex(q => q.Difficulty);

            // EF Core ne sait pas stocker List<string> nativement en SQLite
            // On sérialise en JSON dans une colonne texte
            entity.Property(q => q.AcceptedAnswers)
                  .HasConversion(
                      v => string.Join("||", v),
                      v => v.Split("||", StringSplitOptions.RemoveEmptyEntries).ToList()
                  );

            entity.Property(q => q.Tags)
                  .HasConversion(
                      v => string.Join("||", v),
                      v => v.Split("||", StringSplitOptions.RemoveEmptyEntries).ToList()
                  );
        });
    }
}