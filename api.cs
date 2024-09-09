﻿class Auth: IMiddleware {
  private readonly HttpContext? ctx;
  private readonly SqlConnection conn;

  public Auth(IHttpContextAccessor acx, SqlConnection conn) =>
    (this.ctx, this.conn) = (acx.HttpContext, conn);

  public async Task<int?> GetCurrentUser() {
    if(this.ctx?.Request.Cookies.TryGetValue("_id", out var k) ?? false) {
      await this.conn.OpenAsync();
      using var cmd = this.conn.CreateCommand();
      cmd.CommandText = "select userid from sesion where token=@token";
      cmd.Parameters.AddWithValue("token", k);
      return (int?) await cmd.ExecuteScalarAsync();
    }
    return null;
  }

  public async Task<bool> IsAdmin() => (await this.GetCurrentUser() == 1);

  public async Task InvokeAsync(HttpContext ctx, RequestDelegate nxt) {
    cl($"[;38;5;27;1m[{ctx.Request.Path}][0m");

    if((await this.GetCurrentUser() is not null)
      || (ctx.Request.Path.ToString() == "/login")) {
      await nxt(ctx);
    } else {
      ctx.Response.StatusCode = 403;
      ctx.Response.ContentType = "application/json";
      await ctx.Response.WriteAsync("""{"error": "verboten"}""");
      await ctx.Response.CompleteAsync();
    }
  }
}

class XXX {
  static async Task Main() {
    cl(await Task.Run(() =>
      String.Join("", collatz([Random.Shared.Next(1, 79)])
        .Select(e => $"[;38;5;{e % 256};1m\u25a0")) + "[0m"));

    var builder = WebApplication.CreateEmptyBuilder(new(){
      WebRootPath = "static"
    });
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddJsonFile(
      "config.json", optional: false, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();

    builder.WebHost.UseKestrelCore().ConfigureKestrel(o => {
      o.AddServerHeader = false;
      o.Limits.MaxRequestBodySize = null;
      o.ListenLocalhost(
        builder.Configuration.GetValue<Int16>("port", 5000));
    });

    builder.Services
      .AddRoutingCore()
      .AddCors()
      .AddHttpContextAccessor()
      .AddTransient(_ => new SqlConnection(
        builder.Configuration.GetValue<string>("dbconn")))
      .AddTransient<Auth>()
      .AddProblemDetails();

    var app = builder.Build();

    app.UseCors(builder => {
      builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader();
    });
    app.UseMiddleware<Auth>();
    app.UseStaticFiles();
    app.UseRouting();
    app.MapShortCircuit(404, "robots.txt", "favicon.ico", ".well-known");
    app.UseExceptionHandler();
    app.UseDeveloperExceptionPage();

    app.MapPost("/echo", (JsonElement o) => o);

    app.MapPost("/env", () => env());

    app.MapPost("/now", async (Auth auth, SqlConnection conn) => {
      await conn.OpenAsync();

      using var cmd = conn.CreateCommand();
      cmd.CommandText =
      """
      select
        current_timezone() as tz,
        sysdatetimeoffset() as local,
        datediff(second, '19700101', sysutcdatetime()) as unix_timestamp,
        sysutcdatetime() as unix_timestamp_str
      """;

      var ut = DateTimeOffset.UtcNow;

      return new {
        user = await auth.GetCurrentUser(),
        server = new {
          tz = TimeZoneInfo.Local.DisplayName,
          local = ut.ToLocalTime().ToString(),
          unix_timestamp = ut.ToUnixTimeSeconds(),
          unix_timestamp_str = ut.ToString()
        },
        database = (await cmd.ExecuteReaderAsync())
          .ToDictArray().FirstOrDefault(),
        ng = new NonGen().Cast<int>(),
        cg = new ConGen()
      };
    });

    app.MapPost("/login", async (
      HttpContext ctx, SqlConnection conn, Auth auth, JsonElement o
    ) => {
      if (await auth.GetCurrentUser() is not null) {
        cl("logged in - skip");
        return Results.Ok();
      }

      string? username = o._str("username");
      string? passwd = o._str("passwd");

      if((username, passwd) is (null, null)) {
        return Results.BadRequest(new {error = "need a name and password"});
      }

      await conn.OpenAsync();

      using var user_cmd = conn.CreateCommand();
      user_cmd.CommandText
        = "select id, passwd from usuario where username=@u";
      user_cmd.Parameters.AddWithValue("u", username);

      var user = (await user_cmd.ExecuteReaderAsync())
        .ToDictArray().FirstOrDefault();

      if(user is null
        || (((string) user["passwd"]).Split(':') is var arr
          && !CryptographicOperations.FixedTimeEquals(
          deriveKey(
            password: passwd!,
            salt: Convert.FromBase64String(arr[0])
          ),
          Convert.FromBase64String(arr[1])))
        ) {
        return Results.BadRequest(new {error = "incorrect user/pass"});
      }

      var userid = (int) user["id"];

      using var sess_del = conn.CreateCommand();
      sess_del.CommandText = "delete from sesion where userid=@u";
      sess_del.Parameters.AddWithValue("u", userid);
      await sess_del.ExecuteNonQueryAsync();

      static IEnumerable<string> _guid() {
        while(true) {
          yield return Guid.NewGuid().ToString();
        }
      }

      foreach(var g in _guid()) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select token from sesion where token=@token";
        cmd.Parameters.AddWithValue("token", g);

        if(await cmd.ExecuteScalarAsync() is null) {
          using var sess_add = conn.CreateCommand();
          sess_add.CommandText
            = "insert into sesion(token, userid) values (@g, @u)";
          sess_add.Parameters.AddWithValue("g", g);
          sess_add.Parameters.AddWithValue("u", userid);
          await sess_add.ExecuteNonQueryAsync();

          ctx.Response.Headers.Append(
            "set-cookie", "_id=" + g
            + ";domain=0.0.0.0;path=/;httponly;samesite=lax;max-age=604800"
          );
          break;
        }
      };

      return Results.Ok();
    });

    app.MapPost("/logout", async (
      HttpContext ctx, SqlConnection conn, Auth auth
    ) => {
      await conn.OpenAsync();
      using var sess_del = conn.CreateCommand();
      sess_del.CommandText = "delete from sesion where userid=@u";
      sess_del.Parameters.AddWithValue("u", await auth.GetCurrentUser());
      sess_del.ExecuteNonQuery();

      ctx.Response.Headers.Append(
        "set-cookie", "_id="
        + ";domain=0.0.0.0;path=/;httponly;samesite=lax;max-age=0"
      );

      return Results.Ok();
    });


    cl($"[48;5;227;38;5;0;1m{app.Environment.EnvironmentName}[0m");
    await app.RunAsync();
  }
}
