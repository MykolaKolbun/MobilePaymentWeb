using MobilePaymentWeb.Services.IPay;
using MobilePaymentWeb.Services.SkidataMobilePay;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<SkidataOptions>(builder.Configuration.GetSection("Skidata"));
builder.Services.AddSingleton(sp => new IPayClient(
    mchId: builder.Configuration["IPay:MchId"] ?? throw new InvalidOperationException("IPay:MchId is not configured"),
    signKey: builder.Configuration["IPay:SignKey"] ?? throw new InvalidOperationException("IPay:SignKey is not configured"),
    logger: sp.GetRequiredService<ILogger<IPayClient>>(),
    sandbox: true
));
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(10);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseSession();
app.UseHttpsRedirection();
app.UseRouting();
app.UseDeveloperExceptionPage();
app.UseAuthorization();
app.UseStaticFiles();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=ParkingSession}/{action=ParkingSession}")
    .WithStaticAssets();


app.Run();
