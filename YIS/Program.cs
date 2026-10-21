using Microsoft.EntityFrameworkCore;
using YIS.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages()
    .AddMvcOptions(options =>
    {
        options.Filters.Add<YIS.Filters.RbacFilter>();
    });

//productions 


 builder.Services.AddDbContext<ApplicationDbContext>(options =>
     options.UseSqlServer("Server=SQL5113.site4now.net; Database=db_ac35e5_admin;User Id=db_ac35e5_admin_admin;Password=zindagi123;TrustServerCertificate=True;",
        sqlServerOptions => sqlServerOptions.UseCompatibilityLevel(120)));

//development 
//  builder.Services.AddDbContext<ApplicationDbContext>(options =>
//      options.UseSqlServer("Server=144.91.78.205;Database=visacmec_YarnSYS;User Id=yarnsysmaster;Password=master@1yarnsys;TrustServerCertificate=True;",
//         sqlServerOptions => sqlServerOptions.UseCompatibilityLevel(120)));

builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<YIS.Services.FbrValidationService>();

// Add Authentication
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

// Add Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add Antiforgery for HTMX
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

// Default route - redirect to Login
app.MapGet("/", () => Results.Redirect("/Login"));

app.MapRazorPages();

app.Run();
