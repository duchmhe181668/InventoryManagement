using InventoryManagement.Data;
using InventoryManagement.Models.Auth;
using InventoryManagement.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

using System.Text;

var builder = WebApplication.CreateBuilder(args);

// =======================
// Database (SQL Server)
// =======================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// =======================
// MVC Controllers & Swagger
// =======================
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// =======================
// JWT Configuration
// - Bạn có thể cấp giá trị qua:
//   (a) User Secrets (khuyên dùng dev):
//       dotnet user-secrets init
//       dotnet user-secrets set "Jwt:Issuer" "InventoryManagement"
//       dotnet user-secrets set "Jwt:Audience" "InventoryManagement"
//       dotnet user-secrets set "Jwt:Key" "CHUOI_BI_MAT_RAT_DAI_>=64_KY_TU................................"
//       dotnet user-secrets set "Jwt:ExpiresMinutes" "120"
//   (b) hoặc appsettings.json (thêm block "Jwt")
// =======================

// Bind section "Jwt" vào JwtOptions (được nạp từ User Secrets/appsettings)
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
// Service phát token
builder.Services.AddSingleton<IJwtService, JwtService>();

// Lấy cấu hình JWT để validate token
var jwtCfg = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// Bảo vệ: nếu thiếu Key, báo lỗi sớm để dễ debug
if (string.IsNullOrWhiteSpace(jwtCfg.Key))
{
    throw new InvalidOperationException(
        "JWT Key is not configured. Set it via User Secrets (recommended) or add a 'Jwt' section in appsettings.json.");
}

// Đăng ký Authentication: JWT Bearer
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
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
// services
builder.Services.AddCors(o => o.AddPolicy("AllowLoginPage", p =>
    p.AllowAnyHeader().AllowAnyMethod().WithOrigins(
      "http://localhost:5146",         // nếu đặt login.html trong wwwroot của chính API thì KHÔNG cần CORS
      "http://localhost:5500",         // ví dụ Live Server
      "http://127.0.0.1:5500",
      "http://localhost:8080",
      "http://localhost:4200",
      "http://localhost:5173"
    )
));

// middleware (đặt trước MapControllers)



var app = builder.Build();
app.UseCors("AllowLoginPage");

// =======================
// Middleware pipeline
// =======================
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
