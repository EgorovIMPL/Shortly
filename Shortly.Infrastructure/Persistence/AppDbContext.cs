using Microsoft.EntityFrameworkCore;
using Shortly.Domain.Entities;

namespace Shortly.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ShortLink> Links => Set<ShortLink>();
    public DbSet<ClickEvent> Clicks => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortLink>(entity =>
        {
            entity.ToTable("short_links");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.Property(x => x.OriginalUrl).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.TotalClicks).IsRequired();

            // уникальный индекс: гарантия отсутствия коллизий кодов на уровне БД
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.ToTable("click_events");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Referer).HasMaxLength(512);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.IpHash).HasMaxLength(64);

            // составной индекс под основной запрос статистики: WHERE link_id = .. AND occurred_at BETWEEN ..
            entity.HasIndex(x => new { x.LinkId, x.OccurredAtUtc });

            entity.HasOne<ShortLink>()
                .WithMany()
                .HasForeignKey(x => x.LinkId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}