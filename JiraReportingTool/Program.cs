using JiraReportingTool.Components;
using JiraReportingTool.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddHttpClient<JiraService>();
builder.Services.AddScoped<JiraDbRepository>();
builder.Services.AddScoped<IJiraService, JiraCacheService>();
builder.Services.AddScoped<DataSyncService>();
builder.Services.AddScoped<SprintConfigService>();
builder.Services.AddScoped<ISprintPlanService, SprintPlanService>();
builder.Services.AddHttpClient<GitHubService>();

var app = builder.Build();

// Apply any pending EF Core migrations automatically on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
