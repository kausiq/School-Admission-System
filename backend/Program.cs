using System.Text.RegularExpressions;
using Backend;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDb>((sp, o) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    o.UseNpgsql(cfg.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=admission;Username=postgres;Password=postgres");
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
var holdSeconds = int.TryParse(app.Configuration["HOLD_DURATION_SECONDS"], out var hs) && hs > 0 ? hs : 600;

app.UseExceptionHandler(a => a.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    // malformed JSON / bad request body is a client error, never a 500
    if (ex is BadHttpRequestException or System.Text.Json.JsonException)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "অনুরোধের তথ্য ভুল। সঠিক ফরম্যাটে তথ্য দিন।" });
        return;
    }
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "সার্ভারে সমস্যা হয়েছে। একটু পরে আবার চেষ্টা করুন।" });
}));
app.UseCors();

await InitDb(app);

static IResult Err(int code, string msg) => Results.Json(new { error = msg }, statusCode: code);
static string Eff(Hold h, DateTime now) => h.Status == "HELD" && h.ExpiresAt <= now ? "EXPIRED" : h.Status;
static string NewId(string p) => p + Guid.NewGuid().ToString("N")[..12];

static bool TryStudent(HttpRequest r, out string id, out IResult? err)
{
    id = r.Headers["X-Student-Id"].ToString().Trim();
    if (!Regex.IsMatch(id, @"^\d{17}$"))
    {
        err = Err(400, "শিক্ষার্থীর জন্ম নিবন্ধন নম্বর ১৭ অঙ্কের হতে হবে।");
        return false;
    }
    err = null;
    return true;
}

static async Task<object> ClassDto(AppDb db, SchoolClass c, DateTime now)
{
    var rows = await db.Holds.Where(h => h.ClassId == c.Id && (h.Status == "CONFIRMED" || (h.Status == "HELD" && h.ExpiresAt > now)))
        .Select(h => h.Status).ToListAsync();
    return Dto(c, rows.Count(s => s == "HELD"), rows.Count(s => s == "CONFIRMED"));
}
static object Dto(SchoolClass c, int held, int conf) => new
{
    id = c.Id, name = c.Name, totalSeats = c.TotalSeats,
    heldSeats = held, confirmedSeats = conf, availableSeats = c.TotalSeats - held - conf
};

// 1. create class
app.MapPost("/classes", async (CreateClassReq? req, AppDb db) =>
{
    var name = req?.Name?.Trim();
    if (string.IsNullOrEmpty(name) || name.Length > 100) return Err(400, "Class-এর নাম দিন (সর্বোচ্চ ১০০ অক্ষর)।");
    if (req?.TotalSeats is null or < 1 or > 100000) return Err(400, "Seat সংখ্যা ১ থেকে ১,০০,০০০-এর মধ্যে হতে হবে।");
    var c = new SchoolClass { Id = NewId("c_"), Name = name, TotalSeats = req.TotalSeats.Value, CreatedAt = DateTime.UtcNow };
    db.Classes.Add(c);
    await db.SaveChangesAsync();
    return Results.Json(new { id = c.Id, name = c.Name, totalSeats = c.TotalSeats }, statusCode: 201);
});

// 2. list classes
app.MapGet("/classes", async (AppDb db) =>
{
    var now = DateTime.UtcNow;
    var classes = await db.Classes.OrderBy(c => c.CreatedAt).ToListAsync();
    var counts = await db.Holds.Where(h => h.Status == "CONFIRMED" || (h.Status == "HELD" && h.ExpiresAt > now))
        .GroupBy(h => h.ClassId)
        .Select(g => new { ClassId = g.Key, Held = g.Count(h => h.Status == "HELD"), Conf = g.Count(h => h.Status == "CONFIRMED") })
        .ToDictionaryAsync(x => x.ClassId);
    return Results.Ok(classes.Select(c =>
    {
        counts.TryGetValue(c.Id, out var k);
        return Dto(c, k?.Held ?? 0, k?.Conf ?? 0);
    }));
});

// 3. single class
app.MapGet("/classes/{classId}", async (string classId, AppDb db) =>
{
    var c = await db.Classes.FindAsync(classId);
    return c is null ? Err(404, "Class পাওয়া যায়নি।") : Results.Ok(await ClassDto(db, c, DateTime.UtcNow));
});

// 4. apply (seat hold) - class row is locked so concurrent requests are serialized (works across processes)
app.MapPost("/classes/{classId}/holds", async (string classId, HttpRequest req, AppDb db) =>
{
    if (!TryStudent(req, out var sid, out var e)) return e!;
    try
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var cls = (await db.Classes.FromSqlInterpolated($"SELECT * FROM \"Classes\" WHERE \"Id\" = {classId} FOR UPDATE").ToListAsync()).FirstOrDefault();
        if (cls is null) return Err(404, "Class পাওয়া যায়নি।");
        var now = DateTime.UtcNow;
        await db.Holds.Where(h => h.ClassId == classId && h.Status == "HELD" && h.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, "EXPIRED"));
        var active = await db.Holds.Where(h => h.ClassId == classId && (h.Status == "HELD" || h.Status == "CONFIRMED"))
            .Select(h => h.StudentId).ToListAsync();
        if (active.Contains(sid)) return Err(409, "এই শিক্ষার্থীর এই class-এ ইতিমধ্যে একটি চলমান আবেদন বা নিশ্চিত ভর্তি আছে।");
        if (active.Count >= cls.TotalSeats) return Err(409, "দুঃখিত, এই class-এ আর কোনো seat খালি নেই।");
        var h = new Hold { Id = NewId("h_"), ClassId = classId, StudentId = sid, Status = "HELD", CreatedAt = now, ExpiresAt = now.AddSeconds(holdSeconds) };
        db.Holds.Add(h);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Json(new { holdId = h.Id, classId = h.ClassId, studentId = h.StudentId, status = h.Status, expiresAt = h.ExpiresAt }, statusCode: 201);
    }
    catch (DbUpdateException) { return Err(409, "এই শিক্ষার্থীর এই class-এ ইতিমধ্যে আবেদন আছে।"); }
});

// 5. hold status
app.MapGet("/holds/{holdId}", async (string holdId, AppDb db) =>
{
    var h = await db.Holds.FindAsync(holdId);
    return h is null ? Err(404, "আবেদন পাওয়া যায়নি।")
        : Results.Ok(new { holdId = h.Id, classId = h.ClassId, status = Eff(h, DateTime.UtcNow), expiresAt = h.ExpiresAt });
});

// 6. confirm (pay admission fee) - hold row locked => idempotent even under concurrent double-clicks
app.MapPost("/holds/{holdId}/confirm", async (string holdId, HttpRequest req, AppDb db) =>
{
    if (!TryStudent(req, out var sid, out var e)) return e!;
    await using var tx = await db.Database.BeginTransactionAsync();
    var h = (await db.Holds.FromSqlInterpolated($"SELECT * FROM \"Holds\" WHERE \"Id\" = {holdId} FOR UPDATE").ToListAsync()).FirstOrDefault();
    if (h is null) return Err(404, "আবেদন পাওয়া যায়নি।");
    if (h.StudentId != sid) return Err(403, "এটি আপনার আবেদন নয়।");
    var st = Eff(h, DateTime.UtcNow);
    if (st == "CONFIRMED") return Results.Ok(new { holdId = h.Id, status = "CONFIRMED" });
    if (st == "EXPIRED") return Err(409, "আবেদনের মেয়াদ শেষ হয়ে গেছে। আবার আবেদন করুন।");
    if (st == "CANCELLED") return Err(409, "এই আবেদন বাতিল করা হয়েছে।");
    h.Status = "CONFIRMED";
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return Results.Ok(new { holdId = h.Id, status = "CONFIRMED" });
});

// 7. cancel
app.MapDelete("/holds/{holdId}", async (string holdId, HttpRequest req, AppDb db) =>
{
    if (!TryStudent(req, out var sid, out var e)) return e!;
    await using var tx = await db.Database.BeginTransactionAsync();
    var h = (await db.Holds.FromSqlInterpolated($"SELECT * FROM \"Holds\" WHERE \"Id\" = {holdId} FOR UPDATE").ToListAsync()).FirstOrDefault();
    if (h is null) return Err(404, "আবেদন পাওয়া যায়নি।");
    if (h.StudentId != sid) return Err(403, "এটি আপনার আবেদন নয়।");
    var st = Eff(h, DateTime.UtcNow);
    if (st == "CANCELLED") return Results.NoContent();
    if (st == "CONFIRMED") return Err(409, "ভর্তি নিশ্চিত হয়ে গেছে, বাতিল করা যাবে না।");
    if (st == "EXPIRED") return Err(409, "আবেদনের মেয়াদ আগেই শেষ হয়ে গেছে।");
    h.Status = "CANCELLED";
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return Results.NoContent();
});

// 8. my holds
app.MapGet("/me/holds", async (HttpRequest req, AppDb db) =>
{
    if (!TryStudent(req, out var sid, out var e)) return e!;
    var now = DateTime.UtcNow;
    var rows = await db.Holds.Where(h => h.StudentId == sid)
        .Join(db.Classes, h => h.ClassId, c => c.Id, (h, c) => new { h, c.Name })
        .OrderByDescending(x => x.h.CreatedAt).ToListAsync();
    return Results.Ok(rows.Select(x => new
    {
        holdId = x.h.Id, classId = x.h.ClassId, className = x.Name,
        status = Eff(x.h, now), expiresAt = x.h.ExpiresAt
    }));
});

app.Run();

static async Task InitDb(WebApplication app)
{
    for (var i = 0; ; i++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Database.OpenConnectionAsync(); // keep one connection so the advisory lock is held
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(727001)");
            try { await db.Database.EnsureCreatedAsync(); }
            finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(727001)"); }
            return;
        }
        catch (Exception ex) when (i < 30)
        {
            app.Logger.LogWarning("DB not ready ({Msg}); retrying...", ex.Message);
            await Task.Delay(2000);
        }
    }
}

public partial class Program { }
