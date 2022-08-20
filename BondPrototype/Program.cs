using BondPrototype.Middleware;
using BondPrototype.Models;
using BondPrototype.Models.DataSeeding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Newtonsoft.Json;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews().AddNewtonsoftJson(options => { options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore; });
builder.Services.Configure<RazorViewEngineOptions>(o =>
{
    o.ViewLocationFormats.Clear();
    o.ViewLocationFormats.Add("/Angular/src/app/{1}/{0}/{0}.component.ts");
    o.ViewLocationFormats.Add("/Angular/src/app/Shared/{0}/{0}.component.ts");
});

builder.Services.AddSwaggerGen(options => { options.CustomSchemaIds(type => type.ToString()); });

builder.Services.AddDbContext<Entities>((serviceProvider, options) => options
    .UseInMemoryDatabase("BondntPrototypeDemo")
    .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning))
    .EnableSensitiveDataLogging());

var app = builder.Build();

using var serviceScope = ((IApplicationBuilder)app).ApplicationServices.CreateScope();
var db = serviceScope.ServiceProvider.GetRequiredService<Entities>();
db.Database.EnsureDeleted();
db.Database.EnsureCreated();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseCustomEngine();
app.UseRouting();
app.UseCors();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();