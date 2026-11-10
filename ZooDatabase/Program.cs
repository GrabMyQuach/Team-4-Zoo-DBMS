using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
// Always format dates and money as US English, no matter what server we run on
var usCulture = new System.Globalization.CultureInfo("en-US");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = usCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = usCulture;

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

builder.Services.AddAuthorization();

// Shopping cart for the public website (kept on the server for 2 hours per visitor)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 2. Every page requires login, except these
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Error");
    // The public website (homepage, animals, tickets, membership) is open to everyone
    options.Conventions.AllowAnonymousToFolder("/Visit");
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

// Show a friendly page (not a blank screen) for "not found", "bad request", etc.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.UseHttpsRedirection();
app.UseRouting();

// 3. Check who you are, then what you're allowed to do
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

// Tour guides can only open their own workspace (and the public site), see Data/GuideOptions.cs
app.Use(async (context, next) =>
{
    if (ZooDatabase.Data.GuideOptions.IsGuideOnly(context.User)
        && !ZooDatabase.Data.GuideOptions.GuideCanOpen(context.Request.Path))
    {
        context.Response.Redirect("/Guide");
        return;
    }
    await next();
});

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();