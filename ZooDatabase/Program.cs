
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// 1. Turn on login using cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// 2. Define who can access Staff & Operations
builder.Services.AddAuthorization(options =>
{
    // Both roles can view Staff & Operations
    options.AddPolicy("StaffAccess", policy =>
        policy.RequireRole("Admin", "SuperUser"));

    // Only Admin can modify operational records
    options.AddPolicy("AdminManage", policy =>
        policy.RequireRole("Admin"));

    // SuperUser can access approved supervisory functions
    options.AddPolicy("SuperUserAccess", policy =>
        policy.RequireRole("SuperUser"));

    // Only DBA can manage system accounts
    options.AddPolicy("DBAAccess", policy =>
        policy.RequireRole("DBA"));

    // Existing veterinarian policy
    options.AddPolicy("VeterinarianAccess", policy =>
        policy.RequireRole("Veterinarian", "DBA", "SuperUser", "Admin"));
});

// 3. Every page requires login, except these
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");

    // Protect all Staff pages
    options.Conventions.AuthorizeFolder("/Staff", "StaffAccess");

    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await ZooDatabase.Data.UserSeeder.SeedAsync(app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// 4. Check who you are, then what you're allowed to do
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
