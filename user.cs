static class User {

  public static async Task<IResult> Create(
    Auth auth, SqlConnection conn, JsonElement o
  ) {
    if(!(await auth.IsAdmin())) {
      return Results.BadRequest(new {error = "verboten"});
    }

    string? username = o._str("username");
    string? passwd = o._str("passwd");

    if((username, passwd) is (null, null)) {
      return Results.BadRequest(new {error = "need a name and password"});
    }

    await conn.OpenAsync();
    using var ex_user = conn.CreateCommand();
    ex_user.CommandText = "select id from usuario where username=@username";
    ex_user.Parameters.AddWithValue("username", username);

    if(await ex_user.ExecuteScalarAsync() is not null) {
      return Results.BadRequest(new {error = "username already exists"});
    }

    byte[] salt = RandomNumberGenerator.GetBytes(16);
    byte[] hash = deriveKey(password: passwd!, salt: salt);

    using var cmd = conn.CreateCommand();
    cmd.CommandText
      = "insert into usuario(username, passwd) values (@username, @passwd)";

    cmd.Parameters.AddWithValue("username", username);
    cmd.Parameters.AddWithValue("passwd",
      Convert.ToBase64String(salt) + ':' + Convert.ToBase64String(hash));
    if(await cmd.ExecuteNonQueryAsync() == 0) {
      return Results.BadRequest(new {error = "cannot create"});
    }

    return Results.Ok();
  }

  public static async Task<IResult> List(
    Auth auth, SqlConnection conn, JsonElement? o
  ) {
    if(!(await auth.IsAdmin())) {
      return Results.BadRequest(new {error = "verboten"});
    }

    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "select id, username from usuario where 1=1 ";

    if(o?._int("id") is int id) {
      cmd.CommandText += " and id=@id ";
      cmd.Parameters.AddWithValue("id", id);
    }

    if(o?._str("username") is string username) {
      cmd.CommandText += " and username = @username ";
      cmd.Parameters.AddWithValue("username", username);
    }

    return Results.Ok((await cmd.ExecuteReaderAsync()).ToDictArray());
  }

  public static async Task<IResult> Delete(
    Auth auth, SqlConnection conn, JsonElement o
  ) {
    if(!(await auth.IsAdmin())) {
      return Results.BadRequest(new {error = "verboten"});
    }

    int? id = o._int("id");
    if(id is null) {
      return Results.BadRequest(new {error = "need an id"});
    }

    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "delete from usuario where id = @id";
    cmd.Parameters.AddWithValue("id", id);
    if(await cmd.ExecuteNonQueryAsync() == 0) {
      return Results.BadRequest(new {error = "cannot delete"});
    }

    return Results.Ok();
  }

  public static async Task<IResult> ResetPass(
    Auth auth, SqlConnection conn, JsonElement o
  ) {
    string? passwd = o._str("passwd");
    if(passwd is null) {
      return Results.BadRequest(new {error = "need a password"});
    }

    byte[] salt = RandomNumberGenerator.GetBytes(16);
    byte[] hash = deriveKey(password: passwd!, salt: salt);

    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText
      = "update usuario set passwd = @passwd where id = @id";
    cmd.Parameters.AddWithValue("id", await auth.GetCurrentUser());
    cmd.Parameters.AddWithValue("passwd",
      Convert.ToBase64String(salt) + ':' + Convert.ToBase64String(hash));

    if(await cmd.ExecuteNonQueryAsync() == 0) {
      return Results.BadRequest(new {error = "cannot reset"});
    }

    return Results.Ok();
  }
}
