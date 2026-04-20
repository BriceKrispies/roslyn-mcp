using Autofac;
using Autofac.Extensions.DependencyInjection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MyApp.Data;
using MyApp.Handlers;
using MyApp.Services;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Use Autofac as the service provider factory
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Add services to the container
builder.Services.AddControllersWithViews();

// Add MediatR - include handlers from both main app and DependencyApp
builder.Services.AddMediatR(cfg => 
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    cfg.RegisterServicesFromAssembly(typeof(DependencyApp.Handlers.ValidateUserDataQueryHandler).Assembly);
});

// Add Entity Framework with SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=myapp.db"));

// Add Memory Cache for caching services
builder.Services.AddMemoryCache();

// Configure Autofac
builder.Host.ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
{
    // Register message handlers with Autofac
    containerBuilder.RegisterType<HelloMessageHandler>().AsImplementedInterfaces();
    containerBuilder.RegisterType<MyApp.Handlers.GetUsersQueryHandler>().AsImplementedInterfaces();
    
    // Register User Service implementations - LSP should find all these implementations
    containerBuilder.RegisterType<DatabaseUserService>().As<IUserService>().Named<IUserService>("database");
    containerBuilder.RegisterType<MockUserService>().As<IUserService>().Named<IUserService>("mock");
    
    // Register CacheUserService with decorator pattern (wraps DatabaseUserService)
    containerBuilder.Register<IUserService>(c =>
    {
        var databaseService = c.ResolveNamed<IUserService>("database");
        var cache = c.Resolve<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var logger = c.Resolve<Microsoft.Extensions.Logging.ILogger<CacheUserService>>();
        return new CacheUserService(databaseService, cache, logger);
    }).Named<IUserService>("cached");
    
    // Set the primary user service (LSP should trace this to CacheUserService)
    containerBuilder.Register<IUserService>(c => c.ResolveNamed<IUserService>("cached"));
    
    // Register Notification Service implementations - multiple implementations for LSP testing
    containerBuilder.RegisterType<EmailNotificationService>().As<INotificationService>().Named<INotificationService>("email");
    containerBuilder.RegisterType<SmsNotificationService>().As<INotificationService>().Named<INotificationService>("sms");
    containerBuilder.RegisterType<PushNotificationService>().As<INotificationService>().Named<INotificationService>("push");
    
    // Register primary notification service (can be changed for testing different providers)
    containerBuilder.Register<INotificationService>(c => c.ResolveNamed<INotificationService>("email"));
    
    // Register Payment Processor implementations - LSP should find all these
    containerBuilder.RegisterType<StripePaymentProcessor>().As<IPaymentProcessor>().Named<IPaymentProcessor>("stripe");
    containerBuilder.RegisterType<PayPalPaymentProcessor>().As<IPaymentProcessor>().Named<IPaymentProcessor>("paypal");
    containerBuilder.RegisterType<MockPaymentProcessor>().As<IPaymentProcessor>().Named<IPaymentProcessor>("mock");
    
    // Register primary payment processor (Stripe for production, Mock for development)
    containerBuilder.Register<IPaymentProcessor>(c =>
    {
        var environment = c.Resolve<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        return environment.IsDevelopment() 
            ? c.ResolveNamed<IPaymentProcessor>("mock")
            : c.ResolveNamed<IPaymentProcessor>("stripe");
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
