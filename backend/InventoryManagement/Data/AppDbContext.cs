using Microsoft.EntityFrameworkCore;
using InventoryManagement.Models;

namespace InventoryManagement.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // DbSets
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
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

            // ===== Composite Keys =====
            modelBuilder.Entity<ReceiptDetail>()
                .HasKey(rd => new { rd.ReceiptID, rd.GoodID });

            modelBuilder.Entity<OrderDetail>()
                .HasKey(od => new { od.OrderID, od.GoodID });

            modelBuilder.Entity<OutboundDetail>()
                .HasKey(od => new { od.OutboundID, od.GoodID });

            // ===== Relationships =====

            // Store - User (1-1)
            modelBuilder.Entity<Store>()
                .HasOne(s => s.User)
                .WithOne(u => u.Store)
                .HasForeignKey<Store>(s => s.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // Good - Category
            modelBuilder.Entity<Good>()
                .HasOne(g => g.Category)
                .WithMany(c => c.Goods)
                .HasForeignKey(g => g.CategoryID)
                .OnDelete(DeleteBehavior.Restrict);

            // Good - Store
            modelBuilder.Entity<Good>()
                .HasOne(g => g.Store)
                .WithMany(s => s.Goods)
                .HasForeignKey(g => g.StoreID)
                .OnDelete(DeleteBehavior.Restrict);

            // Good - Supplier
            modelBuilder.Entity<Good>()
                .HasOne(g => g.Supplier)
                .WithMany(s => s.Goods)
                .HasForeignKey(g => g.SupplierID)
                .OnDelete(DeleteBehavior.Restrict);

            // ReceiptDetail
            modelBuilder.Entity<ReceiptDetail>()
                .HasOne(rd => rd.Receipt)
                .WithMany(r => r.ReceiptDetails)
                .HasForeignKey(rd => rd.ReceiptID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ReceiptDetail>()
                .HasOne(rd => rd.Good)
                .WithMany(g => g.ReceiptDetails)
                .HasForeignKey(rd => rd.GoodID)
                .OnDelete(DeleteBehavior.Restrict);

            // OrderDetail
            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Order)
                .WithMany(o => o.OrderDetails)
                .HasForeignKey(od => od.OrderID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Store)
                .WithMany(s => s.OrderDetails)
                .HasForeignKey(od => od.StoreID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Good)
                .WithMany(g => g.OrderDetails)
                .HasForeignKey(od => od.GoodID)
                .OnDelete(DeleteBehavior.Restrict);

            // OutboundDetail
            modelBuilder.Entity<OutboundDetail>()
                .HasOne(od => od.Outbound)
                .WithMany(o => o.OutboundDetails)
                .HasForeignKey(od => od.OutboundID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OutboundDetail>()
                .HasOne(od => od.Good)
                .WithMany(g => g.OutboundDetails)
                .HasForeignKey(od => od.GoodID)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
