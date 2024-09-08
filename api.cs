﻿class XXX {
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
      .AddProblemDetails()
      .AddCors();

    var app = builder.Build();

    app.UseCors(builder => {
      builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader();
    });
    app.UseStaticFiles();
    app.UseRouting();
    app.MapShortCircuit(404, "robots.txt", "favicon.ico", ".well-known");
    app.UseExceptionHandler();
    app.UseDeveloperExceptionPage();

    app.MapPost("/echo", (JsonElement o) => o);

    app.MapPost("/env", () => env());

    cl($"[48;5;227;38;5;0;1m{app.Environment.EnvironmentName}[0m");
    await app.RunAsync();
  }
}
