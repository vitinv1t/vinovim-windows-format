-- Vinovim Windows Format: schema próprio, isolado dos outros projetos Vinovim.
-- As tabelas não estão expostas na API; o site usa só as funções public.winformat_*.
-- (Já aplicado no projeto Supabase em 22/11/2026.)
create schema if not exists winformat;
revoke all on schema winformat from public, anon, authenticated;

create table winformat.definicoes (
  chave text primary key,
  valor jsonb not null default '{}'::jsonb,
  atualizado_em timestamptz not null default now(),
  atualizado_por text
);
alter table winformat.definicoes enable row level security;

create table winformat.downloads (
  arquitetura text primary key check (arquitetura in ('x64', 'arm64')),
  total bigint not null default 0
);
alter table winformat.downloads enable row level security;

insert into winformat.definicoes (chave, valor) values ('manutencao', '{"ativa": false}');
insert into winformat.downloads (arquitetura) values ('x64'), ('arm64');

create function public.winformat_manutencao_ativa()
returns boolean language sql stable security definer set search_path = '' as $$
  select coalesce((select (valor->>'ativa')::boolean from winformat.definicoes where chave = 'manutencao'), true);
$$;

create function public.winformat_contar(p_arquitetura text)
returns void language plpgsql security definer set search_path = '' as $$
begin
  update winformat.downloads set total = total + 1 where arquitetura = p_arquitetura;
end $$;

create function public.winformat_definir_manutencao(p_ativa boolean)
returns boolean language plpgsql security definer set search_path = '' as $$
begin
  if coalesce(auth.jwt() ->> 'email', '') <> 'vitin7xisde@gmail.com' then
    raise exception 'sem permissão' using errcode = '42501';
  end if;
  insert into winformat.definicoes (chave, valor, atualizado_em, atualizado_por)
  values ('manutencao', jsonb_build_object('ativa', p_ativa), now(), auth.jwt() ->> 'email')
  on conflict (chave) do update set valor = excluded.valor, atualizado_em = excluded.atualizado_em, atualizado_por = excluded.atualizado_por;
  return p_ativa;
end $$;

create function public.winformat_downloads()
returns table (arquitetura text, total bigint) language plpgsql stable security definer set search_path = '' as $$
begin
  if coalesce(auth.jwt() ->> 'email', '') <> 'vitin7xisde@gmail.com' then
    raise exception 'sem permissão' using errcode = '42501';
  end if;
  return query select d.arquitetura, d.total from winformat.downloads d order by d.arquitetura;
end $$;

revoke all on function public.winformat_manutencao_ativa() from public;
revoke all on function public.winformat_contar(text) from public;
revoke all on function public.winformat_definir_manutencao(boolean) from public;
revoke all on function public.winformat_downloads() from public;
grant execute on function public.winformat_manutencao_ativa() to anon, authenticated;
grant execute on function public.winformat_contar(text) to anon, authenticated;
grant execute on function public.winformat_definir_manutencao(boolean) to authenticated;
grant execute on function public.winformat_downloads() to authenticated;
