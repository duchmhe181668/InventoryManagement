using InventoryManagement.Models;
using InventoryManagement.Models.Views;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Role> Roles => Set<Role>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Good> Goods => Set<Good>();
        public DbSet<Location> Locations => Set<Location>();
        public DbSet<Store> Stores => Set<Store>();
        public DbSet<Batch> Batches => Set<Batch>();
        public DbSet<StorePrice> StorePrices => Set<StorePrice>();
        public DbSet<Stock> Stocks => Set<Stock>();
        public DbSet<StockMovement> StockMovements => Set<StockMovement>();
        public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
        public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
        public DbSet<Receipt> Receipts => Set<Receipt>();
        public DbSet<ReceiptDetail> ReceiptDetails => Set<ReceiptDetail>();
        public DbSet<Transfer> Transfers => Set<Transfer>();
        public DbSet<TransferItem> TransferItems => Set<TransferItem>();
        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<SaleLine> SaleLines => Set<SaleLine>();
        public DbSet<Reservation> Reservations => Set<Reservation>();
        public DbSet<Adjustment> Adjustments => Set<Adjustment>();
        public DbSet<AdjustmentLine> AdjustmentLines => Set<AdjustmentLine>();
        public DbSet<PriceChange> PriceChanges => Set<PriceChange>();

        // Views
        public DbSet<StockAvailableView> StockAvailable => Set<StockAvailableView>();
        public DbSet<StockByGoodView> StockByGood => Set<StockByGoodView>();

        protected override void OnModelCreating(ModelBuilder model)
        {
            // Unique indexes & constraints
            model.Entity<Role>().HasIndex(x => x.RoleName).IsUnique();

            model.Entity<User>().HasIndex(x => x.Username).IsUnique();
            model.Entity<User>().Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

            model.Entity<Category>().HasIndex(x => x.CategoryName).IsUnique();

            model.Entity<Good>().HasIndex(x => x.SKU).IsUnique();
            model.Entity<Good>().HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            model.Entity<Good>().Property(x => x.PriceCost).HasColumnType("decimal(18,2)").HasDefaultValue(0);
            model.Entity<Good>().Property(x => x.PriceSell).HasColumnType("decimal(18,2)").HasDefaultValue(0);
            model.Entity<Good>().Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            model.Entity<Good>()
                .HasOne(g => g.Category).WithMany(c => c.Goods)
                .HasForeignKey(g => g.CategoryID).OnDelete(DeleteBehavior.SetNull);

            model.Entity<Location>().HasIndex(x => x.LocationType);
            model.Entity<Location>()
                .HasOne(x => x.Parent).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentLocationID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Location>().Property(x => x.IsActive).HasDefaultValue(true);
            model.Entity<Location>()
                .ToTable(tb => tb.HasCheckConstraint("CK_Locations_LocationType",
                    "[LocationType] IN ('WAREHOUSE','STORE','BIN')"));

            model.Entity<Store>()
                .HasOne(s => s.Location).WithOne()
                .HasForeignKey<Store>(s => s.LocationID)
                .OnDelete(DeleteBehavior.Cascade);
            model.Entity<Store>().HasIndex(s => s.LocationID).IsUnique();

            model.Entity<Batch>()
                .HasIndex(x => new { x.GoodID, x.BatchNo }).IsUnique();

            // Composite keys
            model.Entity<StorePrice>().HasKey(x => new { x.StoreID, x.GoodID, x.EffectiveFrom });
            model.Entity<StorePrice>().Property(x => x.PriceSell).HasColumnType("decimal(18,2)");
            model.Entity<StorePrice>().Property(x => x.EffectiveFrom).HasColumnType("date");

            model.Entity<Stock>().HasKey(x => new { x.LocationID, x.GoodID, x.BatchID });
            model.Entity<Stock>().Property(x => x.OnHand).HasColumnType("decimal(18,2)").HasDefaultValue(0);
            model.Entity<Stock>().Property(x => x.Reserved).HasColumnType("decimal(18,2)").HasDefaultValue(0);
            model.Entity<Stock>().Property(x => x.InTransit).HasColumnType("decimal(18,2)").HasDefaultValue(0);
            model.Entity<Stock>().Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            model.Entity<Stock>().HasIndex(x => x.GoodID);
            model.Entity<Stock>()
                .ToTable(tb => tb.HasCheckConstraint("CK_Stocks_NonNegative",
                    "[OnHand] >= 0 AND [Reserved] >= 0 AND [InTransit] >= 0"));

            model.Entity<StockMovement>()
                .Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            model.Entity<StockMovement>().Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            model.Entity<StockMovement>().Property(x => x.UnitCost).HasColumnType("decimal(18,2)");
            model.Entity<StockMovement>().HasIndex(x => new { x.GoodID, x.CreatedAt });
            model.Entity<StockMovement>().HasIndex(x => new { x.FromLocationID, x.ToLocationID });
            model.Entity<StockMovement>()
                .ToTable(tb => tb.HasCheckConstraint("CK_StockMovements_Type",
                    "[MovementType] IN ('RECEIPT','SALE','RETURN','TRANSFER_SHIP','TRANSFER_RECEIVE','ADJUST_POS','ADJUST_NEG')"));
            model.Entity<StockMovement>()
                .HasOne(x => x.Good).WithMany(g => g.StockMovements)
                .HasForeignKey(x => x.GoodID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<StockMovement>()
                .HasOne(x => x.FromLocation).WithMany()
                .HasForeignKey(x => x.FromLocationID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<StockMovement>()
                .HasOne(x => x.ToLocation).WithMany()
                .HasForeignKey(x => x.ToLocationID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<StockMovement>()
                .HasOne(x => x.Batch).WithMany()
                .HasForeignKey(x => x.BatchID).OnDelete(DeleteBehavior.Restrict);

            model.Entity<PurchaseOrder>()
                .Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            model.Entity<PurchaseOrder>()
                .ToTable(tb => tb.HasCheckConstraint("CK_PurchaseOrders_Status",
                    "[Status] IN ('Draft','Submitted','Received','Cancelled')"));
            model.Entity<PurchaseOrder>()
                .HasOne(x => x.Supplier).WithMany(s => s.PurchaseOrders)
                .HasForeignKey(x => x.SupplierID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<PurchaseOrder>()
                .HasOne(x => x.CreatedByUser).WithMany()
                .HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);

            model.Entity<PurchaseOrderLine>()
                .Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            model.Entity<PurchaseOrderLine>()
                .Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
            model.Entity<PurchaseOrderLine>()
                .HasOne(x => x.PO).WithMany(p => p.Lines)
                .HasForeignKey(x => x.POID).OnDelete(DeleteBehavior.Cascade);
            model.Entity<PurchaseOrderLine>()
                .HasOne(x => x.Good).WithMany()
                .HasForeignKey(x => x.GoodID).OnDelete(DeleteBehavior.Restrict);

            model.Entity<Receipt>()
                .Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            model.Entity<Receipt>()
                .ToTable(tb => tb.HasCheckConstraint("CK_Receipts_Status",
                    "[Status] IN ('Draft','Confirmed')"));
            model.Entity<Receipt>()
                .HasOne(x => x.PO).WithMany()
                .HasForeignKey(x => x.POID).OnDelete(DeleteBehavior.SetNull);
            model.Entity<Receipt>()
                .HasOne(x => x.Supplier).WithMany()
                .HasForeignKey(x => x.SupplierID).OnDelete(DeleteBehavior.SetNull);
            model.Entity<Receipt>()
                .HasOne(x => x.Location).WithMany()
                .HasForeignKey(x => x.LocationID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Receipt>()
                .HasOne(x => x.ReceivedByUser).WithMany()
                .HasForeignKey(x => x.ReceivedBy).OnDelete(DeleteBehavior.Restrict);

            model.Entity<ReceiptDetail>()
                .Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            model.Entity<ReceiptDetail>()
                .Property(x => x.UnitCost).HasColumnType("decimal(18,2)");
            model.Entity<ReceiptDetail>()
                .HasOne(x => x.Receipt).WithMany(r => r.Details)
                .HasForeignKey(x => x.ReceiptID).OnDelete(DeleteBehavior.Cascade);
            model.Entity<ReceiptDetail>()
                .HasOne(x => x.Good).WithMany()
                .HasForeignKey(x => x.GoodID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<ReceiptDetail>()
                .HasOne(x => x.Batch).WithMany()
                .HasForeignKey(x => x.BatchID).OnDelete(DeleteBehavior.Restrict);

            model.Entity<Transfer>()
                .Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            model.Entity<Transfer>()
                .ToTable(tb => tb.HasCheckConstraint("CK_Transfers_Status",
                    "[Status] IN ('Draft','Approved','Shipped','Received','Cancelled')"));
            model.Entity<Transfer>()
                .HasOne(x => x.FromLocation).WithMany()
                .HasForeignKey(x => x.FromLocationID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Transfer>()
                .HasOne(x => x.ToLocation).WithMany()
                .HasForeignKey(x => x.ToLocationID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Transfer>()
                .HasOne(x => x.CreatedByUser).WithMany()
                .HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);

            model.Entity<TransferItem>()
                .Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            model.Entity<TransferItem>()
                .Property(x => x.ShippedQty).HasColumnType("decimal(18,2)");
            model.Entity<TransferItem>()
                .Property(x => x.ReceivedQty).HasColumnType("decimal(18,2)");
            model.Entity<TransferItem>()
                .HasOne(x => x.Transfer).WithMany(t => t.Items)
                .HasForeignKey(x => x.TransferID).OnDelete(DeleteBehavior.Cascade);
            model.Entity<TransferItem>()
                .HasOne(x => x.Good).WithMany()
                .HasForeignKey(x => x.GoodID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<TransferItem>()
                .HasOne(x => x.Batch).WithMany()
                .HasForeignKey(x => x.BatchID).OnDelete(DeleteBehavior.Restrict);

            model.Entity<Sale>()
                .Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            model.Entity<Sale>()
                .ToTable(tb => tb.HasCheckConstraint("CK_Sales_Status",
                    "[Status] IN ('Draft','Completed','Cancelled')"));
            model.Entity<Sale>()
                .HasOne(x => x.StoreLocation).WithMany()
                .HasForeignKey(x => x.StoreLocationID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Sale>()
                .HasOne(x => x.Customer).WithMany()
                .HasForeignKey(x => x.CustomerID).OnDelete(DeleteBehavior.SetNull);
            model.Entity<Sale>()
                .HasOne(x => x.CreatedByUser).WithMany()
                .HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);

            model.Entity<SaleLine>()
                .Property(x => x.Quantity).HasColumnType("decimal(18,2)");
            model.Entity<SaleLine>()
                .Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
            model.Entity<SaleLine>()
                .HasOne(x => x.Sale).WithMany(s => s.Lines)
                .HasForeignKey(x => x.SaleID).OnDelete(DeleteBehavior.Cascade);
            model.Entity<SaleLine>()
                .HasOne(x => x.Good).WithMany()
                .HasForeignKey(x => x.GoodID).OnDelete(DeleteBehavior.Restrict);
            model.Entity<SaleLine>()
                .HasOne(x => x.Batch).WithMany()
                .HasForeignKey(x => x.BatchID).OnDelete(DeleteBehavior.Restrict);

            model.Entity<Reservation>()
                .HasIndex(x => new { x.LocationID, x.GoodID });
            model.Entity<Reservation>()
                .Property(x => x.CreatedAt).HasColumnType("datetime2(0)");
            model.Entity<Reservation>()
                .Property(x => x.ExpiresAt).HasColumnType("datetime2(0)");

            model.Entity<Adjustment>()
                .Property(x => x.CreatedAt).HasColumnType("datetime2(0)");

            model.Entity<AdjustmentLine>()
                .Property(x => x.QuantityDelta).HasColumnType("decimal(18,2)");
            model.Entity<AdjustmentLine>()
                .Property(x => x.UnitCost).HasColumnType("decimal(18,2)");

            model.Entity<PriceChange>()
                .Property(x => x.OldPrice).HasColumnType("decimal(18,2)");
            model.Entity<PriceChange>()
                .Property(x => x.NewPrice).HasColumnType("decimal(18,2)");
            model.Entity<PriceChange>()
                .Property(x => x.ChangedAt).HasColumnType("datetime2(0)");

            // Views (keyless)
            model.Entity<StockAvailableView>(e =>
            {
                e.HasNoKey();
                e.ToView("v_StockAvailable");
            });

            model.Entity<StockByGoodView>(e =>
            {
                e.HasNoKey();
                e.ToView("v_StockByGood");
            });

            model.Entity<Stock>()
                .HasOne(s => s.Good)
                .WithMany(g => g.Stocks)
                .HasForeignKey(s => s.GoodID)
                .OnDelete(DeleteBehavior.Restrict);

            model.Entity<Stock>()
                .HasOne(s => s.Batch)
                .WithMany()
                .HasForeignKey(s => s.BatchID)
                .OnDelete(DeleteBehavior.Restrict);

        }
    }
}
