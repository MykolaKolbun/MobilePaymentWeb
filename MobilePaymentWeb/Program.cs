using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using MobilePaymentWeb.Services;
using MobilePaymentWeb.Services.IPay;
using MobilePaymentWeb.Services.SkidataMobilePay;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var facilities = builder.Configuration
    .GetSection("Facilities")
    .Get<Dictionary<string, FacilityOptions>>()
    ?? new Dictionary<string, FacilityOptions>();
builder.Services.AddSingleton(facilities);
builder.Services.AddSingleton<IPaymentProviderFactory, PaymentProviderFactory>();
builder.Services.AddSingleton<IFiscalProviderFactory, FiscalProviderFactory>();
builder.Services.AddMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(10);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Honor X-Forwarded-Proto/For from the cloudflared tunnel so Request.Scheme is https
// (correct iPay callback URLs, and no "https port" redirect warning). The container only
// receives traffic from the local tunnel, so all forwarders are trusted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Persist DataProtection keys outside the container (mount a volume at /keys) so the
// session-cookie keyring survives redeploys instead of regenerating each time.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("/keys"))
        .SetApplicationName("MobilePaymentWeb");
}

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseSession();
app.UseHttpsRedirection();
app.UseRouting();
app.UseDeveloperExceptionPage();
app.UseAuthorization();
app.UseStaticFiles();
app.MapStaticAssets();

app.MapControllers();

app.Run();
