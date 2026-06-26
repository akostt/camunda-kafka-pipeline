using OrderManager.Hubs;
using OrderManager.Middleware;
using OrderManager.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<OrderStateService>();
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<OrderGeneratorService>();
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

app.UseMiddleware<BasicAuthMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<OrderHub>("/hubs/orders");

app.Run("http://0.0.0.0:5000");
