create policy p on products for select to app using (owner = current_user); alter table products enable row level security;
