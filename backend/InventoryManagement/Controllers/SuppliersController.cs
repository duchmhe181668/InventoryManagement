using InventoryManagement.Data;
using InventoryManagement.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;
    public SuppliersController(AppDbContext db) { _db = db; }

    // GET /api/suppliers?q=&sortBy=name|lastpo|spend&sortDir=asc|desc&page=1&pageSize=20
    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierListItemDto>>> GetList(
        [FromQuery] string? q,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDir = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var baseQuery = _db.Suppliers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            baseQuery = baseQuery.Where(s =>
                EF.Functions.Like(s.Name, $"%{keyword}%") ||
                EF.Functions.Like(s.PhoneNumber!, $"%{keyword}%") ||
                EF.Functions.Like(s.Email!, $"%{keyword}%"));
        }

        // Dựng projection có subqueries gắn với SupplierID
        var projected = baseQuery.Select(s => new SupplierListItemDto
        {
            SupplierID = s.SupplierID,
            Name = s.Name,
            PhoneNumber = s.PhoneNumber,
            Email = s.Email,
            Address = s.Address,

            LastPODate = _db.PurchaseOrders
                .Where(p => p.SupplierID == s.SupplierID)
                .Select(p => (DateTime?)p.CreatedAt)
                .Max(),

            POCount = _db.PurchaseOrders
                .Count(p => p.SupplierID == s.SupplierID),

            ReceiptCount = _db.Receipts
                .Count(r => r.SupplierID == s.SupplierID),

            // Tổng chi tiêu: chỉ tính các Receipt Status = Confirmed
            TotalSpend = _db.Receipts
                .Where(r => r.SupplierID == s.SupplierID && r.Status == "Confirmed")
                .SelectMany(r => r.Details!)
                .Sum(d => (decimal?)(d.UnitCost * d.Quantity)) ?? 0
        });

        // Sắp xếp
        bool desc = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        projected = sortBy.ToLower() switch
        {
            "lastpo" => (desc ? projected.OrderByDescending(x => x.LastPODate)
                              : projected.OrderBy(x => x.LastPODate))
                         .ThenBy(x => x.Name),
            "spend" => (desc ? projected.OrderByDescending(x => x.TotalSpend)
                              : projected.OrderBy(x => x.TotalSpend))
                         .ThenBy(x => x.Name),
            _ => (desc ? projected.OrderByDescending(x => x.Name)
                              : projected.OrderBy(x => x.Name))
        };

        var total = await projected.CountAsync();
        var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return Ok(new PagedResult<SupplierListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items
        });
    }

    // GET /api/suppliers/{id}?poStatus=Draft&poStatus=Submitted&rcStatus=Confirmed&max=100
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SupplierDetailDto>> GetDetail(
        [FromRoute] int id,
        [FromQuery] List<string>? poStatus,
        [FromQuery] List<string>? rcStatus,
        [FromQuery] int max = 100)
    {
        max = max <= 0 ? 100 : Math.Min(max, 500);

        var s = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SupplierID == id);

        if (s == null) return NotFound($"Supplier {id} không tồn tại.");

        // Tóm tắt chung
        var lastPODate = await _db.PurchaseOrders
            .Where(p => p.SupplierID == id)
            .Select(p => (DateTime?)p.CreatedAt)
            .MaxAsync();

        var poCount = await _db.PurchaseOrders.CountAsync(p => p.SupplierID == id);

        var rcBase = _db.Receipts.Where(r => r.SupplierID == id);
        var receiptCount = await rcBase.CountAsync();
        var totalSpend = await rcBase.Where(r => r.Status == "Confirmed")
            .SelectMany(r => r.Details!)
            .SumAsync(d => (decimal?)(d.UnitCost * d.Quantity)) ?? 0;

        // Tab PO (lọc theo trạng thái nếu truyền)
        var posQuery = _db.PurchaseOrders.AsNoTracking()
            .Where(p => p.SupplierID == id);

        if (poStatus != null && poStatus.Count > 0)
        {
            posQuery = posQuery.Where(p => poStatus.Contains(p.Status));
        }

        var pos = await posQuery
            .OrderByDescending(p => p.CreatedAt)
            .Take(max)
            .Select(p => new POSummaryDto
            {
                POID = p.POID,
                CreatedAt = p.CreatedAt,
                Status = p.Status,
                LineCount = _db.PurchaseOrderLines.Count(l => l.POID == p.POID),
                TotalQty = _db.PurchaseOrderLines
                    .Where(l => l.POID == p.POID)
                    .Sum(l => (decimal?)l.Quantity) ?? 0
            })
            .ToListAsync();

        // Tab Receipts (lọc theo trạng thái nếu truyền)
        var rcsQuery = _db.Receipts.AsNoTracking()
            .Where(r => r.SupplierID == id);

        if (rcStatus != null && rcStatus.Count > 0)
        {
            rcsQuery = rcsQuery.Where(r => rcStatus.Contains(r.Status));
        }

        var rcs = await rcsQuery
            .OrderByDescending(r => r.CreatedAt)
            .Take(max)
            .Select(r => new ReceiptSummaryDto
            {
                ReceiptID = r.ReceiptID,
                CreatedAt = r.CreatedAt,
                Status = r.Status,
                DetailCount = _db.ReceiptDetails.Count(d => d.ReceiptID == r.ReceiptID),
                TotalQty = _db.ReceiptDetails
                    .Where(d => d.ReceiptID == r.ReceiptID)
                    .Sum(d => (decimal?)d.Quantity) ?? 0,
                TotalCost = _db.ReceiptDetails
                    .Where(d => d.ReceiptID == r.ReceiptID)
                    .Sum(d => (decimal?)(d.UnitCost * d.Quantity)) ?? 0
            })
            .ToListAsync();

        var result = new SupplierDetailDto
        {
            SupplierID = s.SupplierID,
            Name = s.Name,
            PhoneNumber = s.PhoneNumber,
            Email = s.Email,
            Address = s.Address,
            LastPODate = lastPODate,
            POCount = poCount,
            ReceiptCount = receiptCount,
            TotalSpend = totalSpend,
            PurchaseOrders = pos,
            Receipts = rcs
        };

        return Ok(result);
    }

    // GET /api/suppliers/{id}/pos?status=Draft&status=Submitted&page=1&pageSize=10
    [HttpGet("{id:int}/pos")]
    public async Task<ActionResult<PagedResult<POSummaryDto>>> GetPOs(
        int id, [FromQuery] List<string>? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);

        var q = _db.PurchaseOrders.AsNoTracking().Where(p => p.SupplierID == id);
        if (status is { Count: > 0 }) q = q.Where(p => status.Contains(p.Status));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new POSummaryDto
            {
                POID = p.POID,
                CreatedAt = p.CreatedAt,
                Status = p.Status,
                LineCount = _db.PurchaseOrderLines.Count(l => l.POID == p.POID),
                TotalQty = _db.PurchaseOrderLines.Where(l => l.POID == p.POID).Sum(l => (decimal?)l.Quantity) ?? 0
            })
            .ToListAsync();

        return Ok(new PagedResult<POSummaryDto> { Page = page, PageSize = pageSize, TotalItems = total, Items = items });
    }

    // GET /api/suppliers/{id}/receipts?status=Confirmed&page=1&pageSize=10
    [HttpGet("{id:int}/receipts")]
    public async Task<ActionResult<PagedResult<ReceiptSummaryDto>>> GetReceipts(
        int id, [FromQuery] List<string>? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);

        var q = _db.Receipts.AsNoTracking().Where(r => r.SupplierID == id);
        if (status is { Count: > 0 }) q = q.Where(r => status.Contains(r.Status));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ReceiptSummaryDto
            {
                ReceiptID = r.ReceiptID,
                CreatedAt = r.CreatedAt,
                Status = r.Status,
                DetailCount = _db.ReceiptDetails.Count(d => d.ReceiptID == r.ReceiptID),
                TotalQty = _db.ReceiptDetails.Where(d => d.ReceiptID == r.ReceiptID).Sum(d => (decimal?)d.Quantity) ?? 0,
                TotalCost = _db.ReceiptDetails.Where(d => d.ReceiptID == r.ReceiptID).Sum(d => (decimal?)(d.UnitCost * d.Quantity)) ?? 0
            })
            .ToListAsync();

        return Ok(new PagedResult<ReceiptSummaryDto> { Page = page, PageSize = pageSize, TotalItems = total, Items = items });
    }

}
