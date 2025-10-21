using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InventoryManagement.Data;
using InventoryManagement.Models.Auth;
using InventoryManagement.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// =======================
// Database (SQL Server)
// =======================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// =======================
// MVC Controllers + JSON (KHÔNG dùng Preserve)
// =======================
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Tránh vòng tham chiếu nhưng vẫn trả JSON phẳng
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // Nếu FE đang đọc PascalCase thì bỏ dòng trên.
    });

// =======================
// Swagger + Bearer
// =======================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "InventoryManagement API", Version = "v1" });

    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",            // chữ thường là chuẩn theo RFC
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Dán JWT vào đây (có thể không cần gõ 'Bearer ')."
    };

    c.AddSecurityDefinition("Bearer", jwtScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtScheme, Array.Empty<string>() }
    });
});

// =======================
// JWT Configuration
// =======================
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IJwtService, JwtService>();

var jwtCfg = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtCfg.Key))
{
    throw new InvalidOperationException(
        "JWT Key is not configured. Set it via User Secrets or add a 'Jwt' section in appsettings.json.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtCfg.Issuer,
            ValidAudience = jwtCfg.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtCfg.Key)),
            RoleClaimType = ClaimTypes.Role, // giữ cho chắc nếu tự phát token
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// =======================
// CORS
// =======================
builder.Services.AddCors(o => o.AddPolicy("AllowLoginPage", p =>
    p.AllowAnyHeader().AllowAnyMethod().WithOrigins(
        "http://localhost:5146",
        "http://localhost:5500",
        "http://127.0.0.1:5500",
        "http://localhost:8080",
        "http://localhost:4200",
        "http://localhost:5173"
    // Nếu front chạy HTTPS, thêm "https://127.0.0.1:5500" hoặc domain thực tế vào đây
    )
));

// =======================
// HttpClient cho Sales API
// =======================
var salesApiBase = builder.Configuration.GetValue<string>("SalesApiBase")?.TrimEnd('/');
if (string.IsNullOrWhiteSpace(salesApiBase))
{
    salesApiBase = "https://localhost:7225"; // fallback dev
}
builder.Services.AddHttpClient("SalesApi", c =>
{
    c.BaseAddress = new Uri(salesApiBase!);
});

// =======================
// Build & pipeline
// =======================
var app = builder.Build();

app.UseCors("AllowLoginPage");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
