using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using MobilePaymentWeb.Services;
using MobilePaymentWeb.Services.IPay;
using MobilePaymentWeb.Services.Notifications;
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
builder.Services.AddHostedService<MobilePaymentWeb.Services.Fiscal.ShiftMaintenanceService>();
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

// Telegram admin alerting (global chat). BotToken/ChatId are secrets (env var / user-secrets).
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramNotifier>();
builder.Services.AddSingleton<ITelegramNotifier>(sp => sp.GetRequiredService<TelegramNotifier>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramNotifier>());

var app = builder.Build();

app.UseForwardedHeaders();

// Forward runtime Error/Critical (and our own Warnings) to Telegram, only when enabled.
var telegramOptions = app.Services.GetRequiredService<IOptions<TelegramOptions>>().Value;
if (telegramOptions.Enabled)
{
    var telegramNotifier = app.Services.GetRequiredService<ITelegramNotifier>();
    app.Services.GetRequiredService<ILoggerFactory>()
        .AddProvider(new TelegramLoggerProvider(telegramNotifier, telegramOptions.AppName));
    // One-time startup ping so a restart confirms the alerting pipeline end-to-end.
    telegramNotifier.Notify($"✅ {telegramOptions.AppName}: alerting online.");
}

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
