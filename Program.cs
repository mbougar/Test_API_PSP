using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add MongoDB Configuration
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDB"));
builder.Services.AddSingleton<MongoDbService>();

// Add controllers
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// Configure Middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

// MongoDB Settings
public class MongoDbSettings
{
    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "";
}

// User Model
public class User
{
    public ObjectId Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int HighestScore { get; set; } = 0;
}

// MongoDB Service
public class MongoDbService
{
    private readonly IMongoCollection<User> _users;

    public MongoDbService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);
        _users = database.GetCollection<User>("Users");

        // Ensure Username is Unique
        var indexKeys = Builders<User>.IndexKeys.Ascending(u => u.Username);
        var indexOptions = new CreateIndexOptions { Unique = true };
        var indexModel = new CreateIndexModel<User>(indexKeys, indexOptions);
        _users.Indexes.CreateOne(indexModel);
    }

    public async Task<User?> GetUserAsync(string username) =>
        await _users.Find(u => u.Username == username).FirstOrDefaultAsync();

    public async Task<bool> CreateUserAsync(User user)
    {
        try
        {
            await _users.InsertOneAsync(user);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false; // Username already exists
        }
    }

    public async Task<bool> UpdateScoreAsync(string username, int newScore)
    {
        var filter = Builders<User>.Filter.Eq(u => u.Username, username);
        var existingUser = await _users.Find(filter).FirstOrDefaultAsync();

        if (existingUser == null || newScore <= existingUser.HighestScore)
            return false; // No update needed

        var update = Builders<User>.Update.Set(u => u.HighestScore, newScore);
        await _users.UpdateOneAsync(filter, update);
        return true;
    }

    public async Task<List<User>> GetLeaderboardAsync(int limit = 10) =>
        await _users.Find(_ => true).SortByDescending(u => u.HighestScore).Limit(limit).ToListAsync();
}

// User Controller
[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;

    public UserController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] User user)
    {
        var hashedPassword = HashPassword(user.PasswordHash);
        user.PasswordHash = hashedPassword;

        var success = await _mongoDbService.CreateUserAsync(user);
        if (!success) return BadRequest("Username already exists");

        return Ok("User registered successfully");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] User user)
    {
        var existingUser = await _mongoDbService.GetUserAsync(user.Username);
        if (existingUser == null || !VerifyPassword(user.PasswordHash, existingUser.PasswordHash))
            return Unauthorized("Invalid credentials");

        return Ok("Login successful");
    }

    [HttpPost("submit-score")]
    public async Task<IActionResult> SubmitScore([FromBody] User user)
    {
        var success = await _mongoDbService.UpdateScoreAsync(user.Username, user.HighestScore);

        if (!success)
            return Ok("Score not updated (either user not found or score is not higher)");

        return Ok("Score updated successfully");
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard()
    {
        var leaderboard = await _mongoDbService.GetLeaderboardAsync();
        return Ok(leaderboard);
    }

    // Secure Password Hashing (PBKDF2)
    private static string HashPassword(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        byte[] salt = new byte[16];
        rng.GetBytes(salt);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
        byte[] hash = pbkdf2.GetBytes(32);

        return Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 2) return false;

            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] storedHashBytes = Convert.FromBase64String(parts[1]);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            byte[] computedHash = pbkdf2.GetBytes(32);

            return CryptographicOperations.FixedTimeEquals(computedHash, storedHashBytes);
        }
        catch
        {
            return false;
        }
    }
}
