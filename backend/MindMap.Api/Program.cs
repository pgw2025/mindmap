using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Converters;
using MindMap.Api.Common.Filters;
using MindMap.Api.Common.Helpers;
using MindMap.Api.Common.Options;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Domain.Entities.Enums;
using MindMap.Api.Infrastructure.Data;
using MindMap.Api.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog ----
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// ---- 选项 ----
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<RecycleBinOptions>(builder.Configuration.GetSection("RecycleBin"));

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
builder.Services.AddAuthorization(options =>
{
    // 基于 JWT role claim "admin" 的策略：所有管理后台接口必须通过此策略
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
});

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
builder.Services.AddScoped<IMindMapVersionService, MindMapVersionService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<IRecycleBinService, RecycleBinService>();

// 回收站过期清理（软删导图按保留天数物理清除）
builder.Services.AddHostedService<RecycleBinCleanupService>();
// 节点图片孤儿文件清理（未被任何节点引用的旧文件物理删除）
builder.Services.AddHostedService<NodeImageCleanupService>();

// ---- Controllers + 全局异常 ----
builder.Services.AddControllers(options => options.Filters.Add<GlobalExceptionFilter>())
    .ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true)
    .AddJsonOptions(options =>
    {
        // DateTime(Unspecified) → 输出 UTC ISO 8601 带 "Z" 后缀
        // 解决 MySQL 读出的 DateTime 缺少时区信息导致前端解析为本地时间的问题
        options.JsonSerializerOptions.Converters.Add(new JsonConverterDateTime());
        options.JsonSerializerOptions.Converters.Add(new JsonConverterNullableDateTime());
    });

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

// ---- 静态文件：提供 /uploads/* 下的上传文件访问（节点图片等）----
// 显式绑定 PhysicalFileProvider + RequestPath，不使用默认的 WebRootFileProvider：
// 后者在 builder.Build() 时探测 ContentRoot/wwwroot，目录不存在就永久退化为
// NullFileProvider（运行期不重新探测），表现为「上传 200 但 GET 一律 404」。
// 上传目录不存在时自动创建，避免 UploadRoot 指向未初始化的路径。
// 目录解析规则与 UploadsController.ResolveUploadRoot 保持一致（相对 ContentRoot）。
var uploadRootPath = Path.IsPathRooted(appOpt.UploadRoot)
    ? appOpt.UploadRoot
    : Path.Combine(app.Environment.ContentRootPath, appOpt.UploadRoot);
Directory.CreateDirectory(uploadRootPath);
app.UseStaticFiles(new StaticFileOptions
{
    // provider 根即 uploads 目录本身：URL /uploads/nodes/x.png → {uploads}/nodes/x.png
    FileProvider = new PhysicalFileProvider(uploadRootPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();

// ---- 数据库迁移 + 默认管理员种子 ----
// 启动时自动应用迁移并确保至少存在一个管理员账号。
// 默认管理员：用户名 admin / 密码 Admin@2026（首次登录后请立即修改密码）。
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    try
    {
        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var hasAdmin = await db.Users.AnyAsync(u => u.IsAdmin);
        if (!hasAdmin)
        {
            const string defaultAdminUsername = "admin";
            const string defaultAdminPassword = "Admin@2026";
            var (hash, salt) = PasswordHasher.Create(defaultAdminPassword);
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Username = defaultAdminUsername,
                Email = "admin@localhost",
                PasswordHash = hash,
                PasswordSalt = salt,
                IsAdmin = true,
                Status = UserStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            Log.Information("已创建默认管理员账号：{Username} / {Password}（请尽快修改）", defaultAdminUsername, defaultAdminPassword);
        }

        // 初始化内置模板（仅当模板表为空时写入）
        await TemplateSeedData.EnsureTemplatesAsync(db);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "数据库迁移/种子数据执行失败，应用仍将启动");
    }
}

app.MapControllers();

app.Run();
