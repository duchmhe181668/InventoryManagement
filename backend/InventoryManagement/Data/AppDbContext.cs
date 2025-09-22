using Microsoft.EntityFrameworkCore;
using InventoryManagement.Models;

namespace InventoryManagement.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Store> Stores { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Good> Goods { get; set; }
        public DbSet<Receipt> Receipts { get; set; }
        public DbSet<ReceiptDetail> ReceiptDetails { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<Outbound> Outbounds { get; set; }
        public DbSet<OutboundDetail> OutboundDetails { get; set; }
        public DbSet<Report> Reports { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<OrderDetail>()
                .HasKey(od => new { od.OrderID, od.GoodID });

            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Order)
                .WithMany(o => o.OrderDetails)
                .HasForeignKey(od => od.OrderID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Store)
                .WithMany(s => s.OrderDetails)
                .HasForeignKey(od => od.StoreID)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ReceiptDetail>()
        .HasKey(rd => new { rd.ReceiptID, rd.GoodID });

            modelBuilder.Entity<OrderDetail>()
                .HasKey(od => new { od.OrderID, od.GoodID });

            modelBuilder.Entity<OutboundDetail>()
                .HasKey(od => new { od.OutboundID, od.GoodID });
            modelBuilder.Entity<Good>(e =>
            {
                e.ToTable("Goods");
                e.Property(p => p.Quantity).HasColumnType("decimal(18,3)");
                e.Property(p => p.PriceCost).HasColumnType("decimal(18,2)");
                e.Property(p => p.PriceSell).HasColumnType("decimal(18,2)");
            });

        }
    }
}
