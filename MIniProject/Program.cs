using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MIniProject.Data;
using MIniProject.Models;
using MIniProject.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var emailSection = builder.Configuration.GetSection(EmailOptions.SectionName);
builder.Services.Configure<EmailOptions>(emailSection);
builder.Services.PostConfigure<EmailOptions>(options =>
{
    // Accept Email:FromEmail as an alias when FromAddress was left at the shipped .local default.
    if (!string.IsNullOrWhiteSpace(options.FromEmail) &&
        (string.IsNullOrWhiteSpace(options.FromAddress) ||
         string.Equals(options.FromAddress, EmailOptions.DefaultLocalFromAddress, StringComparison.OrdinalIgnoreCase)))
    {
        options.FromAddress = options.FromEmail.Trim();
    }
});
var resetLinkHours = emailSection.GetValue("ResetLinkExpiryHours", 1);
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(Math.Max(1, resetLinkHours));
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddSingleton<IAppEmailSender, ConfigurableEmailSender>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<BookingNotificationService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ReportPdfService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

LogEmailOptions(app);

await DbSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

static void LogEmailOptions(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EmailStartup");
    var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value;
    var resolvedFrom = options.ResolveFromAddress();
    var host = string.IsNullOrWhiteSpace(options.Smtp.Host) ? "(empty)" : options.Smtp.Host;
    var userSet = !string.IsNullOrWhiteSpace(options.Smtp.UserName);

    logger.LogInformation(
        "Email options bound: Enabled={Enabled}, FromAddress={FromAddress}, ResolvedFrom={ResolvedFrom}, SmtpHost={Host}, SmtpPort={Port}, SmtpUserSet={UserSet}, AllowFileFallback={Fallback}",
        options.Enabled,
        options.FromAddress,
        resolvedFrom,
        host,
        options.Smtp.Port,
        userSet,
        options.AllowFileFallback);

    if (options.IsSmtpConfigured &&
        string.Equals(resolvedFrom, EmailOptions.DefaultLocalFromAddress, StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning(
            "Email:FromAddress is still the default .local placeholder while SMTP Host is configured. " +
            "Set user secret Email:FromAddress (or Email:FromEmail) to your SMTP mailbox — for Gmail, use the same value as Email:Smtp:UserName.");
    }
    else if (!options.IsSmtpConfigured)
    {
        logger.LogWarning(
            "Email:Smtp:Host is empty. Password reset and booking confirmation will not reach real inboxes " +
            "(Development may write App_Data/emails when AllowFileFallback is true).");
    }
}
