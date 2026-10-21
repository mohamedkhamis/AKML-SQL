# PostgreSQL reference

This file is generated. Do not edit it: change the generator in `tools/pg-reference/` and run it again, see its README.

It describes PostgreSQL 18 and says in which release each entry first appears, for releases 14, 15, 16, 17, 18. Every entry links to the page of the official manual it was read from.

## Coverage

| Kind | In the manual | In this reference |
| --- | ---: | ---: |
| Commands | 185 | 185 |
| Key words | 494 | 494 |
| Types | 68 | 68 |
| Functions | 679 | 679 |
| Operators | 88 | 88 |
| SQLSTATE codes | 262 | 262 |

## SQL commands

One entry per command name; the synopsis is the first one on the command's reference page.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `ABORT` | abort the current transaction | 14 | [sql-abort](https://www.postgresql.org/docs/18/sql-abort.html) |
| `ALTER AGGREGATE` | change the definition of an aggregate function | 14 | [sql-alteraggregate](https://www.postgresql.org/docs/18/sql-alteraggregate.html) |
| `ALTER COLLATION` | change the definition of a collation | 14 | [sql-altercollation](https://www.postgresql.org/docs/18/sql-altercollation.html) |
| `ALTER CONVERSION` | change the definition of a conversion | 14 | [sql-alterconversion](https://www.postgresql.org/docs/18/sql-alterconversion.html) |
| `ALTER DATABASE` | change a database | 14 | [sql-alterdatabase](https://www.postgresql.org/docs/18/sql-alterdatabase.html) |
| `ALTER DEFAULT PRIVILEGES` | define default access privileges | 14 | [sql-alterdefaultprivileges](https://www.postgresql.org/docs/18/sql-alterdefaultprivileges.html) |
| `ALTER DOMAIN` | change the definition of a domain | 14 | [sql-alterdomain](https://www.postgresql.org/docs/18/sql-alterdomain.html) |
| `ALTER EVENT TRIGGER` | change the definition of an event trigger | 14 | [sql-altereventtrigger](https://www.postgresql.org/docs/18/sql-altereventtrigger.html) |
| `ALTER EXTENSION` | change the definition of an extension | 14 | [sql-alterextension](https://www.postgresql.org/docs/18/sql-alterextension.html) |
| `ALTER FOREIGN DATA WRAPPER` | change the definition of a foreign-data wrapper | 14 | [sql-alterforeigndatawrapper](https://www.postgresql.org/docs/18/sql-alterforeigndatawrapper.html) |
| `ALTER FOREIGN TABLE` | change the definition of a foreign table | 14 | [sql-alterforeigntable](https://www.postgresql.org/docs/18/sql-alterforeigntable.html) |
| `ALTER FUNCTION` | change the definition of a function | 14 | [sql-alterfunction](https://www.postgresql.org/docs/18/sql-alterfunction.html) |
| `ALTER GROUP` | change role name or membership | 14 | [sql-altergroup](https://www.postgresql.org/docs/18/sql-altergroup.html) |
| `ALTER INDEX` | change the definition of an index | 14 | [sql-alterindex](https://www.postgresql.org/docs/18/sql-alterindex.html) |
| `ALTER LANGUAGE` | change the definition of a procedural language | 14 | [sql-alterlanguage](https://www.postgresql.org/docs/18/sql-alterlanguage.html) |
| `ALTER LARGE OBJECT` | change the definition of a large object | 14 | [sql-alterlargeobject](https://www.postgresql.org/docs/18/sql-alterlargeobject.html) |
| `ALTER MATERIALIZED VIEW` | change the definition of a materialized view | 14 | [sql-altermaterializedview](https://www.postgresql.org/docs/18/sql-altermaterializedview.html) |
| `ALTER OPERATOR` | change the definition of an operator | 14 | [sql-alteroperator](https://www.postgresql.org/docs/18/sql-alteroperator.html) |
| `ALTER OPERATOR CLASS` | change the definition of an operator class | 14 | [sql-alteropclass](https://www.postgresql.org/docs/18/sql-alteropclass.html) |
| `ALTER OPERATOR FAMILY` | change the definition of an operator family | 14 | [sql-alteropfamily](https://www.postgresql.org/docs/18/sql-alteropfamily.html) |
| `ALTER POLICY` | change the definition of a row-level security policy | 14 | [sql-alterpolicy](https://www.postgresql.org/docs/18/sql-alterpolicy.html) |
| `ALTER PROCEDURE` | change the definition of a procedure | 14 | [sql-alterprocedure](https://www.postgresql.org/docs/18/sql-alterprocedure.html) |
| `ALTER PUBLICATION` | change the definition of a publication | 14 | [sql-alterpublication](https://www.postgresql.org/docs/18/sql-alterpublication.html) |
| `ALTER ROLE` | change a database role | 14 | [sql-alterrole](https://www.postgresql.org/docs/18/sql-alterrole.html) |
| `ALTER ROUTINE` | change the definition of a routine | 14 | [sql-alterroutine](https://www.postgresql.org/docs/18/sql-alterroutine.html) |
| `ALTER RULE` | change the definition of a rule | 14 | [sql-alterrule](https://www.postgresql.org/docs/18/sql-alterrule.html) |
| `ALTER SCHEMA` | change the definition of a schema | 14 | [sql-alterschema](https://www.postgresql.org/docs/18/sql-alterschema.html) |
| `ALTER SEQUENCE` | change the definition of a sequence generator | 14 | [sql-altersequence](https://www.postgresql.org/docs/18/sql-altersequence.html) |
| `ALTER SERVER` | change the definition of a foreign server | 14 | [sql-alterserver](https://www.postgresql.org/docs/18/sql-alterserver.html) |
| `ALTER STATISTICS` | change the definition of an extended statistics object | 14 | [sql-alterstatistics](https://www.postgresql.org/docs/18/sql-alterstatistics.html) |
| `ALTER SUBSCRIPTION` | change the definition of a subscription | 14 | [sql-altersubscription](https://www.postgresql.org/docs/18/sql-altersubscription.html) |
| `ALTER SYSTEM` | change a server configuration parameter | 14 | [sql-altersystem](https://www.postgresql.org/docs/18/sql-altersystem.html) |
| `ALTER TABLE` | change the definition of a table | 14 | [sql-altertable](https://www.postgresql.org/docs/18/sql-altertable.html) |
| `ALTER TABLESPACE` | change the definition of a tablespace | 14 | [sql-altertablespace](https://www.postgresql.org/docs/18/sql-altertablespace.html) |
| `ALTER TEXT SEARCH CONFIGURATION` | change the definition of a text search configuration | 14 | [sql-altertsconfig](https://www.postgresql.org/docs/18/sql-altertsconfig.html) |
| `ALTER TEXT SEARCH DICTIONARY` | change the definition of a text search dictionary | 14 | [sql-altertsdictionary](https://www.postgresql.org/docs/18/sql-altertsdictionary.html) |
| `ALTER TEXT SEARCH PARSER` | change the definition of a text search parser | 14 | [sql-altertsparser](https://www.postgresql.org/docs/18/sql-altertsparser.html) |
| `ALTER TEXT SEARCH TEMPLATE` | change the definition of a text search template | 14 | [sql-altertstemplate](https://www.postgresql.org/docs/18/sql-altertstemplate.html) |
| `ALTER TRIGGER` | change the definition of a trigger | 14 | [sql-altertrigger](https://www.postgresql.org/docs/18/sql-altertrigger.html) |
| `ALTER TYPE` | change the definition of a type | 14 | [sql-altertype](https://www.postgresql.org/docs/18/sql-altertype.html) |
| `ALTER USER` | change a database role | 14 | [sql-alteruser](https://www.postgresql.org/docs/18/sql-alteruser.html) |
| `ALTER USER MAPPING` | change the definition of a user mapping | 14 | [sql-alterusermapping](https://www.postgresql.org/docs/18/sql-alterusermapping.html) |
| `ALTER VIEW` | change the definition of a view | 14 | [sql-alterview](https://www.postgresql.org/docs/18/sql-alterview.html) |
| `ANALYZE` | collect statistics about a database | 14 | [sql-analyze](https://www.postgresql.org/docs/18/sql-analyze.html) |
| `BEGIN` | start a transaction block | 14 | [sql-begin](https://www.postgresql.org/docs/18/sql-begin.html) |
| `CALL` | invoke a procedure | 14 | [sql-call](https://www.postgresql.org/docs/18/sql-call.html) |
| `CHECKPOINT` | force a write-ahead log checkpoint | 14 | [sql-checkpoint](https://www.postgresql.org/docs/18/sql-checkpoint.html) |
| `CLOSE` | close a cursor | 14 | [sql-close](https://www.postgresql.org/docs/18/sql-close.html) |
| `CLUSTER` | cluster a table according to an index | 14 | [sql-cluster](https://www.postgresql.org/docs/18/sql-cluster.html) |
| `COMMENT` | define or change the comment of an object | 14 | [sql-comment](https://www.postgresql.org/docs/18/sql-comment.html) |
| `COMMIT` | commit the current transaction | 14 | [sql-commit](https://www.postgresql.org/docs/18/sql-commit.html) |
| `COMMIT PREPARED` | commit a transaction that was earlier prepared for two-phase commit | 14 | [sql-commit-prepared](https://www.postgresql.org/docs/18/sql-commit-prepared.html) |
| `COPY` | copy data between a file and a table | 14 | [sql-copy](https://www.postgresql.org/docs/18/sql-copy.html) |
| `CREATE ACCESS METHOD` | define a new access method | 14 | [sql-create-access-method](https://www.postgresql.org/docs/18/sql-create-access-method.html) |
| `CREATE AGGREGATE` | define a new aggregate function | 14 | [sql-createaggregate](https://www.postgresql.org/docs/18/sql-createaggregate.html) |
| `CREATE CAST` | define a new cast | 14 | [sql-createcast](https://www.postgresql.org/docs/18/sql-createcast.html) |
| `CREATE COLLATION` | define a new collation | 14 | [sql-createcollation](https://www.postgresql.org/docs/18/sql-createcollation.html) |
| `CREATE CONVERSION` | define a new encoding conversion | 14 | [sql-createconversion](https://www.postgresql.org/docs/18/sql-createconversion.html) |
| `CREATE DATABASE` | create a new database | 14 | [sql-createdatabase](https://www.postgresql.org/docs/18/sql-createdatabase.html) |
| `CREATE DOMAIN` | define a new domain | 14 | [sql-createdomain](https://www.postgresql.org/docs/18/sql-createdomain.html) |
| `CREATE EVENT TRIGGER` | define a new event trigger | 14 | [sql-createeventtrigger](https://www.postgresql.org/docs/18/sql-createeventtrigger.html) |
| `CREATE EXTENSION` | install an extension | 14 | [sql-createextension](https://www.postgresql.org/docs/18/sql-createextension.html) |
| `CREATE FOREIGN DATA WRAPPER` | define a new foreign-data wrapper | 14 | [sql-createforeigndatawrapper](https://www.postgresql.org/docs/18/sql-createforeigndatawrapper.html) |
| `CREATE FOREIGN TABLE` | define a new foreign table | 14 | [sql-createforeigntable](https://www.postgresql.org/docs/18/sql-createforeigntable.html) |
| `CREATE FUNCTION` | define a new function | 14 | [sql-createfunction](https://www.postgresql.org/docs/18/sql-createfunction.html) |
| `CREATE GROUP` | define a new database role | 14 | [sql-creategroup](https://www.postgresql.org/docs/18/sql-creategroup.html) |
| `CREATE INDEX` | define a new index | 14 | [sql-createindex](https://www.postgresql.org/docs/18/sql-createindex.html) |
| `CREATE LANGUAGE` | define a new procedural language | 14 | [sql-createlanguage](https://www.postgresql.org/docs/18/sql-createlanguage.html) |
| `CREATE MATERIALIZED VIEW` | define a new materialized view | 14 | [sql-creatematerializedview](https://www.postgresql.org/docs/18/sql-creatematerializedview.html) |
| `CREATE OPERATOR` | define a new operator | 14 | [sql-createoperator](https://www.postgresql.org/docs/18/sql-createoperator.html) |
| `CREATE OPERATOR CLASS` | define a new operator class | 14 | [sql-createopclass](https://www.postgresql.org/docs/18/sql-createopclass.html) |
| `CREATE OPERATOR FAMILY` | define a new operator family | 14 | [sql-createopfamily](https://www.postgresql.org/docs/18/sql-createopfamily.html) |
| `CREATE POLICY` | define a new row-level security policy for a table | 14 | [sql-createpolicy](https://www.postgresql.org/docs/18/sql-createpolicy.html) |
| `CREATE PROCEDURE` | define a new procedure | 14 | [sql-createprocedure](https://www.postgresql.org/docs/18/sql-createprocedure.html) |
| `CREATE PUBLICATION` | define a new publication | 14 | [sql-createpublication](https://www.postgresql.org/docs/18/sql-createpublication.html) |
| `CREATE ROLE` | define a new database role | 14 | [sql-createrole](https://www.postgresql.org/docs/18/sql-createrole.html) |
| `CREATE RULE` | define a new rewrite rule | 14 | [sql-createrule](https://www.postgresql.org/docs/18/sql-createrule.html) |
| `CREATE SCHEMA` | define a new schema | 14 | [sql-createschema](https://www.postgresql.org/docs/18/sql-createschema.html) |
| `CREATE SEQUENCE` | define a new sequence generator | 14 | [sql-createsequence](https://www.postgresql.org/docs/18/sql-createsequence.html) |
| `CREATE SERVER` | define a new foreign server | 14 | [sql-createserver](https://www.postgresql.org/docs/18/sql-createserver.html) |
| `CREATE STATISTICS` | define extended statistics | 14 | [sql-createstatistics](https://www.postgresql.org/docs/18/sql-createstatistics.html) |
| `CREATE SUBSCRIPTION` | define a new subscription | 14 | [sql-createsubscription](https://www.postgresql.org/docs/18/sql-createsubscription.html) |
| `CREATE TABLE` | define a new table | 14 | [sql-createtable](https://www.postgresql.org/docs/18/sql-createtable.html) |
| `CREATE TABLE AS` | define a new table from the results of a query | 14 | [sql-createtableas](https://www.postgresql.org/docs/18/sql-createtableas.html) |
| `CREATE TABLESPACE` | define a new tablespace | 14 | [sql-createtablespace](https://www.postgresql.org/docs/18/sql-createtablespace.html) |
| `CREATE TEXT SEARCH CONFIGURATION` | define a new text search configuration | 14 | [sql-createtsconfig](https://www.postgresql.org/docs/18/sql-createtsconfig.html) |
| `CREATE TEXT SEARCH DICTIONARY` | define a new text search dictionary | 14 | [sql-createtsdictionary](https://www.postgresql.org/docs/18/sql-createtsdictionary.html) |
| `CREATE TEXT SEARCH PARSER` | define a new text search parser | 14 | [sql-createtsparser](https://www.postgresql.org/docs/18/sql-createtsparser.html) |
| `CREATE TEXT SEARCH TEMPLATE` | define a new text search template | 14 | [sql-createtstemplate](https://www.postgresql.org/docs/18/sql-createtstemplate.html) |
| `CREATE TRANSFORM` | define a new transform | 14 | [sql-createtransform](https://www.postgresql.org/docs/18/sql-createtransform.html) |
| `CREATE TRIGGER` | define a new trigger | 14 | [sql-createtrigger](https://www.postgresql.org/docs/18/sql-createtrigger.html) |
| `CREATE TYPE` | define a new data type | 14 | [sql-createtype](https://www.postgresql.org/docs/18/sql-createtype.html) |
| `CREATE USER` | define a new database role | 14 | [sql-createuser](https://www.postgresql.org/docs/18/sql-createuser.html) |
| `CREATE USER MAPPING` | define a new mapping of a user to a foreign server | 14 | [sql-createusermapping](https://www.postgresql.org/docs/18/sql-createusermapping.html) |
| `CREATE VIEW` | define a new view | 14 | [sql-createview](https://www.postgresql.org/docs/18/sql-createview.html) |
| `DEALLOCATE` | deallocate a prepared statement | 14 | [sql-deallocate](https://www.postgresql.org/docs/18/sql-deallocate.html) |
| `DECLARE` | define a cursor | 14 | [sql-declare](https://www.postgresql.org/docs/18/sql-declare.html) |
| `DELETE` | delete rows of a table | 14 | [sql-delete](https://www.postgresql.org/docs/18/sql-delete.html) |
| `DISCARD` | discard session state | 14 | [sql-discard](https://www.postgresql.org/docs/18/sql-discard.html) |
| `DO` | execute an anonymous code block | 14 | [sql-do](https://www.postgresql.org/docs/18/sql-do.html) |
| `DROP ACCESS METHOD` | remove an access method | 14 | [sql-drop-access-method](https://www.postgresql.org/docs/18/sql-drop-access-method.html) |
| `DROP AGGREGATE` | remove an aggregate function | 14 | [sql-dropaggregate](https://www.postgresql.org/docs/18/sql-dropaggregate.html) |
| `DROP CAST` | remove a cast | 14 | [sql-dropcast](https://www.postgresql.org/docs/18/sql-dropcast.html) |
| `DROP COLLATION` | remove a collation | 14 | [sql-dropcollation](https://www.postgresql.org/docs/18/sql-dropcollation.html) |
| `DROP CONVERSION` | remove a conversion | 14 | [sql-dropconversion](https://www.postgresql.org/docs/18/sql-dropconversion.html) |
| `DROP DATABASE` | remove a database | 14 | [sql-dropdatabase](https://www.postgresql.org/docs/18/sql-dropdatabase.html) |
| `DROP DOMAIN` | remove a domain | 14 | [sql-dropdomain](https://www.postgresql.org/docs/18/sql-dropdomain.html) |
| `DROP EVENT TRIGGER` | remove an event trigger | 14 | [sql-dropeventtrigger](https://www.postgresql.org/docs/18/sql-dropeventtrigger.html) |
| `DROP EXTENSION` | remove an extension | 14 | [sql-dropextension](https://www.postgresql.org/docs/18/sql-dropextension.html) |
| `DROP FOREIGN DATA WRAPPER` | remove a foreign-data wrapper | 14 | [sql-dropforeigndatawrapper](https://www.postgresql.org/docs/18/sql-dropforeigndatawrapper.html) |
| `DROP FOREIGN TABLE` | remove a foreign table | 14 | [sql-dropforeigntable](https://www.postgresql.org/docs/18/sql-dropforeigntable.html) |
| `DROP FUNCTION` | remove a function | 14 | [sql-dropfunction](https://www.postgresql.org/docs/18/sql-dropfunction.html) |
| `DROP GROUP` | remove a database role | 14 | [sql-dropgroup](https://www.postgresql.org/docs/18/sql-dropgroup.html) |
| `DROP INDEX` | remove an index | 14 | [sql-dropindex](https://www.postgresql.org/docs/18/sql-dropindex.html) |
| `DROP LANGUAGE` | remove a procedural language | 14 | [sql-droplanguage](https://www.postgresql.org/docs/18/sql-droplanguage.html) |
| `DROP MATERIALIZED VIEW` | remove a materialized view | 14 | [sql-dropmaterializedview](https://www.postgresql.org/docs/18/sql-dropmaterializedview.html) |
| `DROP OPERATOR` | remove an operator | 14 | [sql-dropoperator](https://www.postgresql.org/docs/18/sql-dropoperator.html) |
| `DROP OPERATOR CLASS` | remove an operator class | 14 | [sql-dropopclass](https://www.postgresql.org/docs/18/sql-dropopclass.html) |
| `DROP OPERATOR FAMILY` | remove an operator family | 14 | [sql-dropopfamily](https://www.postgresql.org/docs/18/sql-dropopfamily.html) |
| `DROP OWNED` | remove database objects owned by a database role | 14 | [sql-drop-owned](https://www.postgresql.org/docs/18/sql-drop-owned.html) |
| `DROP POLICY` | remove a row-level security policy from a table | 14 | [sql-droppolicy](https://www.postgresql.org/docs/18/sql-droppolicy.html) |
| `DROP PROCEDURE` | remove a procedure | 14 | [sql-dropprocedure](https://www.postgresql.org/docs/18/sql-dropprocedure.html) |
| `DROP PUBLICATION` | remove a publication | 14 | [sql-droppublication](https://www.postgresql.org/docs/18/sql-droppublication.html) |
| `DROP ROLE` | remove a database role | 14 | [sql-droprole](https://www.postgresql.org/docs/18/sql-droprole.html) |
| `DROP ROUTINE` | remove a routine | 14 | [sql-droproutine](https://www.postgresql.org/docs/18/sql-droproutine.html) |
| `DROP RULE` | remove a rewrite rule | 14 | [sql-droprule](https://www.postgresql.org/docs/18/sql-droprule.html) |
| `DROP SCHEMA` | remove a schema | 14 | [sql-dropschema](https://www.postgresql.org/docs/18/sql-dropschema.html) |
| `DROP SEQUENCE` | remove a sequence | 14 | [sql-dropsequence](https://www.postgresql.org/docs/18/sql-dropsequence.html) |
| `DROP SERVER` | remove a foreign server descriptor | 14 | [sql-dropserver](https://www.postgresql.org/docs/18/sql-dropserver.html) |
| `DROP STATISTICS` | remove extended statistics | 14 | [sql-dropstatistics](https://www.postgresql.org/docs/18/sql-dropstatistics.html) |
| `DROP SUBSCRIPTION` | remove a subscription | 14 | [sql-dropsubscription](https://www.postgresql.org/docs/18/sql-dropsubscription.html) |
| `DROP TABLE` | remove a table | 14 | [sql-droptable](https://www.postgresql.org/docs/18/sql-droptable.html) |
| `DROP TABLESPACE` | remove a tablespace | 14 | [sql-droptablespace](https://www.postgresql.org/docs/18/sql-droptablespace.html) |
| `DROP TEXT SEARCH CONFIGURATION` | remove a text search configuration | 14 | [sql-droptsconfig](https://www.postgresql.org/docs/18/sql-droptsconfig.html) |
| `DROP TEXT SEARCH DICTIONARY` | remove a text search dictionary | 14 | [sql-droptsdictionary](https://www.postgresql.org/docs/18/sql-droptsdictionary.html) |
| `DROP TEXT SEARCH PARSER` | remove a text search parser | 14 | [sql-droptsparser](https://www.postgresql.org/docs/18/sql-droptsparser.html) |
| `DROP TEXT SEARCH TEMPLATE` | remove a text search template | 14 | [sql-droptstemplate](https://www.postgresql.org/docs/18/sql-droptstemplate.html) |
| `DROP TRANSFORM` | remove a transform | 14 | [sql-droptransform](https://www.postgresql.org/docs/18/sql-droptransform.html) |
| `DROP TRIGGER` | remove a trigger | 14 | [sql-droptrigger](https://www.postgresql.org/docs/18/sql-droptrigger.html) |
| `DROP TYPE` | remove a data type | 14 | [sql-droptype](https://www.postgresql.org/docs/18/sql-droptype.html) |
| `DROP USER` | remove a database role | 14 | [sql-dropuser](https://www.postgresql.org/docs/18/sql-dropuser.html) |
| `DROP USER MAPPING` | remove a user mapping for a foreign server | 14 | [sql-dropusermapping](https://www.postgresql.org/docs/18/sql-dropusermapping.html) |
| `DROP VIEW` | remove a view | 14 | [sql-dropview](https://www.postgresql.org/docs/18/sql-dropview.html) |
| `END` | commit the current transaction | 14 | [sql-end](https://www.postgresql.org/docs/18/sql-end.html) |
| `EXECUTE` | execute a prepared statement | 14 | [sql-execute](https://www.postgresql.org/docs/18/sql-execute.html) |
| `EXPLAIN` | show the execution plan of a statement | 14 | [sql-explain](https://www.postgresql.org/docs/18/sql-explain.html) |
| `FETCH` | retrieve rows from a query using a cursor | 14 | [sql-fetch](https://www.postgresql.org/docs/18/sql-fetch.html) |
| `GRANT` | define access privileges | 14 | [sql-grant](https://www.postgresql.org/docs/18/sql-grant.html) |
| `IMPORT FOREIGN SCHEMA` | import table definitions from a foreign server | 14 | [sql-importforeignschema](https://www.postgresql.org/docs/18/sql-importforeignschema.html) |
| `INSERT` | create new rows in a table | 14 | [sql-insert](https://www.postgresql.org/docs/18/sql-insert.html) |
| `LISTEN` | listen for a notification | 14 | [sql-listen](https://www.postgresql.org/docs/18/sql-listen.html) |
| `LOAD` | load a shared library file | 14 | [sql-load](https://www.postgresql.org/docs/18/sql-load.html) |
| `LOCK` | lock a table | 14 | [sql-lock](https://www.postgresql.org/docs/18/sql-lock.html) |
| `MERGE` | conditionally insert, update, or delete rows of a table | 15 | [sql-merge](https://www.postgresql.org/docs/18/sql-merge.html) |
| `MOVE` | position a cursor | 14 | [sql-move](https://www.postgresql.org/docs/18/sql-move.html) |
| `NOTIFY` | generate a notification | 14 | [sql-notify](https://www.postgresql.org/docs/18/sql-notify.html) |
| `PREPARE` | prepare a statement for execution | 14 | [sql-prepare](https://www.postgresql.org/docs/18/sql-prepare.html) |
| `PREPARE TRANSACTION` | prepare the current transaction for two-phase commit | 14 | [sql-prepare-transaction](https://www.postgresql.org/docs/18/sql-prepare-transaction.html) |
| `REASSIGN OWNED` | change the ownership of database objects owned by a database role | 14 | [sql-reassign-owned](https://www.postgresql.org/docs/18/sql-reassign-owned.html) |
| `REFRESH MATERIALIZED VIEW` | replace the contents of a materialized view | 14 | [sql-refreshmaterializedview](https://www.postgresql.org/docs/18/sql-refreshmaterializedview.html) |
| `REINDEX` | rebuild indexes | 14 | [sql-reindex](https://www.postgresql.org/docs/18/sql-reindex.html) |
| `RELEASE SAVEPOINT` | release a previously defined savepoint | 14 | [sql-release-savepoint](https://www.postgresql.org/docs/18/sql-release-savepoint.html) |
| `RESET` | restore the value of a run-time parameter to the default value | 14 | [sql-reset](https://www.postgresql.org/docs/18/sql-reset.html) |
| `REVOKE` | remove access privileges | 14 | [sql-revoke](https://www.postgresql.org/docs/18/sql-revoke.html) |
| `ROLLBACK` | abort the current transaction | 14 | [sql-rollback](https://www.postgresql.org/docs/18/sql-rollback.html) |
| `ROLLBACK PREPARED` | cancel a transaction that was earlier prepared for two-phase commit | 14 | [sql-rollback-prepared](https://www.postgresql.org/docs/18/sql-rollback-prepared.html) |
| `ROLLBACK TO SAVEPOINT` | roll back to a savepoint | 14 | [sql-rollback-to](https://www.postgresql.org/docs/18/sql-rollback-to.html) |
| `SAVEPOINT` | define a new savepoint within the current transaction | 14 | [sql-savepoint](https://www.postgresql.org/docs/18/sql-savepoint.html) |
| `SECURITY LABEL` | define or change a security label applied to an object | 14 | [sql-security-label](https://www.postgresql.org/docs/18/sql-security-label.html) |
| `SELECT` | retrieve rows from a table or view | 14 | [sql-select](https://www.postgresql.org/docs/18/sql-select.html) |
| `SELECT INTO` | define a new table from the results of a query | 14 | [sql-selectinto](https://www.postgresql.org/docs/18/sql-selectinto.html) |
| `SET` | change a run-time parameter | 14 | [sql-set](https://www.postgresql.org/docs/18/sql-set.html) |
| `SET CONSTRAINTS` | set constraint check timing for the current transaction | 14 | [sql-set-constraints](https://www.postgresql.org/docs/18/sql-set-constraints.html) |
| `SET ROLE` | set the current user identifier of the current session | 14 | [sql-set-role](https://www.postgresql.org/docs/18/sql-set-role.html) |
| `SET SESSION AUTHORIZATION` | set the session user identifier and the current user identifier of the current session | 14 | [sql-set-session-authorization](https://www.postgresql.org/docs/18/sql-set-session-authorization.html) |
| `SET TRANSACTION` | set the characteristics of the current transaction | 14 | [sql-set-transaction](https://www.postgresql.org/docs/18/sql-set-transaction.html) |
| `SHOW` | show the value of a run-time parameter | 14 | [sql-show](https://www.postgresql.org/docs/18/sql-show.html) |
| `START TRANSACTION` | start a transaction block | 14 | [sql-start-transaction](https://www.postgresql.org/docs/18/sql-start-transaction.html) |
| `TABLE` | retrieve rows from a table or view | 14 | [sql-select](https://www.postgresql.org/docs/18/sql-select.html) |
| `TRUNCATE` | empty a table or set of tables | 14 | [sql-truncate](https://www.postgresql.org/docs/18/sql-truncate.html) |
| `UNLISTEN` | stop listening for a notification | 14 | [sql-unlisten](https://www.postgresql.org/docs/18/sql-unlisten.html) |
| `UPDATE` | update rows of a table | 14 | [sql-update](https://www.postgresql.org/docs/18/sql-update.html) |
| `VACUUM` | garbage-collect and optionally analyze a database | 14 | [sql-vacuum](https://www.postgresql.org/docs/18/sql-vacuum.html) |
| `VALUES` | compute a set of rows | 14 | [sql-values](https://www.postgresql.org/docs/18/sql-values.html) |
| `WITH` | retrieve rows from a table or view | 14 | [sql-select](https://www.postgresql.org/docs/18/sql-select.html) |

## Key words

The parser's key words and how each may be used as a name.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `ABORT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ABSENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ABSOLUTE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ACCESS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ACTION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ADD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ADMIN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `AFTER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `AGGREGATE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ALL` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ALSO` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ALTER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ALWAYS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ANALYSE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ANALYZE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `AND` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ANY` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ARRAY` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `AS` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ASC` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ASENSITIVE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ASSERTION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ASSIGNMENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ASYMMETRIC` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `AT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ATOMIC` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ATTACH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ATTRIBUTE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `AUTHORIZATION` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BACKWARD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BEFORE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BEGIN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BETWEEN` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BIGINT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BINARY` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BIT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BOOLEAN` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BOTH` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BREADTH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `BY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CACHE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CALL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CALLED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CASCADE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CASCADED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CASE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CAST` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CATALOG` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CHAIN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CHAR` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CHARACTER` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CHARACTERISTICS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CHECK` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CHECKPOINT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CLASS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CLOSE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CLUSTER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COALESCE` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COLLATE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COLLATION` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COLUMN` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COLUMNS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COMMENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COMMENTS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COMMIT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COMMITTED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COMPRESSION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONCURRENTLY` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONDITIONAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONFIGURATION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONFLICT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONNECTION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONSTRAINT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONSTRAINTS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONTENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONTINUE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CONVERSION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COPY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `COST` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CREATE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CROSS` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CSV` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CUBE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT_CATALOG` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT_DATE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT_ROLE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT_SCHEMA` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT_TIME` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT_TIMESTAMP` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURRENT_USER` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CURSOR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `CYCLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DATA` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DATABASE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DAY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEALLOCATE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEC` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DECIMAL` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DECLARE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEFAULT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEFAULTS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEFERRABLE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEFERRED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEFINER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DELETE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DELIMITER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DELIMITERS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEPENDS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DEPTH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DESC` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DETACH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DICTIONARY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DISABLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DISCARD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DISTINCT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DO` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DOCUMENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DOMAIN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DOUBLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `DROP` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EACH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ELSE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EMPTY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ENABLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ENCODING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ENCRYPTED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `END` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ENFORCED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 18 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ENUM` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ERROR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ESCAPE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EVENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXCEPT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXCLUDE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXCLUDING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXCLUSIVE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXECUTE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXISTS` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXPLAIN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXPRESSION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXTENSION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXTERNAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `EXTRACT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FALSE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FAMILY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FETCH` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FILTER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FINALIZE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FIRST` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FLOAT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FOLLOWING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FOR` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FORCE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FOREIGN` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FORMAT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FORWARD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FREEZE` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FROM` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FULL` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FUNCTION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `FUNCTIONS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GENERATED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GLOBAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GRANT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GRANTED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GREATEST` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GROUP` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GROUPING` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `GROUPS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `HANDLER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `HAVING` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `HEADER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `HOLD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `HOUR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IDENTITY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IF` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ILIKE` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IMMEDIATE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IMMUTABLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IMPLICIT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IMPORT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IN` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INCLUDE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INCLUDING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INCREMENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INDENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INDEX` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INDEXES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INHERIT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INHERITS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INITIALLY` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INLINE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INNER` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INOUT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INPUT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INSENSITIVE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INSERT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INSTEAD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INTEGER` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INTERSECT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INTERVAL` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INTO` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `INVOKER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `IS` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ISNULL` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ISOLATION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JOIN` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON` | Key word that can be used as a table or column name but not as a function or type name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_ARRAY` | Key word that can be used as a table or column name but not as a function or type name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_ARRAYAGG` | Key word that can be used as a table or column name but not as a function or type name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_EXISTS` | Key word that can be used as a table or column name but not as a function or type name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_OBJECT` | Key word that can be used as a table or column name but not as a function or type name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_OBJECTAGG` | Key word that can be used as a table or column name but not as a function or type name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_QUERY` | Key word that can be used as a table or column name but not as a function or type name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_SCALAR` | Key word that can be used as a table or column name but not as a function or type name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_SERIALIZE` | Key word that can be used as a table or column name but not as a function or type name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_TABLE` | Key word that can be used as a table or column name but not as a function or type name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `JSON_VALUE` | Key word that can be used as a table or column name but not as a function or type name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `KEEP` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `KEY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `KEYS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LABEL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LANGUAGE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LARGE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LAST` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LATERAL` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LEADING` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LEAKPROOF` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LEAST` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LEFT` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LEVEL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LIKE` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LIMIT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LISTEN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOAD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOCAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOCALTIME` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOCALTIMESTAMP` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOCATION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOCK` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOCKED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `LOGGED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MAPPING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MATCH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MATCHED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 15 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MATERIALIZED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MAXVALUE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MERGE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 15 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MERGE_ACTION` | Key word that can be used as a table or column name but not as a function or type name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `METHOD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MINUTE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MINVALUE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MODE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MONTH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `MOVE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NAME` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NAMES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NATIONAL` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NATURAL` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NCHAR` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NESTED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NEW` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NEXT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NFC` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NFD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NFKC` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NFKD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NO` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NONE` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NORMALIZE` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NORMALIZED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NOT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NOTHING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NOTIFY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NOTNULL` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NOWAIT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NULL` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NULLIF` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NULLS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `NUMERIC` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OBJECT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OBJECTS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 18 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OF` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OFF` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OFFSET` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OIDS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OLD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OMIT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ON` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ONLY` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OPERATOR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OPTION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OPTIONS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OR` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ORDER` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ORDINALITY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OTHERS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OUT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OUTER` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OVER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OVERLAPS` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OVERLAY` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OVERRIDING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OWNED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `OWNER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PARALLEL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PARAMETER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 15 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PARSER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PARTIAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PARTITION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PASSING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PASSWORD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PATH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PERIOD` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 18 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PLACING` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PLAN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PLANS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `POLICY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `POSITION` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PRECEDING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PRECISION` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PREPARE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PREPARED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PRESERVE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PRIMARY` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PRIOR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PRIVILEGES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PROCEDURAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PROCEDURE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PROCEDURES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PROGRAM` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `PUBLICATION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `QUOTE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `QUOTES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RANGE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `READ` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REAL` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REASSIGN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RECURSIVE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REF` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REFERENCES` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REFERENCING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REFRESH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REINDEX` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RELATIVE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RELEASE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RENAME` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REPEATABLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REPLACE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REPLICA` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RESET` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RESTART` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RESTRICT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RETURN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RETURNING` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RETURNS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `REVOKE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RIGHT` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ROLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ROLLBACK` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ROLLUP` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ROUTINE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ROUTINES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ROW` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ROWS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `RULE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SAVEPOINT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SCALAR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SCHEMA` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SCHEMAS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SCROLL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SEARCH` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SECOND` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SECURITY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SELECT` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SEQUENCE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SEQUENCES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SERIALIZABLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SERVER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SESSION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SESSION_USER` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SET` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SETOF` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SETS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SHARE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SHOW` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SIMILAR` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SIMPLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SKIP` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SMALLINT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SNAPSHOT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SOME` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SOURCE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SQL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STABLE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STANDALONE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `START` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STATEMENT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STATISTICS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STDIN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STDOUT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STORAGE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STORED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STRICT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STRING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `STRIP` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SUBSCRIPTION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SUBSTRING` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SUPPORT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SYMMETRIC` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SYSID` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SYSTEM` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `SYSTEM_USER` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 16 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TABLE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TABLES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TABLESAMPLE` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TABLESPACE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TARGET` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TEMP` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TEMPLATE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TEMPORARY` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TEXT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `THEN` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TIES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TIME` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TIMESTAMP` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TO` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRAILING` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRANSACTION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRANSFORM` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TREAT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRIGGER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRIM` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRUE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRUNCATE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TRUSTED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TYPE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `TYPES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UESCAPE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNBOUNDED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNCOMMITTED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNCONDITIONAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 17 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNENCRYPTED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNION` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNIQUE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNKNOWN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNLISTEN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNLOGGED` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UNTIL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `UPDATE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `USER` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `USING` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VACUUM` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VALID` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VALIDATE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VALIDATOR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VALUE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VALUES` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VARCHAR` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VARIADIC` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VARYING` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VERBOSE` | Key word that can be used as a function or type name but not as a table or column name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VERSION` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VIEW` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VIEWS` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VIRTUAL` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 18 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `VOLATILE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WHEN` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WHERE` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WHITESPACE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WINDOW` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WITH` | Reserved key word: it cannot be used as an unquoted table, column, function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WITHIN` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WITHOUT` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WORK` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WRAPPER` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `WRITE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XML` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLATTRIBUTES` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLCONCAT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLELEMENT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLEXISTS` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLFOREST` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLNAMESPACES` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLPARSE` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLPI` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLROOT` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLSERIALIZE` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `XMLTABLE` | Key word that can be used as a table or column name but not as a function or type name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `YEAR` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `YES` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| `ZONE` | Non-reserved key word: it has a special meaning only in some contexts and can be used as a name. | 14 | [sql-keywords-appendix](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |

## Data types

Types from the manual's data types tables, with the names they are also known by.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `any` | Indicates that a function accepts any input data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anyarray` | Indicates that a function accepts any array data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anycompatible` | Indicates that a function accepts any data type, with automatic promotion of multiple arguments to a common data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anycompatiblearray` | Indicates that a function accepts any array data type, with automatic promotion of multiple arguments to a common data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anycompatiblemultirange` | Indicates that a function accepts any multirange data type, with automatic promotion of multiple arguments to a common data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anycompatiblenonarray` | Indicates that a function accepts any non-array data type, with automatic promotion of multiple arguments to a common data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anycompatiblerange` | Indicates that a function accepts any range data type, with automatic promotion of multiple arguments to a common data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anyelement` | Indicates that a function accepts any data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anyenum` | Indicates that a function accepts any enum data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anymultirange` | Indicates that a function accepts any multirange data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anynonarray` | Indicates that a function accepts any non-array data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `anyrange` | Indicates that a function accepts any range data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `bigint (int8)` | signed eight-byte integer | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `bigserial (serial8)` | autoincrementing eight-byte integer | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `bit` | fixed-length bit string | 14 | [datatype-bit](https://www.postgresql.org/docs/18/datatype-bit.html) |
| `bit varying (varbit)` | variable-length bit string | 14 | [datatype-bit](https://www.postgresql.org/docs/18/datatype-bit.html) |
| `boolean (bool)` | logical Boolean (true/false) | 14 | [datatype-boolean](https://www.postgresql.org/docs/18/datatype-boolean.html) |
| `box` | rectangular box on a plane | 14 | [datatype-geometric](https://www.postgresql.org/docs/18/datatype-geometric.html) |
| `bytea` | binary data (byte array) | 14 | [datatype-binary](https://www.postgresql.org/docs/18/datatype-binary.html) |
| `character (char)` | fixed-length character string | 14 | [datatype-character](https://www.postgresql.org/docs/18/datatype-character.html) |
| `character varying (varchar)` | variable-length character string | 14 | [datatype-character](https://www.postgresql.org/docs/18/datatype-character.html) |
| `cidr` | IPv4 or IPv6 network address | 14 | [datatype-net-types](https://www.postgresql.org/docs/18/datatype-net-types.html) |
| `circle` | circle on a plane | 14 | [datatype-geometric](https://www.postgresql.org/docs/18/datatype-geometric.html) |
| `cstring` | Indicates that a function accepts or returns a null-terminated C string. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `date` | calendar date (year, month, day) | 14 | [datatype-datetime](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| `double precision (float, float8)` | double precision floating-point number (8 bytes) | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `event_trigger` | An event trigger function is declared to return event_trigger. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `fdw_handler` | A foreign-data wrapper handler is declared to return fdw_handler. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `index_am_handler` | An index access method handler is declared to return index_am_handler. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `inet` | IPv4 or IPv6 host address | 14 | [datatype-net-types](https://www.postgresql.org/docs/18/datatype-net-types.html) |
| `integer (int, int4)` | signed four-byte integer | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `internal` | Indicates that a function accepts or returns a server-internal data type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `interval` | time span | 14 | [datatype-datetime](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| `json` | textual JSON data | 14 | [datatype](https://www.postgresql.org/docs/18/datatype.html) |
| `jsonb` | binary JSON data, decomposed | 14 | [datatype](https://www.postgresql.org/docs/18/datatype.html) |
| `language_handler` | A procedural language call handler is declared to return language_handler. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `line` | infinite line on a plane | 14 | [datatype-geometric](https://www.postgresql.org/docs/18/datatype-geometric.html) |
| `lseg` | line segment on a plane | 14 | [datatype-geometric](https://www.postgresql.org/docs/18/datatype-geometric.html) |
| `macaddr` | MAC (Media Access Control) address | 14 | [datatype-net-types](https://www.postgresql.org/docs/18/datatype-net-types.html) |
| `macaddr8` | MAC (Media Access Control) address (EUI-64 format) | 14 | [datatype-net-types](https://www.postgresql.org/docs/18/datatype-net-types.html) |
| `money` | currency amount | 14 | [datatype-money](https://www.postgresql.org/docs/18/datatype-money.html) |
| `numeric (decimal)` | exact numeric of selectable precision | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `path` | geometric path on a plane | 14 | [datatype-geometric](https://www.postgresql.org/docs/18/datatype-geometric.html) |
| `pg_ddl_command` | Identifies a representation of DDL commands that is available to event triggers. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `pg_lsn` | PostgreSQL Log Sequence Number | 14 | [datatype-pg-lsn](https://www.postgresql.org/docs/18/datatype-pg-lsn.html) |
| `pg_snapshot` | user-level transaction ID snapshot | 14 | [datatype](https://www.postgresql.org/docs/18/datatype.html) |
| `point` | geometric point on a plane | 14 | [datatype-geometric](https://www.postgresql.org/docs/18/datatype-geometric.html) |
| `polygon` | closed geometric path on a plane | 14 | [datatype-geometric](https://www.postgresql.org/docs/18/datatype-geometric.html) |
| `real (float4)` | single precision floating-point number (4 bytes) | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `record` | Identifies a function taking or returning an unspecified row type. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `serial (serial4)` | autoincrementing four-byte integer | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `smallint (int2)` | signed two-byte integer | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `smallserial (serial2)` | autoincrementing two-byte integer | 14 | [datatype-numeric](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| `table_am_handler` | A table access method handler is declared to return table_am_handler. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `text` | variable-length character string | 14 | [datatype-character](https://www.postgresql.org/docs/18/datatype-character.html) |
| `time` | time of day (no time zone) | 14 | [datatype-datetime](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| `time with time zone (timetz)` | time of day, including time zone | 14 | [datatype-datetime](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| `timestamp` | date and time (no time zone) | 14 | [datatype-datetime](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| `timestamp with time zone (timestamptz)` | date and time, including time zone | 14 | [datatype-datetime](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| `trigger` | A trigger function is declared to return trigger. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `tsm_handler` | A tablesample method handler is declared to return tsm_handler. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `tsquery` | text search query | 14 | [datatype-textsearch](https://www.postgresql.org/docs/18/datatype-textsearch.html) |
| `tsvector` | text search document | 14 | [datatype-textsearch](https://www.postgresql.org/docs/18/datatype-textsearch.html) |
| `txid_snapshot` | user-level transaction ID snapshot (deprecated; see pg_snapshot) | 14 | [datatype](https://www.postgresql.org/docs/18/datatype.html) |
| `unknown` | Identifies a not-yet-resolved type, e.g., of an undecorated string literal. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `uuid` | universally unique identifier | 14 | [datatype-uuid](https://www.postgresql.org/docs/18/datatype-uuid.html) |
| `void` | Indicates that a function returns no value. | 14 | [datatype-pseudo](https://www.postgresql.org/docs/18/datatype-pseudo.html) |
| `xml` | XML data | 14 | [datatype-xml](https://www.postgresql.org/docs/18/datatype-xml.html) |

## Functions

Functions the manual documents. Overloads are merged into one entry.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `abbrev` | Creates an abbreviated display format as text | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `abs` | Absolute value | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `acldefault` | Constructs an aclitem array holding the default access privileges for an object of type type belonging to the role with OID ownerId | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `aclexplode` | Returns the aclitem array as a set of rows | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `acos` | Inverse cosine, result in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `acosd` | Inverse cosine, result in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `acosh` | Inverse hyperbolic cosine | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `age` | Subtract arguments, producing a symbolic result that uses years and months, rather than just days | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `area` | Computes area | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `array_append` | Appends an element to the end of an array (same as the anycompatiblearray \|\| anycompatible operator) | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_cat` | Concatenates two arrays (same as the anycompatiblearray \|\| anycompatiblearray operator) | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_dims` | Returns a text representation of the array's dimensions | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_fill` | Returns an array filled with copies of the given value, having dimensions of the lengths specified by the second argument | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_length` | Returns the length of the requested array dimension | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_lower` | Returns the lower bound of the requested array dimension | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_ndims` | Returns the number of dimensions of the array | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_position` | Returns the subscript of the first occurrence of the second argument in the array, or NULL if it's not present | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_positions` | Returns an array of the subscripts of all occurrences of the second argument in the array given as first argument | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_prepend` | Prepends an element to the beginning of an array (same as the anycompatible \|\| anycompatiblearray operator) | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_remove` | Removes all elements equal to the given value from the array | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_replace` | Replaces each array element equal to the second argument with the third argument | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_reverse` | Reverses the first dimension of the array | 18 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_sample` | Returns an array of n items randomly selected from array | 16 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_shuffle` | Randomly shuffles the first dimension of the array | 16 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_sort` | Sorts the first dimension of the array | 18 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_to_json` | Converts an SQL array to a JSON array | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `array_to_string` | Converts each array element to its text representation, and concatenates those separated by the delimiter string | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `array_to_tsvector` | Converts an array of text strings to a tsvector | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `array_upper` | Returns the upper bound of the requested array dimension | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `ascii` | Returns the numeric code of the first character of the argument | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `asin` | Inverse sine, result in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `asind` | Inverse sine, result in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `asinh` | Inverse hyperbolic sine | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `atan` | Inverse tangent, result in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `atan2` | Inverse tangent of y/x, result in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `atan2d` | Inverse tangent of y/x, result in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `atand` | Inverse tangent, result in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `atanh` | Inverse hyperbolic tangent | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `bit_count` | Returns the number of bits set in the binary string (also known as popcount) | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `bit_length` | Returns number of bits in the string (8 times the octet_length) | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `bound_box` | Computes bounding box of two boxes | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `box` | Computes box inscribed within the circle | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `brin_desummarize_range` | Removes the BRIN index tuple that summarizes the page range covering the given table block, if there is one | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `brin_summarize_new_values` | Scans the specified BRIN index to find page ranges in the base table that are not currently summarized by the index; for any such range it creates a new summary index tuple by scanning those table pages | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `brin_summarize_range` | Summarizes the page range covering the given block, if not already summarized | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `broadcast` | Computes the broadcast address for the address's network | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `btrim` | Removes the longest string containing only characters in characters (a space by default) from the start and end of string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `cardinality` | Returns the total number of elements in the array, or 0 if the array is empty | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `casefold` | Performs case folding of the input string according to the collation | 18 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `cbrt` | Cube root | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `ceil` | Nearest integer greater than or equal to argument | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `ceiling` | Nearest integer greater than or equal to argument (same as ceil) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `center` | Computes center point | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `character_length` | Returns number of characters in the string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `char_length` | Returns number of characters in the string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `chr` | Returns the character with the given code | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `circle` | Computes smallest circle enclosing box | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `clock_timestamp` | Current date and time (changes during statement execution) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `coalesce` | The COALESCE function returns the first of its arguments that is not null | 14 | [functions-conditional](https://www.postgresql.org/docs/18/functions-conditional.html) |
| `collation for` | Returns the name of the collation of the value that is passed to it | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `columns` | COLUMNS ( json_table_column , .. | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `col_description` | Returns the comment for a table column, which is specified by the OID of its table and its column number | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `concat` | Concatenates the text representations of all the arguments | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `concat_ws` | Concatenates all but the first argument, with separators | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `convert` | Converts a binary string representing text in encoding src_encoding to a binary string in encoding dest_encoding ( for available conversions) | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `convert_from` | Converts a binary string representing text in encoding src_encoding to text in the database encoding ( for available conversions) | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `convert_to` | Converts a text string (in the database encoding) to a binary string encoded in encoding dest_encoding ( for available conversions) | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `cos` | Cosine, argument in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `cosd` | Cosine, argument in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `cosh` | Hyperbolic cosine | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `cot` | Cotangent, argument in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `cotd` | Cotangent, argument in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `crc32` | Computes the CRC-32 value of the binary string | 18 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `crc32c` | Computes the CRC-32C value of the binary string | 18 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `current_catalog` | Returns the name of the current database | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `current_database` | Returns the name of the current database | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `current_date` | Current date | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `current_query` | Returns the text of the currently executing query, as submitted by the client (which might contain more than one statement) | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `current_role` | This is equivalent to current_user | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `current_schema` | Returns the name of the schema that is first in the search path (or a null value if the search path is empty) | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `current_schemas` | Returns an array of the names of all schemas presently in the effective search path, in their priority order | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `current_setting` | Returns the current value of the setting setting_name | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `current_time` | Current time of day | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `current_timestamp` | Current date and time (start of current transaction) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `current_user` | Returns the user name of the current execution context | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `currval` | Returns the value most recently obtained by nextval for this sequence in the current session | 14 | [functions-sequence](https://www.postgresql.org/docs/18/functions-sequence.html) |
| `cursor_to_xml` | cursor_to_xml fetches the indicated number of rows from the cursor specified by the parameter cursor | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `cursor_to_xmlschema` | map cursor structure to XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `database_to_xml` | map database contents to XML | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `database_to_xmlschema` | map database structure to XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `database_to_xml_and_xmlschema` | map database contents and structure to XML and XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `date_add` | Add an interval to a timestamp with time zone, computing times of day and daylight-savings adjustments according to the time zone named by the third argument, or the current setting if that is omitted | 16 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `date_bin` | Bin input into specified interval aligned with specified origin | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `date_part` | Get timestamp subfield (equivalent to extract) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `date_subtract` | Subtract an interval from a timestamp with time zone, computing times of day and daylight-savings adjustments according to the time zone named by the third argument, or the current setting if that is omitted | 16 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `date_trunc` | Truncate to specified precision | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `decode` | Decodes binary data from a textual representation; supported format values are the same as for encode | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `degrees` | Converts radians to degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `diagonal` | Extracts box's diagonal as a line segment (same as lseg(box)) | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `diameter` | Computes diameter of circle | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `div` | Integer quotient of y/x (truncates towards zero) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `encode` | Encodes binary data into a textual representation; supported format values are: base64, escape, hex | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `enum_first` | Returns the first value of the input enum type | 14 | [functions-enum](https://www.postgresql.org/docs/18/functions-enum.html) |
| `enum_last` | Returns the last value of the input enum type | 14 | [functions-enum](https://www.postgresql.org/docs/18/functions-enum.html) |
| `enum_range` | Returns all values of the input enum type in an ordered array | 14 | [functions-enum](https://www.postgresql.org/docs/18/functions-enum.html) |
| `erf` | Error function | 16 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `erfc` | Complementary error function (1 - erf(x), without loss of precision for large inputs) | 16 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `exists` | The argument of EXISTS is an arbitrary SELECT statement, or subquery | 14 | [functions-subquery](https://www.postgresql.org/docs/18/functions-subquery.html) |
| `exp` | Exponential (e raised to the given power) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `extract` | Get timestamp subfield | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `factorial` | Factorial | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `family` | Returns the address's family: 4 for IPv4, 6 for IPv6 | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `floor` | Nearest integer less than or equal to argument | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `format` | Formats arguments according to a format string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `format_type` | Returns the SQL name for a data type that is identified by its type OID and possibly a type modifier | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `gamma` | Gamma function | 18 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `gcd` | Greatest common divisor (the largest positive number that divides both inputs with no remainder); returns 0 if both inputs are zero; available for integer, bigint, and numeric | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `generate_series` | Generates a series of values from start to stop, with a step size of step | 14 | [functions-srf](https://www.postgresql.org/docs/18/functions-srf.html) |
| `generate_subscripts` | Generates a series comprising the valid subscripts of the dim'th dimension of the given array | 14 | [functions-srf](https://www.postgresql.org/docs/18/functions-srf.html) |
| `gen_random_uuid` | Generates a version 4 (random) UUID | 14 | [functions-uuid](https://www.postgresql.org/docs/18/functions-uuid.html) |
| `get_bit` | Extracts n'th bit from binary string | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `get_byte` | Extracts n'th byte from binary string | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `get_current_ts_config` | Returns the OID of the current default text search configuration (as set by ) | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `gin_clean_pending_list` | Cleans up the pending list of the specified GIN index by moving entries in it, in bulk, to the main GIN data structure | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `greatest` | The GREATEST and LEAST functions select the largest or smallest value from a list of any number of expressions | 14 | [functions-conditional](https://www.postgresql.org/docs/18/functions-conditional.html) |
| `grouping` | Returns a bit mask indicating which GROUP BY expressions are not included in the current grouping set | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `has_any_column_privilege` | Does user have privilege for any column of table? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_column_privilege` | Does user have privilege for the specified table column? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_database_privilege` | Does user have privilege for database? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_foreign_data_wrapper_privilege` | Does user have privilege for foreign-data wrapper? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_function_privilege` | Does user have privilege for function? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_language_privilege` | Does user have privilege for language? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_largeobject_privilege` | Does user have privilege for large object? | 18 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_parameter_privilege` | Does user have privilege for configuration parameter? | 15 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_schema_privilege` | Does user have privilege for schema? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_sequence_privilege` | Does user have privilege for sequence? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_server_privilege` | Does user have privilege for foreign server? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_tablespace_privilege` | Does user have privilege for tablespace? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_table_privilege` | Does user have privilege for table? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `has_type_privilege` | Does user have privilege for data type? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `height` | Computes vertical size of box | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `host` | Returns the IP address as text, ignoring the netmask | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `hostmask` | Computes the host mask for the address's network | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `icu_unicode_version` | Returns a string representing the version of Unicode used by ICU, if the server was built with ICU support; otherwise returns NULL | 17 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `inet_client_addr` | Returns the IP address of the current client, or NULL if the current connection is via a Unix-domain socket | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `inet_client_port` | Returns the IP port number of the current client, or NULL if the current connection is via a Unix-domain socket | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `inet_merge` | Computes the smallest network that includes both of the given networks | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `inet_same_family` | Tests whether the addresses belong to the same IP family | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `inet_server_addr` | Returns the IP address on which the server accepted the current connection, or NULL if the current connection is via a Unix-domain socket | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `inet_server_port` | Returns the IP port number on which the server accepted the current connection, or NULL if the current connection is via a Unix-domain socket | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `initcap` | Converts the first letter of each word to upper case and the rest to lower case | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `isclosed` | Is path closed? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `isempty` | Is the range empty? | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `isfinite` | Test for finite date (not +/-infinity) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `isopen` | Is path open? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `json` | Converts a given expression specified as text or bytea string (in UTF8 encoding) into a JSON value | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_array_elements` | Expands the top-level JSON array into a set of JSON values | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_array_elements_text` | Expands the top-level JSON array into a set of text values | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_array_length` | Returns the number of elements in the top-level JSON array | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_build_array` | Builds a possibly-heterogeneously-typed JSON array out of a variadic argument list | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_build_object` | Builds a JSON object out of a variadic argument list | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_each` | Expands the top-level JSON object into a set of key/value pairs | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_each_text` | Expands the top-level JSON object into a set of key/value pairs | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_extract_path` | Extracts JSON sub-object at the specified path | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_extract_path_text` | Extracts JSON sub-object at the specified path as text | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_insert` | Returns target with new_value inserted | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_object` | Builds a JSON object out of a text array | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_object_keys` | Returns the set of keys in the top-level JSON object | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_exists` | Checks whether the JSON path returns any item for the specified JSON value | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_exists_tz` | These functions act like their counterparts described above without the _tz suffix, except that these functions support comparisons of date/time values that require timezone-aware conversions | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_match` | Returns the SQL boolean result of a JSON path predicate check for the specified JSON value | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_match_tz` | These functions act like their counterparts described above without the _tz suffix, except that these functions support comparisons of date/time values that require timezone-aware conversions | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_query` | Returns all JSON items returned by the JSON path for the specified JSON value | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_query_array` | Returns all JSON items returned by the JSON path for the specified JSON value, as a JSON array | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_query_array_tz` | These functions act like their counterparts described above without the _tz suffix, except that these functions support comparisons of date/time values that require timezone-aware conversions | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_query_first` | Returns the first JSON item returned by the JSON path for the specified JSON value, or NULL if there are no results | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_query_first_tz` | These functions act like their counterparts described above without the _tz suffix, except that these functions support comparisons of date/time values that require timezone-aware conversions | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_path_query_tz` | These functions act like their counterparts described above without the _tz suffix, except that these functions support comparisons of date/time values that require timezone-aware conversions | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_populate_record` | Expands the top-level JSON object to a row having the composite type of the base argument | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_populate_recordset` | Expands the top-level JSON array of objects to a set of rows having the composite type of the base argument | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_populate_record_valid` | Function for testing jsonb_populate_record | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_pretty` | Converts the given JSON value to pretty-printed, indented text | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_set` | Returns target with the item designated by path replaced by new_value, or with new_value added if create_if_missing is true (which is the default) and the item designated by path does not exist | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_set_lax` | If new_value is not NULL, behaves identically to jsonb_set | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_strip_nulls` | Deletes all object fields that have null values from the given JSON value, recursively | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_to_record` | Expands the top-level JSON object to a row having the composite type defined by an AS clause | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_to_recordset` | Expands the top-level JSON array of objects to a set of rows having the composite type defined by an AS clause | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `jsonb_to_tsvector` | Selects each item in the JSON document that is requested by the filter and converts each one to a tsvector, normalizing words according to the specified or default configuration | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `jsonb_typeof` | Returns the type of the top-level JSON value as a text string | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_array` | Constructs a JSON array from either a series of value_expression parameters or from the results of query_expression, which must be a SELECT query returning a single column | 16 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_arrayagg` | Behaves in the same way as json_array but as an aggregate function so it only takes one value_expression parameter | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_array_elements` | Expands the top-level JSON array into a set of JSON values | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_array_elements_text` | Expands the top-level JSON array into a set of text values | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_array_length` | Returns the number of elements in the top-level JSON array | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_build_array` | Builds a possibly-heterogeneously-typed JSON array out of a variadic argument list | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_build_object` | Builds a JSON object out of a variadic argument list | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_each` | Expands the top-level JSON object into a set of key/value pairs | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_each_text` | Expands the top-level JSON object into a set of key/value pairs | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_exists` | Returns true if the SQL/JSON path_expression applied to the context_item yields any items, false otherwise | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_extract_path` | Extracts JSON sub-object at the specified path | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_extract_path_text` | Extracts JSON sub-object at the specified path as text | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_object` | Constructs a JSON object of all the key/value pairs given, or an empty object if none are given | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_objectagg` | Behaves like json_object, but as an aggregate function, so it only takes one key_expression and one value_expression parameter | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_object_keys` | Returns the set of keys in the top-level JSON object | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_populate_record` | Expands the top-level JSON object to a row having the composite type of the base argument | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_populate_recordset` | Expands the top-level JSON array of objects to a set of rows having the composite type of the base argument | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_query` | Returns the result of applying the SQL/JSON path_expression to the context_item | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_scalar` | Converts a given SQL scalar value into a JSON scalar value | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_serialize` | Converts an SQL/JSON expression into a character or binary string | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_strip_nulls` | Deletes all object fields that have null values from the given JSON value, recursively | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_table` | Each syntax element is described below in more detail | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_to_record` | Expands the top-level JSON object to a row having the composite type defined by an AS clause | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_to_recordset` | Expands the top-level JSON array of objects to a set of rows having the composite type defined by an AS clause | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_to_tsvector` | Selects each item in the JSON document that is requested by the filter and converts each one to a tsvector, normalizing words according to the specified or default configuration | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `json_typeof` | Returns the type of the top-level JSON value as a text string | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `json_value` | Returns the result of applying the SQL/JSON path_expression to the context_item | 17 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `justify_days` | Adjust interval, converting 30-day time periods to months | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `justify_hours` | Adjust interval, converting 24-hour time periods to days | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `justify_interval` | Adjust interval using justify_days and justify_hours, with additional sign adjustments | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `lastval` | Returns the value most recently returned by nextval in the current session | 14 | [functions-sequence](https://www.postgresql.org/docs/18/functions-sequence.html) |
| `lcm` | Least common multiple (the smallest strictly positive number that is an integral multiple of both inputs); returns 0 if either input is zero; available for integer, bigint, and numeric | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `least` | The GREATEST and LEAST functions select the largest or smallest value from a list of any number of expressions | 14 | [functions-conditional](https://www.postgresql.org/docs/18/functions-conditional.html) |
| `left` | Returns first n characters in the string, or when n is negative, returns all but last \|n\| characters | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `length` | Returns the number of characters in the string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `lgamma` | Natural logarithm of the absolute value of the gamma function | 18 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `line` | Converts two points to the line through them | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `ln` | Natural logarithm | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `localtime` | Current time of day | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `localtimestamp` | Current date and time (start of current transaction) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `log` | Base 10 logarithm | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `log10` | Base 10 logarithm (same as log) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `lower` | Converts the string to all lower case, according to the rules of the database's locale | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `lower_inc` | Is the range's lower bound inclusive? | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `lower_inf` | Does the range have no lower bound? | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `lpad` | Extends the string to length length by prepending the characters fill (a space by default) | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `lseg` | Extracts box's diagonal as a line segment | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `ltrim` | Removes the longest string containing only characters in characters (a space by default) from the start of string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `macaddr8_set7bit` | Sets the 7th bit of the address to one, creating what is known as modified EUI-64, for inclusion in an IPv6 address | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `makeaclitem` | Constructs an aclitem with the given properties | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `make_date` | Create date from year, month and day fields (negative years signify BC) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `make_interval` | Create interval from years, months, weeks, days, hours, minutes and seconds fields, each of which can default to zero | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `make_time` | Create time from hour, minute and seconds fields | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `make_timestamp` | Create timestamp from year, month, day, hour, minute and seconds fields (negative years signify BC) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `make_timestamptz` | Create timestamp with time zone from year, month, day, hour, minute and seconds fields (negative years signify BC) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `masklen` | Returns the netmask length in bits | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `md5` | Computes the MD5 hash of the argument, with the result written in hexadecimal | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `merge_action` | Returns the merge action command executed for the current row | 17 | [functions-merge-support](https://www.postgresql.org/docs/18/functions-merge-support.html) |
| `min_scale` | Minimum scale (number of fractional decimal digits) needed to represent the supplied value precisely | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `mod` | Remainder of y/x; available for smallint, integer, bigint, and numeric | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `multirange` | Returns a multirange containing just the given range | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `mxid_age` | Returns the number of multixacts IDs between the supplied multixact ID and the current multixacts counter | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `netmask` | Computes the network mask for the address's network | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `network` | Returns the network part of the address, zeroing out whatever is to the right of the netmask | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `nextval` | Advances the sequence object to its next value and returns that value | 14 | [functions-sequence](https://www.postgresql.org/docs/18/functions-sequence.html) |
| `normalize` | Converts the string to the specified Unicode normalization form | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `now` | Current date and time (start of current transaction) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `npoints` | Returns the number of points | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `nullif` | The NULLIF function returns a null value if value1 equals value2; otherwise it returns value1 | 14 | [functions-conditional](https://www.postgresql.org/docs/18/functions-conditional.html) |
| `numnode` | Returns the number of lexemes plus operators in the tsquery | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `num_nonnulls` | Returns the number of non-null arguments | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `num_nulls` | Returns the number of null arguments | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `obj_description` | Returns the comment for a database object specified by its OID and the name of the containing system catalog | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `octet_length` | Returns number of bytes in the string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `overlay` | Replaces the substring of string that starts at the start'th character and extends for count characters with newsubstring | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `parse_ident` | Splits qualified_identifier into an array of identifiers, removing any quoting of individual identifiers | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `path` | Converts polygon to a closed path with the same list of points | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `pclose` | Converts path to closed form | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `pg_advisory_lock` | Obtains an exclusive session-level advisory lock, waiting if necessary | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_advisory_lock_shared` | Obtains a shared session-level advisory lock, waiting if necessary | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_advisory_unlock` | Releases a previously-acquired exclusive session-level advisory lock | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_advisory_unlock_all` | Releases all session-level advisory locks held by the current session | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_advisory_unlock_shared` | Releases a previously-acquired shared session-level advisory lock | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_advisory_xact_lock` | Obtains an exclusive transaction-level advisory lock, waiting if necessary | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_advisory_xact_lock_shared` | Obtains a shared transaction-level advisory lock, waiting if necessary | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_available_wal_summaries` | Returns information about the WAL summary files present in the data directory, under pg_wal/summaries | 17 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_backend_pid` | Returns the process ID of the server process attached to the current session | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_backup_start` | Prepares the server to begin an on-line backup | 15 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_backup_stop` | Finishes performing an on-line backup | 15 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_basetype` | Returns the OID of the base type of a domain identified by its type OID | 17 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_blocking_pids` | Returns an array of the process ID(s) of the sessions that are blocking the server process with the specified process ID from acquiring a lock, or an empty array if there is no such server process or it is not blocked | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_cancel_backend` | Cancels the current query of the session whose backend process has the specified process ID | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_char_to_encoding` | Converts the supplied encoding name into an integer representing the internal identifier used in some system catalog tables | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_clear_attribute_stats` | Clears column-level statistics for the given relation and attribute, as though the table was newly created | 18 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_clear_relation_stats` | Clears table-level statistics for the given relation, as though the table was newly created | 18 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_client_encoding` | Returns current client encoding name | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `pg_collation_actual_version` | Returns the actual version of the collation object as it is currently installed in the operating system | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_collation_is_visible` | Is collation visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_column_compression` | Shows the compression algorithm that was used to compress an individual variable-length value | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_column_size` | Shows the number of bytes used to store any individual data value | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_column_toast_chunk_id` | Shows the chunk_id of an on-disk TOASTed value | 17 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_conf_load_time` | Returns the time when the server configuration files were last loaded | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_control_checkpoint` | Returns information about current checkpoint state, as shown | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_control_init` | Returns information about cluster initialization state, as shown | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_control_recovery` | Returns information about recovery state, as shown | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_control_system` | Returns information about current control file state, as shown | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_conversion_is_visible` | Is conversion visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_copy_logical_replication_slot` | Copies an existing logical replication slot named src_slot_name to a logical replication slot named dst_slot_name, optionally changing the output plugin and persistence | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_copy_physical_replication_slot` | Copies an existing physical replication slot named src_slot_name to a physical replication slot named dst_slot_name | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_create_logical_replication_slot` | Creates a new logical (decoding) replication slot named slot_name using the output plugin plugin | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_create_physical_replication_slot` | Creates a new physical replication slot named slot_name | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_create_restore_point` | Creates a named marker record in the write-ahead log that can later be used as a recovery target, and returns the corresponding write-ahead log location | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_current_logfile` | Returns the path name of the log file currently in use by the logging collector | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_current_snapshot` | Returns a current snapshot, a data structure showing which transaction IDs are now in-progress | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_current_wal_flush_lsn` | Returns the current write-ahead log flush location (see notes below) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_current_wal_insert_lsn` | Returns the current write-ahead log insert location (see notes below) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_current_wal_lsn` | Returns the current write-ahead log write location (see notes below) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_current_xact_id` | Returns the current transaction's ID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_current_xact_id_if_assigned` | Returns the current transaction's ID, or NULL if no ID is assigned yet | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_database_collation_actual_version` | Returns the actual version of the database's collation as it is currently installed in the operating system | 15 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_database_size` | Computes the total disk space used by the database with the specified name or OID | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_describe_object` | Returns a textual description of a database object identified by catalog OID, object OID, and sub-object ID (such as a column number within a table; the sub-object ID is zero when referring to a whole object) | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_drop_replication_slot` | Drops the physical or logical replication slot named slot_name | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_encoding_to_char` | Converts the integer used as the internal identifier of an encoding in some system catalog tables into a human-readable string | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_event_trigger_ddl_commands` | pg_event_trigger_ddl_commands returns a list of DDL commands executed by each user action, when invoked in a function attached to a ddl_command_end event trigger | 14 | [functions-event-triggers](https://www.postgresql.org/docs/18/functions-event-triggers.html) |
| `pg_event_trigger_dropped_objects` | pg_event_trigger_dropped_objects returns a list of all objects dropped by the command in whose sql_drop event it is called | 14 | [functions-event-triggers](https://www.postgresql.org/docs/18/functions-event-triggers.html) |
| `pg_event_trigger_table_rewrite_oid` | Returns the OID of the table about to be rewritten | 14 | [functions-event-triggers](https://www.postgresql.org/docs/18/functions-event-triggers.html) |
| `pg_event_trigger_table_rewrite_reason` | Returns a code explaining the reason(s) for rewriting | 14 | [functions-event-triggers](https://www.postgresql.org/docs/18/functions-event-triggers.html) |
| `pg_export_snapshot` | Saves the transaction's current snapshot and returns a text string identifying the snapshot | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_filenode_relation` | Returns a relation's OID given the tablespace OID and filenode it is stored under | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_function_is_visible` | Is function visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_acl` | Returns the ACL for a database object, specified by catalog OID, object OID and sub-object ID | 18 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_catalog_foreign_keys` | Returns a set of records describing the foreign key relationships that exist within the PostgreSQL system catalogs | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_constraintdef` | Reconstructs the creating command for a constraint | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_expr` | Decompiles the internal form of an expression stored in the system catalogs, such as the default value for a column | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_functiondef` | Reconstructs the creating command for a function or procedure | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_function_arguments` | Reconstructs the argument list of a function or procedure, in the form it would need to appear in within CREATE FUNCTION (including default values) | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_function_identity_arguments` | Reconstructs the argument list necessary to identify a function or procedure, in the form it would need to appear in within commands such as ALTER FUNCTION | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_function_result` | Reconstructs the RETURNS clause of a function, in the form it would need to appear in within CREATE FUNCTION | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_indexdef` | Reconstructs the creating command for an index | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_keywords` | Returns a set of records describing the SQL keywords recognized by the server | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_loaded_modules` | Returns a list of the loadable modules that are loaded into the current server session | 18 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_multixact_members` | Returns the transaction ID and lock mode for each member of the specified multixact ID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_object_address` | Returns a row containing enough information to uniquely identify the database object specified by a type code and object name and argument arrays | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_partition_constraintdef` | Reconstructs the definition of a partition constraint | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_partkeydef` | Reconstructs the definition of a partitioned table's partition key, in the form it would have in the PARTITION BY clause of CREATE TABLE | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_ruledef` | Reconstructs the creating command for a rule | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_serial_sequence` | Returns the name of the sequence associated with a column, or NULL if no sequence is associated with the column | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_statisticsobjdef` | Reconstructs the creating command for an extended statistics object | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_triggerdef` | Reconstructs the creating command for a trigger | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_userbyid` | Returns a role's name given its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_viewdef` | Reconstructs the underlying SELECT command for a view or materialized view | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_get_wal_replay_pause_state` | Returns recovery pause state | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_get_wal_resource_managers` | Returns the currently-loaded WAL resource managers in the system | 15 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_get_wal_summarizer_state` | Returns information about the progress of the WAL summarizer | 17 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_has_role` | Does user have privilege for role? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_identify_object` | Returns a row containing enough information to uniquely identify the database object specified by catalog OID, object OID and sub-object ID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_identify_object_as_address` | Returns a row containing enough information to uniquely identify the database object specified by catalog OID, object OID and sub-object ID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_import_system_collations` | Adds collations to the system catalog pg_collation based on all the locales it finds in the operating system | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_indexam_has_property` | Tests whether an index access method has the named property | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_indexes_size` | Computes the total disk space used by indexes attached to the specified table | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_index_column_has_property` | Tests whether an index column has the named property | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_index_has_property` | Tests whether an index has the named property | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_input_error_info` | Tests whether the given string is valid input for the specified data type; if not, return the details of the error that would have been thrown | 16 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_input_is_valid` | Tests whether the given string is valid input for the specified data type, returning true or false | 16 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_is_in_recovery` | Returns true if recovery is still in progress | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_is_other_temp_schema` | Returns true if the given OID is the OID of another session's temporary schema | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_is_wal_replay_paused` | Returns true if recovery pause is requested | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_jit_available` | Returns true if a JIT compiler extension is available and the configuration parameter is set | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_last_committed_xact` | Returns the transaction ID, commit timestamp and replication origin of the latest committed transaction | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_last_wal_receive_lsn` | Returns the last write-ahead log location that has been received and synced to disk by streaming replication | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_last_wal_replay_lsn` | Returns the last write-ahead log location that has been replayed during recovery | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_last_xact_replay_timestamp` | Returns the time stamp of the last transaction replayed during recovery | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_listening_channels` | Returns the set of names of asynchronous notification channels that the current session is listening to | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_logical_emit_message` | Emits a logical decoding message | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_logical_slot_get_binary_changes` | Behaves just like the pg_logical_slot_get_changes() function, except that changes are returned as bytea | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_logical_slot_get_changes` | Returns changes in the slot slot_name, starting from the point from which changes have been consumed last | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_logical_slot_peek_binary_changes` | Behaves just like the pg_logical_slot_peek_changes() function, except that changes are returned as bytea | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_logical_slot_peek_changes` | Behaves just like the pg_logical_slot_get_changes() function, except that changes are not consumed; that is, they will be returned again on future calls | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_log_backend_memory_contexts` | Requests to log the memory contexts of the backend with the specified process ID | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_log_standby_snapshot` | Take a snapshot of running transactions and write it to WAL, without having to wait for bgwriter or checkpointer to log one | 16 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_archive_statusdir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the server's WAL archive status directory (pg_wal/archive_status) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_dir` | Returns the names of all files (and directories and other special files) in the specified directory | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_logdir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the server's log directory | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_logicalmapdir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the server's pg_logical/mappings directory | 15 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_logicalsnapdir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the server's pg_logical/snapshots directory | 15 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_replslotdir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the server's pg_replslot/slot_name directory, where slot_name is the name of the replication slot provided as input of the function | 15 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_summariesdir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the server's WAL summaries directory (pg_wal/summaries) | 18 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_tmpdir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the temporary file directory for the specified tablespace | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ls_waldir` | Returns the name, size, and last modification time (mtime) of each ordinary file in the server's write-ahead log (WAL) directory | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_mcv_list_items` | pg_mcv_list_items returns a set of records describing all items stored in a multi-column MCV list | 14 | [functions-statistics](https://www.postgresql.org/docs/18/functions-statistics.html) |
| `pg_my_temp_schema` | Returns the OID of the current session's temporary schema, or zero if it has none (because it has not created any temporary tables) | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_notification_queue_usage` | Returns the fraction (0-1) of the asynchronous notification queue's maximum size that is currently occupied by notifications that are waiting to be processed | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_numa_available` | Returns true if the server has been compiled with NUMA support | 18 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_opclass_is_visible` | Is operator class visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_operator_is_visible` | Is operator visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_opfamily_is_visible` | Is operator family visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_options_to_table` | Returns the set of storage options represented by a value from pg_class.reloptions or pg_attribute.attoptions | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_partition_ancestors` | Lists the ancestor relations of the given partition, including the relation itself | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_partition_root` | Returns the top-most parent of the partition tree to which the given relation belongs | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_partition_tree` | Lists the tables or indexes in the partition tree of the given partitioned table or partitioned index, with one row for each partition | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_postmaster_start_time` | Returns the time when the server started | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_promote` | Promotes a standby server to primary status | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_read_binary_file` | Returns all or part of a file | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_read_file` | Returns all or part of a text file, starting at the given byte offset, returning at most length bytes (less if the end of file is reached first) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_relation_filenode` | Returns the filenode number currently assigned to the specified relation | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_relation_filepath` | Returns the entire file path name (relative to the database cluster's data directory, PGDATA) of the relation | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_relation_size` | Computes the disk space used by one fork of the specified relation | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_reload_conf` | Causes all processes of the PostgreSQL server to reload their configuration files | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_advance` | Sets replication progress for the given node to the given location | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_create` | Creates a replication origin with the given external name, and returns the internal ID assigned to it | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_drop` | Deletes a previously-created replication origin, including any associated replay progress | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_oid` | Looks up a replication origin by name and returns the internal ID | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_progress` | Returns the replay location for the given replication origin | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_session_is_setup` | Returns true if a replication origin has been selected in the current session | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_session_progress` | Returns the replay location for the replication origin selected in the current session | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_session_reset` | Cancels the effects of pg_replication_origin_session_setup() | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_session_setup` | Marks the current session as replaying from the given origin, allowing replay progress to be tracked | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_xact_reset` | Cancels the effects of pg_replication_origin_xact_setup() | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_origin_xact_setup` | Marks the current transaction as replaying a transaction that has committed at the given LSN and timestamp | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_replication_slot_advance` | Advances the current confirmed position of a replication slot named slot_name | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_restore_attribute_stats` | Creates or updates column-level statistics | 18 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_restore_relation_stats` | Updates table-level statistics | 18 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_rotate_logfile` | Signals the log-file manager to switch to a new output file immediately | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_safe_snapshot_blocking_pids` | Returns an array of the process ID(s) of the sessions that are blocking the server process with the specified process ID from acquiring a safe snapshot, or an empty array if there is no such server process or it is not blocked | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_settings_get_flags` | Returns an array of the flags associated with the given GUC, or NULL if it does not exist | 15 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_size_bytes` | Converts a size in human-readable format (as returned by pg_size_pretty) into bytes | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_size_pretty` | Converts a size in bytes into a more easily human-readable format with size units (bytes, kB, MB, GB, TB, or PB as appropriate) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_sleep` | pg_sleep makes the current session's process sleep until the given number of seconds have elapsed | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `pg_sleep_for` | pg_sleep_for is a convenience function to allow the sleep time to be specified as an interval | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `pg_sleep_until` | pg_sleep_until is a convenience function for when a specific wake-up time is desired | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `pg_snapshot_xip` | Returns the set of in-progress transaction IDs contained in a snapshot | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_snapshot_xmax` | Returns the xmax of a snapshot | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_snapshot_xmin` | Returns the xmin of a snapshot | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_split_walfile_name` | Extracts the sequence number and timeline ID from a WAL file name | 16 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_statistics_obj_is_visible` | Is statistics object visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_stat_file` | Returns a record containing the file's size, last access time stamp, last modification time stamp, last file status change time stamp (Unix platforms only), file creation time stamp (Windows only), and a flag indicating if it is a directory | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_switch_wal` | Forces the server to switch to a new write-ahead log file, which allows the current file to be archived (assuming you are using continuous archiving) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_sync_replication_slots` | Synchronize the logical failover replication slots from the primary server to the standby server | 17 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_tablespace_databases` | Returns the set of OIDs of databases that have objects stored in the specified tablespace | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_tablespace_location` | Returns the file system path that this tablespace is located in | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_tablespace_size` | Computes the total disk space used in the tablespace with the specified name or OID | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_table_is_visible` | Is table visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_table_size` | Computes the disk space used by the specified table, excluding indexes (but including its TOAST table if any, free space map, and visibility map) | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_terminate_backend` | Terminates the session whose backend process has the specified process ID | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_total_relation_size` | Computes the total disk space used by the specified table, including all indexes and TOAST data | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_trigger_depth` | Returns the current nesting level of PostgreSQL triggers (0 if not called, directly or indirectly, from inside a trigger) | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_try_advisory_lock` | Obtains an exclusive session-level advisory lock if available | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_try_advisory_lock_shared` | Obtains a shared session-level advisory lock if available | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_try_advisory_xact_lock` | Obtains an exclusive transaction-level advisory lock if available | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_try_advisory_xact_lock_shared` | Obtains a shared transaction-level advisory lock if available | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_ts_config_is_visible` | Is text search configuration visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_ts_dict_is_visible` | Is text search dictionary visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_ts_parser_is_visible` | Is text search parser visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_ts_template_is_visible` | Is text search template visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_typeof` | Returns the OID of the data type of the value that is passed to it | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_type_is_visible` | Is type (or domain) visible in search path? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_visible_in_snapshot` | Is the given transaction ID visible according to this snapshot (that is, was it completed before the snapshot was taken)? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_walfile_name` | Converts a write-ahead log location to the name of the WAL file holding that location | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_walfile_name_offset` | Converts a write-ahead log location to a WAL file name and byte offset within that file | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_wal_lsn_diff` | Calculates the difference in bytes (lsn1 - lsn2) between two write-ahead log locations | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_wal_replay_pause` | Request to pause recovery | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_wal_replay_resume` | Restarts recovery if it was paused | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `pg_wal_summary_contents` | Returns one information about the contents of a single WAL summary file identified by TLI and starting and ending LSNs | 17 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_xact_commit_timestamp` | Returns the commit timestamp of a transaction | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_xact_commit_timestamp_origin` | Returns the commit timestamp and replication origin of a transaction | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `pg_xact_status` | Reports the commit status of a recent transaction | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `phraseto_tsquery` | Converts text to a tsquery, normalizing words according to the specified or default configuration | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `pi` | Approximate value of pi | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `plainto_tsquery` | Converts text to a tsquery, normalizing words according to the specified or default configuration | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `point` | Constructs point from its coordinates | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `polygon` | Converts box to a 4-point polygon | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `popen` | Converts path to open form | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `position` | Returns first starting index of the specified substring within string, or zero if it's not present | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `power` | a raised to the power of b | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `querytree` | Produces a representation of the indexable portion of a tsquery | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `query_to_xml` | query_to_xml executes the query whose text is passed as parameter query and maps the result set | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `query_to_xmlschema` | map query result structure to XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `query_to_xml_and_xmlschema` | map query result and structure to XML and XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `quote_ident` | Returns the given string suitably quoted to be used as an identifier in an SQL statement string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `quote_literal` | Returns the given string suitably quoted to be used as a string literal in an SQL statement string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `quote_nullable` | Returns the given string suitably quoted to be used as a string literal in an SQL statement string; or, if the argument is null, returns NULL | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `radians` | Converts degrees to radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `radius` | Computes radius of circle | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `random` | Returns a random value in the range 0.0 <= x < 1.0 | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `random_normal` | Returns a random value from the normal distribution with the given parameters; mean defaults to 0.0 and stddev defaults to 1.0 | 16 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `range_merge` | Computes the smallest range that includes both of the given ranges | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `regexp_count` | Returns the number of times the POSIX regular expression pattern matches in the string | 15 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_instr` | Returns the position within string where the N'th match of the POSIX regular expression pattern occurs, or zero if there is no such match | 15 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_like` | Checks whether a match of the POSIX regular expression pattern occurs within string | 15 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_match` | Returns substrings within the first match of the POSIX regular expression pattern to the string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_matches` | Returns substrings within the first match of the POSIX regular expression pattern to the string, or substrings within all such matches if the g flag is used | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_replace` | Replaces the substring that is the first match to the POSIX regular expression pattern, or all such matches if the g flag is used | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_split_to_array` | Splits string using a POSIX regular expression as the delimiter, producing an array of results | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_split_to_table` | Splits string using a POSIX regular expression as the delimiter, producing a set of results | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `regexp_substr` | Returns the substring within string that matches the N'th occurrence of the POSIX regular expression pattern, or NULL if there is no such match | 15 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `repeat` | Repeats string the specified number of times | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `replace` | Replaces all occurrences in string of substring from with substring to | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `reverse` | Reverses the order of the characters in the string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `right` | Returns last n characters in the string, or when n is negative, returns all but first \|n\| characters | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `round` | Rounds to nearest integer | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `row_security_active` | Is row-level security active for the specified table in the context of the current user and current environment? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `row_to_json` | Converts an SQL composite value to a JSON object | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `rpad` | Extends the string to length length by appending the characters fill (a space by default) | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `rtrim` | Removes the longest string containing only characters in characters (a space by default) from the end of string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `scale` | Scale of the argument (the number of decimal digits in the fractional part) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `schema_to_xml` | map schema contents to XML | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `schema_to_xmlschema` | map schema structure to XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `schema_to_xml_and_xmlschema` | map schema contents and structure to XML and XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `session_user` | Returns the session user's name | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `setseed` | Sets the seed for subsequent random() and random_normal() calls; argument must be between -1.0 and 1.0, inclusive | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `setval` | Sets the sequence object's current value, and optionally its is_called flag | 14 | [functions-sequence](https://www.postgresql.org/docs/18/functions-sequence.html) |
| `setweight` | Assigns the specified weight to each element of the vector | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `set_bit` | Sets n'th bit in binary string to newvalue | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `set_byte` | Sets n'th byte in binary string to newvalue | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `set_config` | Sets the parameter setting_name to new_value, and returns that value | 14 | [functions-admin](https://www.postgresql.org/docs/18/functions-admin.html) |
| `set_masklen` | Sets the netmask length for an inet value | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `sha224` | Computes the SHA-224 hash of the binary string | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `sha256` | Computes the SHA-256 hash of the binary string | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `sha384` | Computes the SHA-384 hash of the binary string | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `sha512` | Computes the SHA-512 hash of the binary string | 14 | [functions-binarystring](https://www.postgresql.org/docs/18/functions-binarystring.html) |
| `shobj_description` | Returns the comment for a shared database object specified by its OID and the name of the containing system catalog | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `sign` | Sign of the argument (-1, 0, or +1) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `sin` | Sine, argument in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `sind` | Sine, argument in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `sinh` | Hyperbolic sine | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `slope` | Computes slope of a line drawn through the two points | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `split_part` | Splits string at occurrences of delimiter and returns the n'th field (counting from one), or when n is negative, returns the \|n\|'th-from-last field | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `sqrt` | Square root | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `starts_with` | Returns true if string starts with prefix | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `statement_timestamp` | Current date and time (start of current statement) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `string_to_array` | Splits the string at occurrences of delimiter and forms the resulting fields into a text array | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `string_to_table` | Splits the string at occurrences of delimiter and returns the resulting fields as a set of text rows | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `strip` | Removes positions and weights from the tsvector | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `strpos` | Returns first starting index of the specified substring within string, or zero if it's not present | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `substr` | Extracts the substring of string starting at the start'th character, and extending for count characters if that is specified | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `substring` | Extracts the substring of string starting at the start'th character if that is specified, and stopping after count characters if that is specified | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `suppress_redundant_updates_trigger` | Suppresses do-nothing update operations | 14 | [functions-trigger](https://www.postgresql.org/docs/18/functions-trigger.html) |
| `system_user` | Returns the authentication method and the identity (if any) that the user presented during the authentication cycle before they were assigned a database role | 16 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `table_to_xml` | table_to_xml maps the content of the named table, passed as parameter table | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `table_to_xmlschema` | map table structure to XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `table_to_xml_and_xmlschema` | map table contents and structure to XML and XML Schema | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `tan` | Tangent, argument in radians | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `tand` | Tangent, argument in degrees | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `tanh` | Hyperbolic tangent | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `text` | Returns the unabbreviated IP address and netmask length as text | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `timeofday` | Current date and time (like clock_timestamp, but as a text string) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `to_ascii` | Converts string to ASCII from another encoding, which may be identified by name or number | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `to_bin` | Converts the number to its equivalent two's complement binary representation | 17 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `to_char` | Converts time stamp to string according to the given format | 14 | [functions-formatting](https://www.postgresql.org/docs/18/functions-formatting.html) |
| `to_date` | Converts string to date according to the given format | 14 | [functions-formatting](https://www.postgresql.org/docs/18/functions-formatting.html) |
| `to_hex` | Converts the number to its equivalent two's complement hexadecimal representation | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `to_json` | Converts any SQL value to json or jsonb | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `to_jsonb` | Converts any SQL value to json or jsonb | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `to_number` | Converts string to numeric according to the given format | 14 | [functions-formatting](https://www.postgresql.org/docs/18/functions-formatting.html) |
| `to_oct` | Converts the number to its equivalent two's complement octal representation | 17 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `to_regclass` | Translates a textual relation name to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regcollation` | Translates a textual collation name to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regnamespace` | Translates a textual schema name to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regoper` | Translates a textual operator name to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regoperator` | Translates a textual operator name (with parameter types) to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regproc` | Translates a textual function or procedure name to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regprocedure` | Translates a textual function or procedure name (with argument types) to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regrole` | Translates a textual role name to its OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regtype` | Parses a string of text, extracts a potential type name from it, and translates that name into a type OID | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_regtypemod` | Parses a string of text, extracts a potential type name from it, and translates its type modifier, if any | 17 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `to_timestamp` | Converts string to time stamp according to the given format | 14 | [functions-formatting](https://www.postgresql.org/docs/18/functions-formatting.html) |
| `to_tsquery` | Converts text to a tsquery, normalizing words according to the specified or default configuration | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `to_tsvector` | Converts text to a tsvector, normalizing words according to the specified or default configuration | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `transaction_timestamp` | Current date and time (start of current transaction) | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `translate` | Replaces each character in string that matches a character in the from set with the corresponding character in the to set | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `trim` | Removes the longest string containing only characters in characters (a space by default) from the start, end, or both ends (BOTH is the default) of string | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `trim_array` | Trims an array by removing the last n elements | 14 | [functions-array](https://www.postgresql.org/docs/18/functions-array.html) |
| `trim_scale` | Reduces the value's scale (number of fractional decimal digits) by removing trailing zeroes | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `trunc` | Truncates to integer (towards zero) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `tsquery_phrase` | Constructs a phrase query that searches for matches of query1 and query2 at successive lexemes (same as <-> operator) | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `tsvector_to_array` | Converts a tsvector to an array of lexemes | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `tsvector_update_trigger` | Automatically updates a tsvector column from associated plain-text document column(s) | 14 | [functions-trigger](https://www.postgresql.org/docs/18/functions-trigger.html) |
| `tsvector_update_trigger_column` | Automatically updates a tsvector column from associated plain-text document column(s) | 14 | [functions-trigger](https://www.postgresql.org/docs/18/functions-trigger.html) |
| `ts_debug` | Extracts and normalizes tokens from the document according to the specified or default text search configuration, and returns information about how each token was processed | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_delete` | Removes any occurrence of the given lexeme from the vector | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_filter` | Selects only elements with the given weights from the vector | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_headline` | Displays, in an abbreviated form, the match(es) for the query in the document, which must be raw text not a tsvector | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_lexize` | Returns an array of replacement lexemes if the input token is known to the dictionary, or an empty array if the token is known to the dictionary but it is a stop word, or NULL if it is not a known word | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_parse` | Extracts tokens from the document using the named parser | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_rank` | Computes a score showing how well the vector matches the query | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_rank_cd` | Computes a score showing how well the vector matches the query, using a cover density algorithm | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_rewrite` | Replaces occurrences of target with substitute within the query | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_stat` | Executes the sqlquery, which must return a single tsvector column, and returns statistics about each distinct lexeme contained in the data | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `ts_token_type` | Returns a table that describes each type of token the named parser can recognize | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `txid_current` | See pg_current_xact_id() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `txid_current_if_assigned` | See pg_current_xact_id_if_assigned() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `txid_current_snapshot` | See pg_current_snapshot() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `txid_snapshot_xip` | See pg_snapshot_xip() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `txid_snapshot_xmax` | See pg_snapshot_xmax() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `txid_snapshot_xmin` | See pg_snapshot_xmin() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `txid_status` | See pg_xact_status() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `txid_visible_in_snapshot` | See pg_visible_in_snapshot() | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `unicode_assigned` | Returns true if all characters in the string are assigned Unicode codepoints; false otherwise | 17 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `unicode_version` | Returns a string representing the version of Unicode used by PostgreSQL | 17 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `unistr` | Evaluate escaped Unicode characters in the argument | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `unnest` | Expands a tsvector into a set of rows, one per lexeme | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `upper` | Converts the string to all upper case, according to the rules of the database's locale | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `upper_inc` | Is the range's upper bound inclusive? | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `upper_inf` | Does the range have no upper bound? | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `user` | This is equivalent to current_user | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `uuidv4` | Generates a version 4 (random) UUID | 18 | [functions-uuid](https://www.postgresql.org/docs/18/functions-uuid.html) |
| `uuidv7` | Generates a version 7 (time-ordered) UUID | 18 | [functions-uuid](https://www.postgresql.org/docs/18/functions-uuid.html) |
| `uuid_extract_timestamp` | Extracts a timestamp with time zone from a UUID of version 1 or 7 | 17 | [functions-uuid](https://www.postgresql.org/docs/18/functions-uuid.html) |
| `uuid_extract_version` | Extracts the version from a UUID of one of the variants described by RFC 9562 | 17 | [functions-uuid](https://www.postgresql.org/docs/18/functions-uuid.html) |
| `version` | Returns a string describing the PostgreSQL server's version | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `websearch_to_tsquery` | Converts text to a tsquery, normalizing words according to the specified or default configuration | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `width` | Computes horizontal size of box | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `width_bucket` | Returns the number of the bucket in which operand falls in a histogram having count equal-width buckets spanning the range low to high | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `xmlcomment` | The function xmlcomment creates an XML value containing an XML comment with the specified text as content | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmlconcat` | The function xmlconcat concatenates a list of individual XML values to create a single value containing an XML content fragment | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmlelement` | The xmlelement expression produces an XML element with the given name, attributes, and content | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmlexists` | The function xmlexists evaluates an XPath 1.0 expression (the first argument), with the passed XML value as its context item | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmlforest` | The xmlforest expression produces an XML forest (sequence) of elements using the given names and content | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmlnamespaces` | The optional XMLNAMESPACES clause gives a comma-separated list of namespace definitions, where each namespace_uri is a text expression and each namespace_name is a simple identifier | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmlpi` | The xmlpi expression creates an XML processing instruction | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmlroot` | The xmlroot expression alters the properties of the root node of an XML value | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmltable` | The xmltable expression produces a table based on an XML value, an XPath filter to extract rows, and a set of column definitions | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xmltext` | The function xmltext returns an XML value with a single text node containing the input argument as its content | 17 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xml_is_well_formed` | xml_is_well_formed does the former if the configuration parameter is set to DOCUMENT, or the latter if it is set to CONTENT | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xml_is_well_formed_content` | xml_is_well_formed_document checks for a well-formed document, while xml_is_well_formed_content checks for well-formed content | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xml_is_well_formed_document` | xml_is_well_formed_document checks for a well-formed document, while xml_is_well_formed_content checks for well-formed content | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xpath` | The function xpath evaluates the XPath 1.0 expression xpath (given as text) against the XML value xml | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |
| `xpath_exists` | The function xpath_exists is a specialized form of the xpath function | 14 | [functions-xml](https://www.postgresql.org/docs/18/functions-xml.html) |

## Aggregate functions

Functions that combine many rows into one value.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `any_value` | Returns an arbitrary value from the non-null input values | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `array_agg` | Collects all the input values, including nulls, into an array | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `avg` | Computes the average (arithmetic mean) of all the non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `bit_and` | Computes the bitwise AND of all non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `bit_or` | Computes the bitwise OR of all non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `bit_xor` | Computes the bitwise exclusive OR of all non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `bool_and` | Returns true if all non-null input values are true, otherwise false | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `bool_or` | Returns true if any non-null input value is true, otherwise false | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `corr` | Computes the correlation coefficient | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `count` | Computes the number of input rows | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `covar_pop` | Computes the population covariance | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `covar_samp` | Computes the sample covariance | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `cume_dist` | Computes the cumulative distribution, that is (number of rows preceding or peers with hypothetical row) / (total rows) | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `dense_rank` | Computes the rank of the hypothetical row, without gaps; this function effectively counts peer groups | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `every` | This is the SQL standard's equivalent to bool_and | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `jsonb_agg` | Collects all the input values, including nulls, into a JSON array | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `jsonb_agg_strict` | Collects all the input values, skipping nulls, into a JSON array | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `jsonb_object_agg` | Collects all the key/value pairs into a JSON object | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `jsonb_object_agg_strict` | Collects all the key/value pairs into a JSON object | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `jsonb_object_agg_unique` | Collects all the key/value pairs into a JSON object | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `jsonb_object_agg_unique_strict` | Collects all the key/value pairs into a JSON object | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_agg` | Collects all the input values, including nulls, into a JSON array | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_agg_strict` | Collects all the input values, skipping nulls, into a JSON array | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_object_agg` | Collects all the key/value pairs into a JSON object | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_object_agg_strict` | Collects all the key/value pairs into a JSON object | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_object_agg_unique` | Collects all the key/value pairs into a JSON object | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `json_object_agg_unique_strict` | Collects all the key/value pairs into a JSON object | 16 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `max` | Computes the maximum of the non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `min` | Computes the minimum of the non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `mode` | Computes the mode, the most frequent value of the aggregated argument (arbitrarily choosing the first one if there are multiple equally-frequent values) | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `percentile_cont` | Computes the continuous percentile, a value corresponding to the specified fraction within the ordered set of aggregated argument values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `percentile_disc` | Computes the discrete percentile, the first value within the ordered set of aggregated argument values whose position in the ordering equals or exceeds the specified fraction | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `percent_rank` | Computes the relative rank of the hypothetical row, that is (rank - 1) / (total rows - 1) | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `range_agg` | Computes the union of the non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `range_intersect_agg` | Computes the intersection of the non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `rank` | Computes the rank of the hypothetical row, with gaps; that is, the row number of the first row in its peer group | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_avgx` | Computes the average of the independent variable, sum(X)/N | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_avgy` | Computes the average of the dependent variable, sum(Y)/N | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_count` | Computes the number of rows in which both inputs are non-null | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_intercept` | Computes the y-intercept of the least-squares-fit linear equation determined by the (X, Y) pairs | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_r2` | Computes the square of the correlation coefficient | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_slope` | Computes the slope of the least-squares-fit linear equation determined by the (X, Y) pairs | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_sxx` | Computes the sum of squares of the independent variable, sum(X^2) - sum(X)^2/N | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_sxy` | Computes the sum of products of independent times dependent variables, sum(X*Y) - sum(X) * sum(Y)/N | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `regr_syy` | Computes the sum of squares of the dependent variable, sum(Y^2) - sum(Y)^2/N | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `stddev` | This is a historical alias for stddev_samp | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `stddev_pop` | Computes the population standard deviation of the input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `stddev_samp` | Computes the sample standard deviation of the input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `string_agg` | Concatenates the non-null input values into a string | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `sum` | Computes the sum of the non-null input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `variance` | This is a historical alias for var_samp | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `var_pop` | Computes the population variance of the input values (square of the population standard deviation) | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `var_samp` | Computes the sample variance of the input values (square of the sample standard deviation) | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |
| `xmlagg` | Concatenates the non-null XML input values | 14 | [functions-aggregate](https://www.postgresql.org/docs/18/functions-aggregate.html) |

## Window functions

Functions that are used with OVER.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `first_value` | Returns value evaluated at the row that is the first row of the window frame | 14 | [functions-window](https://www.postgresql.org/docs/18/functions-window.html) |
| `lag` | Returns value evaluated at the row that is offset rows before the current row within the partition; if there is no such row, instead returns default (which must be of a type compatible with value) | 14 | [functions-window](https://www.postgresql.org/docs/18/functions-window.html) |
| `last_value` | Returns value evaluated at the row that is the last row of the window frame | 14 | [functions-window](https://www.postgresql.org/docs/18/functions-window.html) |
| `lead` | Returns value evaluated at the row that is offset rows after the current row within the partition; if there is no such row, instead returns default (which must be of a type compatible with value) | 14 | [functions-window](https://www.postgresql.org/docs/18/functions-window.html) |
| `nth_value` | Returns value evaluated at the row that is the n'th row of the window frame (counting from 1); returns NULL if there is no such row | 14 | [functions-window](https://www.postgresql.org/docs/18/functions-window.html) |
| `ntile` | Returns an integer ranging from 1 to the argument value, dividing the partition as equally as possible | 14 | [functions-window](https://www.postgresql.org/docs/18/functions-window.html) |
| `row_number` | Returns the number of the current row within its partition, counting from 1 | 14 | [functions-window](https://www.postgresql.org/docs/18/functions-window.html) |

## Operators

Operators the manual documents. Overloads are merged into one entry.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `!` | Boolean NOT | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `!!` | Negates a tsquery, producing a query that matches documents that do not match the input query | 14 | [functions-textsearch](https://www.postgresql.org/docs/18/functions-textsearch.html) |
| `!=` | Non-equality comparison | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `!~` | String does not match regular expression, case sensitively | 14 | [functions-matching](https://www.postgresql.org/docs/18/functions-matching.html) |
| `!~*` | String does not match regular expression, case-insensitively | 14 | [functions-matching](https://www.postgresql.org/docs/18/functions-matching.html) |
| `#` | Bitwise exclusive OR | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `##` | Computes the closest point to the first object on the second object | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `#-` | Deletes the field or array element at the specified path, where path elements can be either field keys or array indexes | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `#>` | Extracts JSON sub-object at the specified path, where path elements can be either field keys or array indexes | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `#>>` | Extracts JSON sub-object at the specified path as text | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `%` | Modulo (remainder); available for smallint, integer, bigint, and numeric | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `&` | Bitwise AND | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `&&` | Do these objects overlap? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `&<` | Does first object not extend to the right of second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `&<\|` | Does first object not extend above second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `&>` | Does first object not extend to the left of second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `*` | Multiplication | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `+` | Addition | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `-` | Subtraction | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `->` | Extracts n'th element of JSON array (array elements are indexed from zero, but negative integers count from the end) | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `->>` | Extracts n'th element of JSON array, as text | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `-\|-` | Are the ranges adjacent? | 14 | [functions-range](https://www.postgresql.org/docs/18/functions-range.html) |
| `.` | Type of the JSON item (see json_typeof) | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `/` | Division (for integral types, division truncates the result towards zero) | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `<` | Less-than comparison | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `<->` | Computes the distance between the objects | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `<<` | Bitwise shift left | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `<<=` | Is subnet contained by or equal to subnet? | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `<<\|` | Is first object strictly below second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `<=` | Less-than-or-equal-to comparison | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `<>` | Non-equality comparison | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `<@` | Is first object contained in or on second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `<^` | Is first object below second (allows edges to touch)? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `=` | Are aclitems equal? | 14 | [functions-info](https://www.postgresql.org/docs/18/functions-info.html) |
| `==` | Equality comparison (this, and the other comparison operators, work on all JSON scalar values) | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `>` | Greater-than comparison | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `>=` | Greater-than-or-equal-to comparison | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `>>` | Bitwise shift right | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `>>=` | Does subnet contain or equal subnet? | 14 | [functions-net](https://www.postgresql.org/docs/18/functions-net.html) |
| `>^` | Is first object above second (allows edges to touch)? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `?` | Does the text string exist as a top-level key or array element within the JSON value? | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `?#` | Do these objects intersect? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `?&` | Do all of the strings in the text array exist as top-level keys or array elements? | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `?-` | Is line horizontal? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `?-\|` | Are lines perpendicular? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `?\|` | Is line vertical? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `?\|\|` | Are lines parallel? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `@` | Absolute value | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `@-@` | Computes the total length | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `@>` | Does first object contain second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `@?` | Does JSON path return any item for the specified JSON value? | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `@@` | Computes the center point | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `AT LOCAL` | Converts given time stamp without time zone to time stamp with the session's TimeZone value as time zone | 17 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `AT TIME ZONE` | Converts given time stamp without time zone to time stamp with time zone, assuming the given value is in the named time zone | 14 | [functions-datetime](https://www.postgresql.org/docs/18/functions-datetime.html) |
| `BETWEEN` | Between (inclusive of the range endpoints) | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `BETWEEN SYMMETRIC` | Between, after sorting the two endpoint values | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `exists` | Tests whether a path expression matches at least one SQL/JSON item | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `false` | JSON constant false | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `IS` | Checks whether the string is in the specified Unicode normalization form | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `IS DISTINCT FROM` | Not equal, treating null as a comparable value | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS FALSE` | Test whether boolean expression yields false | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS NOT DISTINCT FROM` | Equal, treating null as a comparable value | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS NOT FALSE` | Test whether boolean expression yields true or unknown | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS NOT NULL` | Test whether value is not null | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS NOT TRUE` | Test whether boolean expression yields false or unknown | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS NOT UNKNOWN` | Test whether boolean expression yields true or false | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS NULL` | Test whether value is null | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS TRUE` | Test whether boolean expression yields true | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `IS UNKNOWN` | Test whether boolean expression yields unknown | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `ISNULL` | Test whether value is null (nonstandard syntax) | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `like_regex` | Tests whether the first operand matches the regular expression given by the second operand, optionally with modifications described by a string of flag characters | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `NOT BETWEEN` | Not between (the negation of BETWEEN) | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `NOT BETWEEN SYMMETRIC` | Not between, after sorting the two endpoint values | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `NOTNULL` | Test whether value is not null (nonstandard syntax) | 14 | [functions-comparison](https://www.postgresql.org/docs/18/functions-comparison.html) |
| `null` | JSON constant null (note that, unlike in SQL, comparison to null works normally) | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `starts with` | Tests whether the second operand is an initial substring of the first operand | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `true` | JSON constant true | 14 | [functions-json](https://www.postgresql.org/docs/18/functions-json.html) |
| `^` | Exponentiation | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `^@` | Returns true if the first string starts with the second string (equivalent to the starts_with() function) | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `\|` | Bitwise OR | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `\|&>` | Does first object not extend below second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `\|/` | Square root | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `\|>>` | Is first object strictly above second? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |
| `\|\|` | Concatenates the two strings | 14 | [functions-string](https://www.postgresql.org/docs/18/functions-string.html) |
| `\|\|/` | Cube root | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `~` | Bitwise NOT | 14 | [functions-math](https://www.postgresql.org/docs/18/functions-math.html) |
| `~*` | String matches regular expression, case-insensitively | 14 | [functions-matching](https://www.postgresql.org/docs/18/functions-matching.html) |
| `~=` | Are these objects the same? | 14 | [functions-geometry](https://www.postgresql.org/docs/18/functions-geometry.html) |

## SQLSTATE codes

Error codes reported by the server.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `00000 (successful_completion)` | Successful completion | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `01000 (warning)` | Warning | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `01003 (null_value_eliminated_in_set_function)` | Null value eliminated in set function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `01004 (string_data_right_truncation)` | String data right truncation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `01006 (privilege_not_revoked)` | Privilege not revoked | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `01007 (privilege_not_granted)` | Privilege not granted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `01008 (implicit_zero_bit_padding)` | Implicit zero bit padding | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0100C (dynamic_result_sets_returned)` | Dynamic result sets returned | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `01P01 (deprecated_feature)` | Deprecated feature | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `02000 (no_data)` | No data | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `02001 (no_additional_dynamic_result_sets_returned)` | No additional dynamic result sets returned | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `03000 (sql_statement_not_yet_complete)` | Sql statement not yet complete | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `08000 (connection_exception)` | Connection exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `08001 (sqlclient_unable_to_establish_sqlconnection)` | Sqlclient unable to establish sqlconnection | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `08003 (connection_does_not_exist)` | Connection does not exist | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `08004 (sqlserver_rejected_establishment_of_sqlconnection)` | Sqlserver rejected establishment of sqlconnection | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `08006 (connection_failure)` | Connection failure | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `08007 (transaction_resolution_unknown)` | Transaction resolution unknown | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `08P01 (protocol_violation)` | Protocol violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `09000 (triggered_action_exception)` | Triggered action exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0A000 (feature_not_supported)` | Feature not supported | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0B000 (invalid_transaction_initiation)` | Invalid transaction initiation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0F000 (locator_exception)` | Locator exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0F001 (invalid_locator_specification)` | Invalid locator specification | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0L000 (invalid_grantor)` | Invalid grantor | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0LP01 (invalid_grant_operation)` | Invalid grant operation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0P000 (invalid_role_specification)` | Invalid role specification | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0Z000 (diagnostics_exception)` | Diagnostics exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `0Z002 (stacked_diagnostics_accessed_without_active_handler)` | Stacked diagnostics accessed without active handler | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `10608 (invalid_argument_for_xquery)` | Invalid argument for xquery | 18 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `20000 (case_not_found)` | Case not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `21000 (cardinality_violation)` | Cardinality violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22000 (data_exception)` | Data exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22001 (string_data_right_truncation)` | String data right truncation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22002 (null_value_no_indicator_parameter)` | Null value no indicator parameter | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22003 (numeric_value_out_of_range)` | Numeric value out of range | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22004 (null_value_not_allowed)` | Null value not allowed | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22005 (error_in_assignment)` | Error in assignment | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22007 (invalid_datetime_format)` | Invalid datetime format | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22008 (datetime_field_overflow)` | Datetime field overflow | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22009 (invalid_time_zone_displacement_value)` | Invalid time zone displacement value | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200B (escape_character_conflict)` | Escape character conflict | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200C (invalid_use_of_escape_character)` | Invalid use of escape character | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200D (invalid_escape_octet)` | Invalid escape octet | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200F (zero_length_character_string)` | Zero length character string | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200G (most_specific_type_mismatch)` | Most specific type mismatch | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200H (sequence_generator_limit_exceeded)` | Sequence generator limit exceeded | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200L (not_an_xml_document)` | Not an xml document | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200M (invalid_xml_document)` | Invalid xml document | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200N (invalid_xml_content)` | Invalid xml content | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200S (invalid_xml_comment)` | Invalid xml comment | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2200T (invalid_xml_processing_instruction)` | Invalid xml processing instruction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22010 (invalid_indicator_parameter_value)` | Invalid indicator parameter value | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22011 (substring_error)` | Substring error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22012 (division_by_zero)` | Division by zero | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22013 (invalid_preceding_or_following_size)` | Invalid preceding or following size | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22014 (invalid_argument_for_ntile_function)` | Invalid argument for ntile function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22015 (interval_field_overflow)` | Interval field overflow | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22016 (invalid_argument_for_nth_value_function)` | Invalid argument for nth value function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22018 (invalid_character_value_for_cast)` | Invalid character value for cast | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22019 (invalid_escape_character)` | Invalid escape character | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2201B (invalid_regular_expression)` | Invalid regular expression | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2201E (invalid_argument_for_logarithm)` | Invalid argument for logarithm | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2201F (invalid_argument_for_power_function)` | Invalid argument for power function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2201G (invalid_argument_for_width_bucket_function)` | Invalid argument for width bucket function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2201W (invalid_row_count_in_limit_clause)` | Invalid row count in limit clause | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2201X (invalid_row_count_in_result_offset_clause)` | Invalid row count in result offset clause | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22021 (character_not_in_repertoire)` | Character not in repertoire | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22022 (indicator_overflow)` | Indicator overflow | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22023 (invalid_parameter_value)` | Invalid parameter value | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22024 (unterminated_c_string)` | Unterminated c string | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22025 (invalid_escape_sequence)` | Invalid escape sequence | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22026 (string_data_length_mismatch)` | String data length mismatch | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22027 (trim_error)` | Trim error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2202E (array_element_error)` | Array element error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2202G (invalid_tablesample_repeat)` | Invalid tablesample repeat | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2202H (invalid_tablesample_argument)` | Invalid tablesample argument | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22030 (duplicate_json_object_key_value)` | Duplicate json object key value | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22031 (invalid_argument_for_sql_json_datetime_function)` | Invalid argument for sql json datetime function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22032 (invalid_json_text)` | Invalid json text | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22033 (invalid_sql_json_subscript)` | Invalid sql json subscript | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22034 (more_than_one_sql_json_item)` | More than one sql json item | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22035 (no_sql_json_item)` | No sql json item | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22036 (non_numeric_sql_json_item)` | Non numeric sql json item | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22037 (non_unique_keys_in_a_json_object)` | Non unique keys in a json object | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22038 (singleton_sql_json_item_required)` | Singleton sql json item required | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22039 (sql_json_array_not_found)` | Sql json array not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2203A (sql_json_member_not_found)` | Sql json member not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2203B (sql_json_number_not_found)` | Sql json number not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2203C (sql_json_object_not_found)` | Sql json object not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2203D (too_many_json_array_elements)` | Too many json array elements | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2203E (too_many_json_object_members)` | Too many json object members | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2203F (sql_json_scalar_required)` | Sql json scalar required | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2203G (sql_json_item_cannot_be_cast_to_target_type)` | Sql json item cannot be cast to target type | 15 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22P01 (floating_point_exception)` | Floating point exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22P02 (invalid_text_representation)` | Invalid text representation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22P03 (invalid_binary_representation)` | Invalid binary representation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22P04 (bad_copy_file_format)` | Bad copy file format | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22P05 (untranslatable_character)` | Untranslatable character | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `22P06 (nonstandard_use_of_escape_character)` | Nonstandard use of escape character | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `23000 (integrity_constraint_violation)` | Integrity constraint violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `23001 (restrict_violation)` | Restrict violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `23502 (not_null_violation)` | Not null violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `23503 (foreign_key_violation)` | Foreign key violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `23505 (unique_violation)` | Unique violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `23514 (check_violation)` | Check violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `23P01 (exclusion_violation)` | Exclusion violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `24000 (invalid_cursor_state)` | Invalid cursor state | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25000 (invalid_transaction_state)` | Invalid transaction state | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25001 (active_sql_transaction)` | Active sql transaction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25002 (branch_transaction_already_active)` | Branch transaction already active | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25003 (inappropriate_access_mode_for_branch_transaction)` | Inappropriate access mode for branch transaction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25004 (inappropriate_isolation_level_for_branch_transaction)` | Inappropriate isolation level for branch transaction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25005 (no_active_sql_transaction_for_branch_transaction)` | No active sql transaction for branch transaction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25006 (read_only_sql_transaction)` | Read only sql transaction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25007 (schema_and_data_statement_mixing_not_supported)` | Schema and data statement mixing not supported | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25008 (held_cursor_requires_same_isolation_level)` | Held cursor requires same isolation level | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25P01 (no_active_sql_transaction)` | No active sql transaction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25P02 (in_failed_sql_transaction)` | In failed sql transaction | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25P03 (idle_in_transaction_session_timeout)` | Idle in transaction session timeout | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `25P04 (transaction_timeout)` | Transaction timeout | 17 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `26000 (invalid_sql_statement_name)` | Invalid sql statement name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `27000 (triggered_data_change_violation)` | Triggered data change violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `28000 (invalid_authorization_specification)` | Invalid authorization specification | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `28P01 (invalid_password)` | Invalid password | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2B000 (dependent_privilege_descriptors_still_exist)` | Dependent privilege descriptors still exist | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2BP01 (dependent_objects_still_exist)` | Dependent objects still exist | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2D000 (invalid_transaction_termination)` | Invalid transaction termination | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2F000 (sql_routine_exception)` | Sql routine exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2F002 (modifying_sql_data_not_permitted)` | Modifying sql data not permitted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2F003 (prohibited_sql_statement_attempted)` | Prohibited sql statement attempted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2F004 (reading_sql_data_not_permitted)` | Reading sql data not permitted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `2F005 (function_executed_no_return_statement)` | Function executed no return statement | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `34000 (invalid_cursor_name)` | Invalid cursor name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `38000 (external_routine_exception)` | External routine exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `38001 (containing_sql_not_permitted)` | Containing sql not permitted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `38002 (modifying_sql_data_not_permitted)` | Modifying sql data not permitted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `38003 (prohibited_sql_statement_attempted)` | Prohibited sql statement attempted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `38004 (reading_sql_data_not_permitted)` | Reading sql data not permitted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `39000 (external_routine_invocation_exception)` | External routine invocation exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `39001 (invalid_sqlstate_returned)` | Invalid sqlstate returned | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `39004 (null_value_not_allowed)` | Null value not allowed | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `39P01 (trigger_protocol_violated)` | Trigger protocol violated | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `39P02 (srf_protocol_violated)` | Srf protocol violated | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `39P03 (event_trigger_protocol_violated)` | Event trigger protocol violated | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `3B000 (savepoint_exception)` | Savepoint exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `3B001 (invalid_savepoint_specification)` | Invalid savepoint specification | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `3D000 (invalid_catalog_name)` | Invalid catalog name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `3F000 (invalid_schema_name)` | Invalid schema name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `40000 (transaction_rollback)` | Transaction rollback | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `40001 (serialization_failure)` | Serialization failure | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `40002 (transaction_integrity_constraint_violation)` | Transaction integrity constraint violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `40003 (statement_completion_unknown)` | Statement completion unknown | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `40P01 (deadlock_detected)` | Deadlock detected | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42000 (syntax_error_or_access_rule_violation)` | Syntax error or access rule violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42501 (insufficient_privilege)` | Insufficient privilege | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42601 (syntax_error)` | Syntax error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42602 (invalid_name)` | Invalid name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42611 (invalid_column_definition)` | Invalid column definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42622 (name_too_long)` | Name too long | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42701 (duplicate_column)` | Duplicate column | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42702 (ambiguous_column)` | Ambiguous column | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42703 (undefined_column)` | Undefined column | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42704 (undefined_object)` | Undefined object | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42710 (duplicate_object)` | Duplicate object | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42712 (duplicate_alias)` | Duplicate alias | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42723 (duplicate_function)` | Duplicate function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42725 (ambiguous_function)` | Ambiguous function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42803 (grouping_error)` | Grouping error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42804 (datatype_mismatch)` | Datatype mismatch | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42809 (wrong_object_type)` | Wrong object type | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42830 (invalid_foreign_key)` | Invalid foreign key | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42846 (cannot_coerce)` | Cannot coerce | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42883 (undefined_function)` | Undefined function | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `428C9 (generated_always)` | Generated always | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42939 (reserved_name)` | Reserved name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P01 (undefined_table)` | Undefined table | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P02 (undefined_parameter)` | Undefined parameter | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P03 (duplicate_cursor)` | Duplicate cursor | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P04 (duplicate_database)` | Duplicate database | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P05 (duplicate_prepared_statement)` | Duplicate prepared statement | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P06 (duplicate_schema)` | Duplicate schema | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P07 (duplicate_table)` | Duplicate table | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P08 (ambiguous_parameter)` | Ambiguous parameter | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P09 (ambiguous_alias)` | Ambiguous alias | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P10 (invalid_column_reference)` | Invalid column reference | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P11 (invalid_cursor_definition)` | Invalid cursor definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P12 (invalid_database_definition)` | Invalid database definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P13 (invalid_function_definition)` | Invalid function definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P14 (invalid_prepared_statement_definition)` | Invalid prepared statement definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P15 (invalid_schema_definition)` | Invalid schema definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P16 (invalid_table_definition)` | Invalid table definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P17 (invalid_object_definition)` | Invalid object definition | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P18 (indeterminate_datatype)` | Indeterminate datatype | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P19 (invalid_recursion)` | Invalid recursion | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P20 (windowing_error)` | Windowing error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P21 (collation_mismatch)` | Collation mismatch | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `42P22 (indeterminate_collation)` | Indeterminate collation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `44000 (with_check_option_violation)` | With check option violation | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `53000 (insufficient_resources)` | Insufficient resources | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `53100 (disk_full)` | Disk full | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `53200 (out_of_memory)` | Out of memory | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `53300 (too_many_connections)` | Too many connections | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `53400 (configuration_limit_exceeded)` | Configuration limit exceeded | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `54000 (program_limit_exceeded)` | Program limit exceeded | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `54001 (statement_too_complex)` | Statement too complex | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `54011 (too_many_columns)` | Too many columns | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `54023 (too_many_arguments)` | Too many arguments | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `55000 (object_not_in_prerequisite_state)` | Object not in prerequisite state | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `55006 (object_in_use)` | Object in use | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `55P02 (cant_change_runtime_param)` | Cant change runtime param | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `55P03 (lock_not_available)` | Lock not available | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `55P04 (unsafe_new_enum_value_usage)` | Unsafe new enum value usage | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `57000 (operator_intervention)` | Operator intervention | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `57014 (query_canceled)` | Query canceled | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `57P01 (admin_shutdown)` | Admin shutdown | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `57P02 (crash_shutdown)` | Crash shutdown | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `57P03 (cannot_connect_now)` | Cannot connect now | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `57P04 (database_dropped)` | Database dropped | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `57P05 (idle_session_timeout)` | Idle session timeout | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `58000 (system_error)` | System error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `58030 (io_error)` | Io error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `58P01 (undefined_file)` | Undefined file | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `58P02 (duplicate_file)` | Duplicate file | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `58P03 (file_name_too_long)` | File name too long | 18 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `F0000 (config_file_error)` | Config file error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `F0001 (lock_file_exists)` | Lock file exists | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV000 (fdw_error)` | Fdw error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV001 (fdw_out_of_memory)` | Fdw out of memory | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV002 (fdw_dynamic_parameter_value_needed)` | Fdw dynamic parameter value needed | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV004 (fdw_invalid_data_type)` | Fdw invalid data type | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV005 (fdw_column_name_not_found)` | Fdw column name not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV006 (fdw_invalid_data_type_descriptors)` | Fdw invalid data type descriptors | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV007 (fdw_invalid_column_name)` | Fdw invalid column name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV008 (fdw_invalid_column_number)` | Fdw invalid column number | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV009 (fdw_invalid_use_of_null_pointer)` | Fdw invalid use of null pointer | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00A (fdw_invalid_string_format)` | Fdw invalid string format | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00B (fdw_invalid_handle)` | Fdw invalid handle | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00C (fdw_invalid_option_index)` | Fdw invalid option index | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00D (fdw_invalid_option_name)` | Fdw invalid option name | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00J (fdw_option_name_not_found)` | Fdw option name not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00K (fdw_reply_handle)` | Fdw reply handle | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00L (fdw_unable_to_create_execution)` | Fdw unable to create execution | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00M (fdw_unable_to_create_reply)` | Fdw unable to create reply | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00N (fdw_unable_to_establish_connection)` | Fdw unable to establish connection | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00P (fdw_no_schemas)` | Fdw no schemas | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00Q (fdw_schema_not_found)` | Fdw schema not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV00R (fdw_table_not_found)` | Fdw table not found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV010 (fdw_function_sequence_error)` | Fdw function sequence error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV014 (fdw_too_many_handles)` | Fdw too many handles | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV021 (fdw_inconsistent_descriptor_information)` | Fdw inconsistent descriptor information | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV024 (fdw_invalid_attribute_value)` | Fdw invalid attribute value | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV090 (fdw_invalid_string_length_or_buffer_length)` | Fdw invalid string length or buffer length | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `HV091 (fdw_invalid_descriptor_field_identifier)` | Fdw invalid descriptor field identifier | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `P0000 (plpgsql_error)` | Plpgsql error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `P0001 (raise_exception)` | Raise exception | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `P0002 (no_data_found)` | No data found | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `P0003 (too_many_rows)` | Too many rows | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `P0004 (assert_failure)` | Assert failure | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `XX000 (internal_error)` | Internal error | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `XX001 (data_corrupted)` | Data corrupted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| `XX002 (index_corrupted)` | Index corrupted | 14 | [errcodes-appendix](https://www.postgresql.org/docs/18/errcodes-appendix.html) |

## Lexical rules

The shapes of names, constants, comments and other tokens.

| Name | Summary | Since | Manual |
| --- | --- | ---: | --- |
| `"identifier"` | A name in double quotes keeps its case and may contain any character; a double quote inside is written twice. | 14 | [sql-syntax-identifiers](https://www.postgresql.org/docs/18/sql-syntax-identifiers.html) |
| `$$` | A string constant delimited by $$ or $tag$ that contains quotes and backslashes unchanged; function bodies are usually written this way. | 14 | [sql-syntax-dollar-quoting](https://www.postgresql.org/docs/18/sql-syntax-dollar-quoting.html) |
| `$n` | $1, $2, ... refer to the arguments of a function body or of a prepared statement. | 14 | [sql-expressions-parameters-positional](https://www.postgresql.org/docs/18/sql-expressions-parameters-positional.html) |
| `'string'` | A string constant is in single quotes; a single quote inside is written twice. Two constants separated by a newline are joined. | 14 | [sql-syntax-strings](https://www.postgresql.org/docs/18/sql-syntax-strings.html) |
| `--` | -- starts a comment that runs to the end of the line; /* ... */ is a comment that may span lines and may be nested. | 14 | [sql-syntax-comments](https://www.postgresql.org/docs/18/sql-syntax-comments.html) |
| `::` | expression::type converts a value to a type; CAST(expression AS type) is the standard form of the same thing. | 14 | [sql-syntax-type-casts](https://www.postgresql.org/docs/18/sql-syntax-type-casts.html) |
| `=>` | name => value passes a function argument by name; the older := is also accepted. | 14 | [sql-syntax-calling-funcs](https://www.postgresql.org/docs/18/sql-syntax-calling-funcs.html) |
| `ARRAY[]` | ARRAY[1, 2, 3] builds an array from expressions; ARRAY(subquery) builds one from a query's rows. | 14 | [sql-syntax-array-constructors](https://www.postgresql.org/docs/18/sql-syntax-array-constructors.html) |
| `B''` | B'1001' is a bit string of binary digits; X'1FF' is a bit string of hexadecimal digits. | 14 | [sql-syntax-bit-strings](https://www.postgresql.org/docs/18/sql-syntax-bit-strings.html) |
| `COLLATE` | expression COLLATE collation overrides the collation used to compare or sort a character value. | 14 | [sql-syntax-collate-exprs](https://www.postgresql.org/docs/18/sql-syntax-collate-exprs.html) |
| `E''` | A string constant prefixed with E in which a backslash starts an escape such as \n, \t, \xhh or \uxxxx. | 14 | [sql-syntax-strings-escape](https://www.postgresql.org/docs/18/sql-syntax-strings-escape.html) |
| `field selection` | (row_expression).field_name selects a field of a composite value; the parentheses are required unless the expression is a table reference. | 14 | [field-selection](https://www.postgresql.org/docs/18/field-selection.html) |
| `identifier` | An unquoted name is folded to lower case; it starts with a letter or underscore and continues with letters, digits, underscores and dollar signs. | 14 | [sql-syntax-identifiers](https://www.postgresql.org/docs/18/sql-syntax-identifiers.html) |
| `numeric constant` | Digits with an optional decimal point and exponent; 0x, 0o and 0b prefixes and underscores between digits are accepted (PostgreSQL 16 and later). | 14 | [sql-syntax-constants-numeric](https://www.postgresql.org/docs/18/sql-syntax-constants-numeric.html) |
| `operator` | A name of up to 63 characters from + - * / < > = ~ ! @ # % ^ & \| ` ?, with rules about which may end it. | 14 | [sql-syntax-operators](https://www.postgresql.org/docs/18/sql-syntax-operators.html) |
| `operator precedence` | The order in which operators bind, from the highest (. and ::) to the lowest (OR). | 14 | [sql-precedence](https://www.postgresql.org/docs/18/sql-precedence.html) |
| `ROW()` | ROW(a, b) or (a, b) builds a composite value from fields. | 14 | [sql-syntax-row-constructors](https://www.postgresql.org/docs/18/sql-syntax-row-constructors.html) |
| `special character` | Characters that are not operators: $ for parameters, parentheses for grouping and calls, brackets for arrays, commas, semicolons, colons and the dot. | 14 | [sql-syntax-special-chars](https://www.postgresql.org/docs/18/sql-syntax-special-chars.html) |
| `type 'string'` | A constant of any type is written as the type name followed by a string, for example date '2020-01-31'; it is the same as a cast. | 14 | [sql-syntax-constants-generic](https://www.postgresql.org/docs/18/sql-syntax-constants-generic.html) |
| `U&''` | A string constant prefixed with U& in which \xxxx or \+xxxxxx writes a Unicode character; UESCAPE changes the escape character. | 14 | [sql-syntax-strings-uescape](https://www.postgresql.org/docs/18/sql-syntax-strings-uescape.html) |
| `[]` | array[index] picks one element and array[lower:upper] a slice; arrays are 1-based unless declared otherwise. | 14 | [sql-expressions-subscripts](https://www.postgresql.org/docs/18/sql-expressions-subscripts.html) |
