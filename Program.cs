var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var player = new Player("Manuel", 100, 25, 5);

var stage = new Stage("Dark Forest", TimeSpan.FromMinutes(12), "Hard");

app.MapGet("/player", () => player)
    .WithName("GetPlayer");

app.MapGet("/stage", () => stage)
    .WithName("GetStage");

app.Run();

record Player(string Name, int HP, int Damage, int Level);

record Stage(string StageName, TimeSpan TimeOfRun, string Difficulty);
