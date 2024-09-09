create table usuario (
  id int primary key identity(1,1),
  username varchar(10) not null unique,
  passwd char(69) not null
);

create table sesion (
  token char(36) not null primary key,
  userid int not null,
  constraint fk_sesion_usuario foreign key (userid)
    references usuario(id) on delete cascade on update cascade
);

create table category (
  id int primary key identity(1,1),
  name varchar(50) not null unique,
  color varchar(50) not null
);

create table todo (
  id int primary key identity(1,1),
  task nvarchar(max) not null,
  done bit default 0,
  due_unix_timestamp bigint not null,
  userid int not null,
  constraint fk_todo_usuario foreign key (userid)
    references usuario(id) on delete cascade on update cascade
);

create table category_todo (
  categoryid int not null,
  todoid int not null,
  constraint fk_category_todo primary key (categoryid, todoid),
  constraint fk_category_todo_category foreign key (categoryid)
    references category(id) on delete cascade on update cascade,
  constraint fk_category_todo_todo foreign key (todoid)
    references todo(id) on delete cascade on update cascade
);
