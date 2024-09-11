readonly record struct TodoO (
  int? id,
  string? task,
  long? due,
  int[]? categories,
  bool? done
);

static class Todo {

  public static async Task<IResult> Create(
    Auth auth, SqlConnection conn, TodoO todo
  ) {
    if(String.IsNullOrEmpty(todo.task?.Trim())) {
      return Results.BadRequest(new {error = "need a task"});
    }

    await conn.OpenAsync();
    await using var tran = await conn.BeginTransactionAsync();
    try {
      using var cmd = conn.CreateCommand();
      cmd.Transaction = tran as SqlTransaction;
      cmd.CommandText  =
        """
        insert into todo
          (task, done, due_unix_timestamp, userid)
            values
          (@task, @done, @due, @userid)
        """;

      cmd.Parameters.AddWithValue("task", todo.task);
      cmd.Parameters.AddWithValue("done", todo.done ?? false);
      cmd.Parameters.AddWithValue("due",
        todo.due ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds());
      cmd.Parameters.AddWithValue("userid", await auth.GetCurrentUser());

      await cmd.ExecuteNonQueryAsync();

      if(todo.categories is not (null or {Length: 0})) {
        using var last_insert  = conn.CreateCommand();
        last_insert.Transaction = tran as SqlTransaction;
        last_insert.CommandText
          = "select cast(ident_current('todo') as integer)";

        if(await last_insert.ExecuteScalarAsync() is int todoid) {
          using var category_todo  = conn.CreateCommand();
          category_todo.Transaction = tran as SqlTransaction;

          category_todo.CommandText =
            Enumerable.Range(0, todo.categories.Length)
              .Aggregate(
                "insert into category_todo"
                  + " (categoryid, todoid) values ",
                (a, v) => a + $"(@categoryid_{v}, @todoid),")
              .TrimEnd(',');

          category_todo.Parameters.AddRange(
            todo.categories.Select((e, i) => new SqlParameter {
              ParameterName = $"categoryid_{i}",
              SqlDbType = SqlDbType.Int,
              Value = e
            }).ToArray());

          category_todo.Parameters.Add(
            new SqlParameter {
              ParameterName = "todoid",
              SqlDbType = SqlDbType.Int,
              Value = todoid
            });

          await category_todo.ExecuteNonQueryAsync();
        }
      }

      await tran.CommitAsync();
    } catch(Exception ex) {
      await tran.RollbackAsync();
      return Results.BadRequest(new {error = ex.Message});
    }

    return Results.Ok();
  }

  public static async Task<IResult> Update(
    Auth auth, SqlConnection conn, TodoO todo
  ) {
    if(todo.id is null) {
      return Results.BadRequest(new {error = "need an id"});
    }

    if(todo with {id = null} == new TodoO()) {
      return Results.BadRequest(new {error = "nothing to update"});
    }

    await conn.OpenAsync();
    await using var tran = await conn.BeginTransactionAsync();
    try {
      using var cmd = conn.CreateCommand();
      cmd.Transaction = tran as SqlTransaction;
      cmd.CommandText = "update todo set ";

      if(todo.task is not null) {
        cmd.CommandText += " task = @task ,";
        cmd.Parameters.AddWithValue("task", todo.task);
      }

      if(todo.due is not null) {
        cmd.CommandText += " due_unix_timestamp = @due ,";
        cmd.Parameters.AddWithValue("due", todo.due);
      }

      if(todo.done is not null) {
        cmd.CommandText += " done = @done ,";
        cmd.Parameters.AddWithValue("done", todo.done);
      }

      cmd.CommandText = cmd.CommandText.TrimEnd(',');
      cmd.CommandText += " where id = @id and userid = @userid";
      cmd.Parameters.AddWithValue("id", todo.id);
      cmd.Parameters.AddWithValue("userid", await auth.GetCurrentUser());

      if(await cmd.ExecuteNonQueryAsync() == 0) {
        return Results.BadRequest(new {error = "could not update"});
      }

      if(todo.categories is not null) {
        using var del = conn.CreateCommand();
        del.Transaction = tran as SqlTransaction;
        del.CommandText = "delete from category_todo where todoid = @todoid";
        del.Parameters.AddWithValue("todoid", todo.id);
        await del.ExecuteNonQueryAsync();

        if(todo.categories.Length != 0) {
          using var category_todo  = conn.CreateCommand();
          category_todo.Transaction = tran as SqlTransaction;

          category_todo.CommandText =
            Enumerable.Range(0, todo.categories.Length)
              .Aggregate(
                "insert into category_todo"
                  + " (categoryid, todoid) values ",
                (a, v) => a + $"(@categoryid_{v}, @todoid),")
              .TrimEnd(',');

          category_todo.Parameters.AddRange(
            todo.categories.Select((e, i) => new SqlParameter {
              ParameterName = $"categoryid_{i}",
              SqlDbType = SqlDbType.Int,
              Value = e
            }).ToArray());

          category_todo.Parameters.Add(
            new SqlParameter {
              ParameterName = "todoid",
              SqlDbType = SqlDbType.Int,
              Value = todo.id
            });

          await category_todo.ExecuteNonQueryAsync();
        }
      }

      await tran.CommitAsync();
    } catch(Exception ex) {
      await tran.RollbackAsync();
      return Results.BadRequest(new {error = ex.Message});
    }

    return Results.Ok();
  }

  public static async Task<IResult> List(
    Auth auth, SqlConnection conn, JsonElement? o
  ) {
    int? cursor_init = o?._int("cursor_init");
    int? cursor_prev = o?._int("cursor_prev");
    int? cursor_next = o?._int("cursor_next");
    int? cursor = cursor_next ?? cursor_prev;

    bool forward = cursor_prev == null;
    var (op, dir) = forward ? (">" , "asc") : ("<" , "desc");

    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText =
      """
      select top(10)
        t.id,
        t.task,
        t.done,
        t.due_unix_timestamp,
        '[' + string_agg(iif((c.id is null), null,
          formatmessage('{"id":%d,"name":"%s","color":"%s"}',
            c.id, string_escape(c.name, 'json'), string_escape(c.color, 'json')
          )), ',') + ']' as categories
      from todo t
        left join category_todo ct on t.id = ct.todoid
        left join category c on c.id = ct.categoryid
      where 1=1 
      """;

    if(o?._int("id") is int id) {
      cmd.CommandText += " and t.id=@id ";
      cmd.Parameters.AddWithValue("id", id);
    }

    if(o?._str("task") is string task) {
      cmd.CommandText += " and t.task like @task ";
      cmd.Parameters.AddWithValue("task", $"%{task}%");
    }

    if(o?._bool("done") is bool done) {
      cmd.CommandText += " and t.done = @done ";
      cmd.Parameters.AddWithValue("done", done);
    }

    if(o?._long("due_from") is long due_from) {
      cmd.CommandText += " and t.due_unix_timestamp >= @due_from ";
      cmd.Parameters.AddWithValue("due_from", due_from);
    }

    if(o?._long("due_to") is long due_to) {
      cmd.CommandText += " and t.due_unix_timestamp <= @due_to ";
      cmd.Parameters.AddWithValue("due_to", due_to);
    }

    if(o?._arr("categories")?
      .Select(e => (int?) (
        (e.ValueKind is JsonValueKind.Number
          && e.TryGetInt32(out var i)) ? i : null))
      .Where(e => e != null) is var arr
      && arr is not null 
      && String.Join(',', arr) is var categories
      && categories.Length != 0
    ) {
      cmd.CommandText +=
        $" and ct.categoryid in ({String.Join(',', categories)}) ";
    }

    if(cursor != null) {
      cmd.CommandText += $" and t.id {op} @cursor ";
      cmd.Parameters.AddWithValue("cursor", cursor);
    }

    cmd.CommandText +=
      $"""
      and t.userid = @userid
      group by t.id, t.task, t.done, t.due_unix_timestamp
      order by t.id {dir}
      """;
    cmd.Parameters.AddWithValue("userid", await auth.GetCurrentUser());
      
    var data = (await cmd.ExecuteReaderAsync()).ToDictArray(!forward);

    cursor_prev = cursor_next = null;
    if(data.Length != 0) {
      cursor_prev = (int?) data[0]["id"];

      if(cursor == null) {
        cursor_init = cursor_prev;
      }

      if(cursor_init == cursor_prev) {
        cursor_prev = null;
      }

      if(data.Length == 10) {
        cursor_next = (int?) data[^1]["id"];
      }
    }

    return Results.Ok(new {
      data,
      cursor_init,
      cursor_prev,
      cursor_next,
    });
  }

  public static async Task<IResult> Delete(
    Auth auth, SqlConnection conn, JsonElement o
  ) {
    int? id = o._int("id");
    if(id is null) {
      return Results.BadRequest(new {error = "need an id"});
    }

    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "delete from todo where id=@id and userid=@userid";
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("userid", await auth.GetCurrentUser());

    if(await cmd.ExecuteNonQueryAsync() == 0) {
      return Results.BadRequest(new {error = "could not delete"});
    }

    return Results.Ok();
  }

}
