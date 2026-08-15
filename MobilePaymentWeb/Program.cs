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

var app = builder.Build();

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
