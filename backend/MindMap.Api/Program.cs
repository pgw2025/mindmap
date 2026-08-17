using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Filters;
using MindMap.Api.Common.Options;
using MindMap.Api.Infrastructure.Data;
using MindMap.Api.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog ----
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// ---- 选项 ----
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// ---- DataProtection：持久化到项目本地目录，避免沙箱权限问题 ----
var keysDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysDir));

// ---- DbContext (MySQL via Pomelo) ----
// 使用固定版本号避免启动时探测数据库（本地未启动 MySQL 也能拉起 API 探活）
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("未配置 ConnectionStrings:DefaultConnection");
var mySqlVersion = new MySqlServerVersion(new Version(8, 0, 35));
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseMySql(
        connectionString,
        mySqlVersion,
        mysql => mysql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

// ---- JWT 认证 ----
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("未配置 Jwt 节点");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// ---- CORS ----
var appOpt = builder.Configuration.GetSection("App").Get<AppOptions>()
    ?? new AppOptions();
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p
        .WithOrigins(appOpt.CorsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// ---- 应用服务 ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFolderService, FolderService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IMindMapService, MindMapService>();
builder.Services.AddScoped<INodeService, NodeService>();

// ---- Controllers + 全局异常 ----
builder.Services.AddControllers(options => options.Filters.Add<GlobalExceptionFilter>())
    .ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true);

// ---- Swagger/OpenAPI ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MindMap API", Version = "v1", Description = "思维导图系统后端 API" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Bearer 鉴权。示例：Bearer <token>",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ---- Pipeline ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
