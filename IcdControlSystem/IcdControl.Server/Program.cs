var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(opts =>
{
    // Prevent serialization errors when model graph contains cycles
    opts.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    // Keep property names as-is (optional)
    // opts.JsonSerializerOptions.PropertyNamingPolicy = null;
}).ConfigureApiBehaviorOptions(opts =>
{
    // Let controllers handle ModelState failures so we can return useful errors for bad JSON payloads.
    opts.SuppressModelStateInvalidFilter = true;
});
// Register DatabaseService for dependency injection
builder.Services.AddSingleton<IcdControl.Server.Data.DatabaseService>();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Allow request body to be read multiple times (used for better error reporting on bad JSON)
app.Use(async (ctx, next) =>
{
    ctx.Request.EnableBuffering();
    await next();
});

app.UseAuthorization();

app.MapControllers();

app.Run();
