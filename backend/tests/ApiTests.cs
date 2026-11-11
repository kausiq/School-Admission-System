using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Backend.Tests;

// Needs a running PostgreSQL (ConnectionStrings__Default). Run with: docker compose --profile test run --rm backend-tests
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _f;
    static ApiTests() => Environment.SetEnvironmentVariable("HOLD_DURATION_SECONDS", "3");
    public ApiTests(WebApplicationFactory<Program> f) => _f = f;

    static string Sid(int n) => (20260000000000000L + n).ToString();
    static string Uniq() => Guid.NewGuid().ToString("N")[..8];

    async Task<string> NewClass(HttpClient c, int seats)
    {
        var r = await c.PostAsJsonAsync("/classes", new { name = "T-" + Uniq(), totalSeats = seats });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }
    static HttpRequestMessage Req(HttpMethod m, string url, string sid)
    { var r = new HttpRequestMessage(m, url); r.Headers.Add("X-Student-Id", sid); return r; }

    [Fact]
    public async Task Concurrent_applications_never_exceed_total_seats()
    {
        var c = _f.CreateClient();
        var cid = await NewClass(c, 10);
        var results = await Task.WhenAll(Enumerable.Range(1, 50).Select(async i =>
            (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", Sid(i)))).StatusCode));
        Assert.Equal(10, results.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(40, results.Count(s => s == HttpStatusCode.Conflict));
        var cls = await c.GetFromJsonAsync<JsonElement>($"/classes/{cid}");
        Assert.Equal(10, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, cls.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task Same_student_cannot_hold_twice_and_bad_input_gives_4xx()
    {
        var c = _f.CreateClient();
        var cid = await NewClass(c, 5);
        Assert.Equal(HttpStatusCode.Created, (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", Sid(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", Sid(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", "123"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/classes", new { name = "", totalSeats = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/classes", new StringContent("{bad", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req(HttpMethod.Post, "/classes/nope/holds", Sid(1)))).StatusCode);
    }

    [Fact]
    public async Task Confirm_is_idempotent_owner_only_and_counted_once()
    {
        var c = _f.CreateClient();
        var cid = await NewClass(c, 5);
        var hold = await (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", Sid(1)))).Content.ReadFromJsonAsync<JsonElement>();
        var hid = hold.GetProperty("holdId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req(HttpMethod.Post, $"/holds/{hid}/confirm", Sid(2)))).StatusCode);
        var rs = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => c.SendAsync(Req(HttpMethod.Post, $"/holds/{hid}/confirm", Sid(1)))));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var cls = await c.GetFromJsonAsync<JsonElement>($"/classes/{cid}");
        Assert.Equal(1, cls.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(0, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Req(HttpMethod.Delete, $"/holds/{hid}", Sid(1)))).StatusCode);
    }

    [Fact]
    public async Task Expired_hold_releases_seat_and_cannot_be_confirmed()
    {
        var c = _f.CreateClient();
        var cid = await NewClass(c, 1);
        var hold = await (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", Sid(1)))).Content.ReadFromJsonAsync<JsonElement>();
        var hid = hold.GetProperty("holdId").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", Sid(2)))).StatusCode);
        await Task.Delay(3500);
        var h = await c.GetFromJsonAsync<JsonElement>($"/holds/{hid}");
        Assert.Equal("EXPIRED", h.GetProperty("status").GetString());
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>($"/classes/{cid}")).GetProperty("availableSeats").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Req(HttpMethod.Post, $"/holds/{hid}/confirm", Sid(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", Sid(2)))).StatusCode);
    }

    [Fact]
    public async Task Cancel_frees_seat_and_my_holds_lists_it()
    {
        var c = _f.CreateClient();
        var cid = await NewClass(c, 1);
        var sid = Sid(900 + Random.Shared.Next(99));
        var hold = await (await c.SendAsync(Req(HttpMethod.Post, $"/classes/{cid}/holds", sid))).Content.ReadFromJsonAsync<JsonElement>();
        var hid = hold.GetProperty("holdId").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(Req(HttpMethod.Delete, $"/holds/{hid}", sid))).StatusCode);
        var mine = await (await c.SendAsync(Req(HttpMethod.Get, "/me/holds", sid))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(mine.EnumerateArray(), x => x.GetProperty("holdId").GetString() == hid && x.GetProperty("status").GetString() == "CANCELLED");
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>($"/classes/{cid}")).GetProperty("availableSeats").GetInt32());
    }
}
