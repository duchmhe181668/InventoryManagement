using Microsoft.EntityFrameworkCore;
using InventoryManagement.Models;

namespace InventoryManagement.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Good> Goods { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // đảm bảo precision khớp DB
            modelBuilder.Entity<Good>(e =>
            {
                e.ToTable("Goods");
                e.Property(p => p.Quantity).HasColumnType("decimal(18,3)");
                e.Property(p => p.PriceCost).HasColumnType("decimal(18,2)");
                e.Property(p => p.PriceSell).HasColumnType("decimal(18,2)");
                e.Property(p => p.CategoryID).HasMaxLength(200);
            });
        }
    }
}
