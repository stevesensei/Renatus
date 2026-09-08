using Microsoft.EntityFrameworkCore;
using Renatus.Host.Entity;

namespace Renatus.Host.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAuthenticationMethod> UserAuthenticationMethods => Set<UserAuthenticationMethod>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Texture> Textures => Set<Texture>();
    public DbSet<UserTextureFavorite> UserTextureFavorites => Set<UserTextureFavorite>();
    public DbSet<YggdrasilClient> YggdrasilClients => Set<YggdrasilClient>();
    public DbSet<AppConfiguration> AppConfigurations => Set<AppConfiguration>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.UserName).HasMaxLength(64).IsRequired();
            entity.Property(user => user.NormalizedUserName).HasMaxLength(64).IsRequired();
            entity.Property(user => user.NickName).HasMaxLength(64).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(256).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.Property(user => user.AvatarBase64).HasColumnType("text");
            entity.Property(user => user.PreferredLanguage).HasMaxLength(16).IsRequired();
            entity.Property(user => user.Role)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(UserRole.Player)
                .IsRequired();
            entity.Property(user => user.MaxPlayerCount).HasDefaultValue(3).IsRequired();
            entity.HasIndex(user => user.NormalizedUserName).IsUnique();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<UserAuthenticationMethod>(entity =>
        {
            entity.HasKey(method => method.Id);
            entity.Property(method => method.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(method => method.Provider).HasMaxLength(64);
            entity.Property(method => method.Subject).HasMaxLength(256);
            entity.HasOne(method => method.User)
                .WithMany(user => user.AuthenticationMethods)
                .HasForeignKey(method => method.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(method => new { method.Type, method.Provider, method.Subject }).IsUnique();
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(player => player.Id);
            entity.Property(player => player.Name).HasMaxLength(16).IsRequired();
            entity.Property(player => player.NormalizedName).HasMaxLength(16).IsRequired();
            entity.Property(player => player.SkinModel).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(player => player.NormalizedName).IsUnique();
            entity.HasOne(player => player.User)
                .WithMany(user => user.Players)
                .HasForeignKey(player => player.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(player => player.SkinTexture)
                .WithMany()
                .HasForeignKey(player => player.SkinTextureId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(player => player.CapeTexture)
                .WithMany()
                .HasForeignKey(player => player.CapeTextureId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Texture>(entity =>
        {
            entity.HasKey(texture => texture.Id);
            entity.Property(texture => texture.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(texture => texture.Name).HasMaxLength(80).IsRequired();
            entity.Property(texture => texture.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(texture => texture.DescriptionMarkdown).HasMaxLength(4000).IsRequired();
            entity.Property(texture => texture.SkinModel).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(texture => texture.DataBase64).IsRequired();
            entity.Property(texture => texture.ContentType).HasMaxLength(64).IsRequired();
            entity.Property(texture => texture.Sha256).HasMaxLength(64);
            entity.HasOne(texture => texture.User)
                .WithMany(user => user.Textures)
                .HasForeignKey(texture => texture.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(texture => texture.UserId);
            entity.HasIndex(texture => new { texture.IsPublic, texture.Type, texture.CreatedAt });
        });

        modelBuilder.Entity<UserTextureFavorite>(entity =>
        {
            entity.HasKey(favorite => new { favorite.UserId, favorite.TextureId });
            entity.HasOne(favorite => favorite.User)
                .WithMany()
                .HasForeignKey(favorite => favorite.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(favorite => favorite.Texture)
                .WithMany()
                .HasForeignKey(favorite => favorite.TextureId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(favorite => favorite.TextureId);
        });

        modelBuilder.Entity<YggdrasilClient>(entity =>
        {
            entity.HasKey(client => client.Id);
            entity.Property(client => client.ClientToken).HasMaxLength(128).IsRequired();
            entity.Property(client => client.AuthenticationMethod).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(client => new { client.UserId, client.ClientToken }).IsUnique();
            entity.HasOne(client => client.User)
                .WithMany(user => user.Clients)
                .HasForeignKey(client => client.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(client => client.Player)
                .WithMany(player => player.Clients)
                .HasForeignKey(client => client.PlayerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppConfiguration>(entity =>
        {
            entity.HasKey(configuration => configuration.Key);
            entity.Property(configuration => configuration.Key).HasMaxLength(128).IsRequired();
            entity.Property(configuration => configuration.Value).HasColumnType("jsonb").IsRequired();
            entity.Property(configuration => configuration.UpdatedAt).IsRequired();
        });
    }

    private void ApplyTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<UserAuthenticationMethod>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Player>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Texture>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<UserTextureFavorite>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<YggdrasilClient>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AppConfiguration>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
