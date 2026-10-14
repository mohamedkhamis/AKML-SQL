namespace AkmlSql.PostgreSql.Tests.Rules;

/// <summary>One rule's test material: a script it must flag (with the flagged spans marked) and scripts it must leave alone.</summary>
/// <param name="Positive">A script with <c>[[ ]]</c> around each span the rule must underline.</param>
/// <param name="Negatives">Scripts with the same shapes written the accepted way; the rule must report nothing on any of them.</param>
internal sealed class PgRuleCase
{
    public PgRuleCase(string ruleId, string positive, params string[] negatives)
    {
        RuleId = ruleId;
        Positive = positive;
        Negatives = negatives;
    }

    public string RuleId { get; }
    public string Positive { get; }
    public string[] Negatives { get; }

    /// <summary>The rules that need a schema cache to run.</summary>
    public bool NeedsSchema => RuleId is "PGPE004" or "PGPE005" or "PGBP005" or "PGDE006";
}

/// <summary>
/// Spec 042 (task T159, SC-016) - a positive and a negative script for each of the 52 rules of contracts/rules-catalog.md. The
/// positive script names the exact text the finding must underline, which pins where each rule anchors its message.
/// </summary>
internal static class PgRuleCases
{
    private static string Names(int count, string prefix = "n") => string.Concat(Enumerable.Range(0, count).Select(_ => prefix));

    public static readonly IReadOnlyList<PgRuleCase> All = new List<PgRuleCase>
    {
        // ── Performance ──────────────────────────────────────────────────────────────────────────────
        new("PGPE001", "SELECT [[*]] FROM customers;",
            "SELECT customer_id FROM customers;",
            "SELECT count(*) FROM customers;",
            "SELECT customer_id FROM customers c WHERE EXISTS (SELECT * FROM orders o WHERE o.customer_id = c.customer_id);"),
        new("PGPE002", "SELECT customer_id FROM customers WHERE company_name LIKE [['%corp']];",
            "SELECT customer_id FROM customers WHERE company_name LIKE 'corp%';",
            "SELECT customer_id FROM customers WHERE company_name = '%corp';"),
        new("PGPE003", "SELECT customer_id FROM customers WHERE [[lower]](company_name) = 'acme';",
            "SELECT customer_id FROM customers WHERE company_name = lower('ACME');",
            "SELECT customer_id FROM customers WHERE company_name = 'acme';"),
        new("PGPE004", "SELECT customer_id FROM customers WHERE customer_id NOT IN (SELECT [[customer_id]] FROM orders);",
            "SELECT order_id FROM orders WHERE order_id NOT IN (SELECT order_id FROM order_details);",
            "SELECT customer_id FROM customers WHERE customer_id NOT IN (SELECT customer_id FROM orders WHERE customer_id IS NOT NULL);",
            "SELECT customer_id FROM customers c WHERE NOT EXISTS (SELECT 1 FROM orders o WHERE o.customer_id = c.customer_id);"),
        new("PGPE005", "SELECT order_id FROM orders WHERE [[order_id]] = 1.5;",
            "SELECT order_id FROM orders WHERE freight = 1.5;",
            "SELECT order_id FROM orders WHERE order_id = 15;",
            "SELECT order_id FROM orders WHERE order_id = '15';"),
        new("PGPE006", "SELECT * FROM (SELECT customer_id FROM customers ORDER BY [[customer_id]]) c;",
            "SELECT * FROM (SELECT customer_id FROM customers ORDER BY customer_id LIMIT 10) c;",
            "SELECT customer_id FROM customers ORDER BY customer_id;",
            "SELECT ARRAY(SELECT customer_id FROM customers ORDER BY customer_id);",
            "SELECT * FROM (SELECT DISTINCT ON (city) city, customer_id FROM customers ORDER BY city, customer_id) c;"),
        new("PGPE007", "SELECT 1 WHERE (SELECT count(*) FROM orders) [[>]] 0;",
            "SELECT 1 WHERE EXISTS (SELECT 1 FROM orders);",
            "SELECT 1 WHERE (SELECT count(*) FROM orders) > 5;",
            "SELECT customer_id FROM orders GROUP BY customer_id HAVING count(*) > 0;"),
        new("PGPE008", "SELECT c.customer_id, [[(]]SELECT max(o.order_date) FROM orders o WHERE o.customer_id = c.customer_id) FROM customers c;",
            "SELECT c.customer_id, (SELECT max(order_date) FROM orders) FROM customers c;",
            "SELECT c.customer_id, max(o.order_date) FROM customers c JOIN orders o ON o.customer_id = c.customer_id GROUP BY c.customer_id;"),
        new("PGPE009", "SELECT customer_id FROM orders WHERE [[order_id]] IN (" + string.Join(", ", Enumerable.Range(1, 101)) + ");",
            "SELECT customer_id FROM orders WHERE order_id IN (" + string.Join(", ", Enumerable.Range(1, 100)) + ");",
            "SELECT customer_id FROM orders WHERE order_id = ANY($1);"),
        new("PGPE010", "SELECT * FROM customers [[CROSS JOIN]] orders; SELECT * FROM customers, [[orders]];",
            "SELECT * FROM customers JOIN orders ON orders.customer_id = customers.customer_id;",
            "SELECT * FROM customers, orders WHERE customers.customer_id = orders.customer_id;",
            "SELECT * FROM customers c, unnest(ARRAY[1, 2]) AS n;",
            "SELECT * FROM customers c CROSS JOIN LATERAL (SELECT 1 AS one) l;",
            "SELECT * FROM customers c CROSS JOIN unnest(ARRAY[1, 2]) AS n;"),

        // ── Best practices ───────────────────────────────────────────────────────────────────────────
        new("PGBP001", "INSERT INTO [[customers]] VALUES ('a', 'b');",
            "INSERT INTO customers (customer_id, company_name) VALUES ('a', 'b');",
            "INSERT INTO customers DEFAULT VALUES;"),
        new("PGBP002", "UPDATE [[customers]] SET city = 'x'; DELETE FROM [[orders]];",
            "UPDATE customers SET city = 'x' WHERE customer_id = 'a'; DELETE FROM orders WHERE order_id = 1;",
            "UPDATE customers SET city = 'x' WHERE true;"),
        new("PGBP003", "SELECT * FROM customers, [[orders]] WHERE customers.customer_id = orders.customer_id;",
            "SELECT * FROM customers JOIN orders ON customers.customer_id = orders.customer_id;",
            "SELECT * FROM customers c, LATERAL (SELECT 1) AS l;"),
        new("PGBP004", "SELECT customers.city FROM [[customers]] JOIN [[orders]] ON customers.customer_id = orders.customer_id;",
            "SELECT c.city FROM customers c JOIN orders o ON c.customer_id = o.customer_id;",
            "SELECT city FROM customers;"),
        new("PGBP005", "SELECT [[customer_id]] FROM customers c JOIN orders o ON c.customer_id = o.customer_id;",
            "SELECT c.customer_id FROM customers c JOIN orders o ON c.customer_id = o.customer_id;",
            "SELECT customer_id FROM customers JOIN orders USING (customer_id);",
            "SELECT company_name FROM customers c JOIN orders o ON c.customer_id = o.customer_id;"),
        new("PGBP006", "SELECT * INTO [[backup_customers]] FROM customers;",
            "CREATE TABLE backup_customers AS SELECT * FROM customers;"),
        new("PGBP007", "[[BEGIN]]; UPDATE customers SET city = 'x' WHERE customer_id = 'a';",
            "BEGIN; UPDATE customers SET city = 'x' WHERE customer_id = 'a'; COMMIT;",
            "START TRANSACTION; SELECT 1; ROLLBACK;"),
        new("PGBP008", "[[SET search_path TO sales]];",
            "SET timezone TO 'UTC';",
            "RESET search_path;"),
        new("PGBP009", "SELECT * FROM customers [[NATURAL]] JOIN orders;",
            "SELECT * FROM customers JOIN orders USING (customer_id);"),
        new("PGBP010", "WITH [[unused]] AS (SELECT 1) SELECT 2;",
            "WITH used AS (SELECT 1 AS x) SELECT x FROM used;",
            "WITH moved AS (DELETE FROM orders WHERE order_id = 1 RETURNING order_id) SELECT 1;",
            "WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM r WHERE n < 3) SELECT n FROM r;"),

        // ── Security ─────────────────────────────────────────────────────────────────────────────────
        new("PGSE001", "CREATE FUNCTION run(t text) RETURNS void LANGUAGE plpgsql AS $$ BEGIN [[EXECUTE]] 'DELETE FROM ' || t; END $$;",
            "CREATE FUNCTION run(t text) RETURNS void LANGUAGE plpgsql AS $$ BEGIN EXECUTE format('DELETE FROM %I', t); END $$;",
            "CREATE FUNCTION run(t text) RETURNS void LANGUAGE plpgsql AS $$ BEGIN EXECUTE 'DELETE FROM a' || 'b'; END $$;",
            "DO $$ BEGIN EXECUTE 'DELETE FROM ' || quote_ident('x') USING 1; END $$ LANGUAGE sql;"),
        new("PGSE002", "[[GRANT ALL ON customers TO analyst]]; [[GRANT SELECT ON customers TO PUBLIC]];",
            "GRANT SELECT ON customers TO analyst;",
            "REVOKE ALL ON customers FROM PUBLIC;"),
        new("PGSE003", "CREATE ROLE reporter LOGIN [[PASSWORD]] 'hunter2';",
            "CREATE ROLE reporter LOGIN;",
            "CREATE ROLE reporter PASSWORD NULL;"),
        new("PGSE004", "CREATE FUNCTION f() RETURNS int LANGUAGE sql [[SECURITY]] DEFINER AS 'select 1';",
            "CREATE FUNCTION f() RETURNS int LANGUAGE sql SECURITY DEFINER SET search_path = public AS 'select 1';",
            "CREATE FUNCTION f() RETURNS int LANGUAGE sql SECURITY INVOKER AS 'select 1';"),
        new("PGSE005", "[[COPY customers TO '/tmp/c.csv']]; SELECT [[pg_read_file]]('/etc/hosts');",
            "COPY customers TO STDOUT;",
            "SELECT lower('X');"),

        // ── Style ────────────────────────────────────────────────────────────────────────────────────
        new("PGST001", "SELECT customer_id FROM customers; [[select]] customer_id from orders;",
            "SELECT customer_id FROM customers; SELECT customer_id FROM orders;",
            "select customer_id from customers; select customer_id from orders;"),
        new("PGST002", "SELECT 1; SELECT [[2]]",
            "SELECT 1; SELECT 2;",
            "SELECT 1;\n-- done\n"),
        new("PGST003", "SELECT 1 WHERE 1 [[!=]] 2;",
            "SELECT 1 WHERE 1 <> 2;"),
        new("PGST004", "SELECT customer_id [[cid]] FROM customers;",
            "SELECT customer_id AS cid FROM customers;",
            "SELECT customer_id FROM customers;"),
        new("PGST005", "SELECT '" + new string('x', 192) + "[[" + new string('y', 20) + "';]]",
            "SELECT '" + new string('x', 150) + "';"),

        // ── Design ───────────────────────────────────────────────────────────────────────────────────
        new("PGDE001", "CREATE TABLE t (id integer PRIMARY KEY, created [[timestamp]]);",
            "CREATE TABLE t (id integer PRIMARY KEY, created timestamptz);",
            "CREATE TABLE t (id integer PRIMARY KEY, created timestamp with time zone);"),
        new("PGDE002", "CREATE TABLE t (id integer PRIMARY KEY, code [[char]](10));",
            "CREATE TABLE t (id integer PRIMARY KEY, code varchar(10));"),
        new("PGDE003", "CREATE TABLE t (id integer PRIMARY KEY, price [[money]]);",
            "CREATE TABLE t (id integer PRIMARY KEY, price numeric(12, 2));"),
        new("PGDE004", "CREATE TABLE t (id [[serial]] PRIMARY KEY);",
            "CREATE TABLE t (id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY);"),
        new("PGDE005", "CREATE TABLE [[events]] (id integer, name text);",
            "CREATE TABLE events (id integer PRIMARY KEY, name text);",
            "CREATE TABLE events (id integer, name text, PRIMARY KEY (id));",
            "CREATE TABLE events_2024 PARTITION OF events FOR VALUES FROM (1) TO (2);",
            "CREATE TABLE events_copy (LIKE events INCLUDING ALL);",
            "CREATE TEMP TABLE scratch (id integer, name text);"),
        new("PGDE006", "ALTER TABLE orders ADD [[CONSTRAINT]] fk_customer FOREIGN KEY (customer_id) REFERENCES customers (customer_id);",
            "CREATE INDEX ON orders (customer_id); ALTER TABLE orders ADD CONSTRAINT fk_customer FOREIGN KEY (customer_id) REFERENCES customers (customer_id);",
            "CREATE TABLE child (id integer PRIMARY KEY, parent_id integer, FOREIGN KEY (parent_id) REFERENCES customers (customer_id)); CREATE INDEX ON child (parent_id);",
            "CREATE TABLE child (parent_id integer PRIMARY KEY REFERENCES customers (customer_id));"),
        new("PGDE007", "CREATE TABLE t (id integer PRIMARY KEY, doc [[json]]);",
            "CREATE TABLE t (id integer PRIMARY KEY, doc jsonb);"),
        new("PGDE008", "CREATE TABLE t (id integer PRIMARY KEY, at [[time]] with time zone);",
            "CREATE TABLE t (id integer PRIMARY KEY, at time);"),

        // ── Deprecated ───────────────────────────────────────────────────────────────────────────────
        new("PGDEP001", "[[SET default_with_oids = true]]; CREATE TABLE t (id integer) WITH ([[oids]] = true);",
            "SET timezone TO 'UTC'; CREATE TABLE t (id integer) WITH (fillfactor = 70);"),
        new("PGDEP002", "SELECT f(a [[:=]] 1);",
            "SELECT f(a => 1);",
            "SELECT '1'::int;"),
        new("PGDEP003", "CREATE TABLE t (id integer, at [[abstime]]);",
            "CREATE TABLE t (id integer, at timestamptz);"),
        new("PGDEP004", "CREATE TABLE [[child]] (extra integer) INHERITS (parent);",
            "CREATE TABLE child PARTITION OF parent FOR VALUES FROM (1) TO (2);",
            "CREATE TABLE child (extra integer);"),

        // ── Execution ────────────────────────────────────────────────────────────────────────────────
        new("PGEX001", "BEGIN; [[CREATE INDEX CONCURRENTLY ON customers (city)]]; COMMIT;",
            "CREATE INDEX CONCURRENTLY ON customers (city);",
            "BEGIN; CREATE INDEX ON customers (city); COMMIT;"),
        new("PGEX002", "[[DROP TABLE old_orders]];",
            "BEGIN; DROP TABLE old_orders; ROLLBACK;",
            "DROP INDEX CONCURRENTLY idx_old;"),
        new("PGEX003", "[[SET LOCAL statement_timeout = 1000]];",
            "BEGIN; SET LOCAL statement_timeout = 1000; COMMIT;",
            "SET statement_timeout = 1000;"),
        new("PGEX004", "SELECT 1;\n[[\\dt]]\n",
            "SELECT 1;"),
        new("PGEX005", "[[COPY customers FROM STDIN]];",
            "COPY customers TO '/tmp/x.csv';",
            "COPY customers FROM PROGRAM 'cat x';"),

        // ── Naming ───────────────────────────────────────────────────────────────────────────────────
        new("PGNM001", "CREATE TABLE t ([[" + Names(64) + "]] integer);",
            "CREATE TABLE t (" + Names(63) + " integer);"),
        new("PGNM002", "SELECT [[Customer_Id]] FROM customers;",
            "SELECT customer_id FROM customers;",
            "SELECT COUNT(*), NOW() FROM customers;",
            "CREATE TABLE t (id UUID, at DATE);",
            "CREATE TABLE \"Orders\" (\"Order ID\" integer PRIMARY KEY);"),
        new("PGNM003", "CREATE TABLE t ([[\"user\"]] text);",
            "CREATE TABLE t (\"User\" text);",
            "CREATE TABLE t (\"name\" text);"),
        new("PGNM004", "CREATE TABLE [[\"Orders\"]] (id integer PRIMARY KEY); SELECT * FROM Orders;",
            "CREATE TABLE \"orders\" (id integer PRIMARY KEY); SELECT * FROM orders;",
            "CREATE TABLE \"Orders\" (id integer PRIMARY KEY); SELECT * FROM \"Orders\";"),
        new("PGNM005", "CREATE TABLE t ([[\"first name\"]] text);",
            "CREATE TABLE t (first_name text);",
            "CREATE TABLE t (\"FirstName\" text);"),
    };

    public static IEnumerable<object[]> Ids => All.Select(c => new object[] { c.RuleId });

    public static PgRuleCase For(string ruleId) => All.Single(c => c.RuleId == ruleId);
}
