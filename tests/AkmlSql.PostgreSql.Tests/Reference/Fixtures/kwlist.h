/*-------------------------------------------------------------------------
 *
 * kwlist.h
 *
 * The keyword lists are kept in their own source files for use by
 * automatic tools.  The exact representation of a keyword is determined
 * by the PG_KEYWORD macro, which is not defined in this file.
 *
 *-------------------------------------------------------------------------
 */

/* name, value, category, is-bare-label */
PG_KEYWORD("abort", ABORT_P, UNRESERVED_KEYWORD, BARE_LABEL)
PG_KEYWORD("authorization", AUTHORIZATION, TYPE_FUNC_NAME_KEYWORD, BARE_LABEL)
PG_KEYWORD("between", BETWEEN, COL_NAME_KEYWORD, BARE_LABEL)
PG_KEYWORD("lateral", LATERAL_P, RESERVED_KEYWORD, BARE_LABEL)
PG_KEYWORD("merge", MERGE, UNRESERVED_KEYWORD, BARE_LABEL)
PG_KEYWORD("select", SELECT, RESERVED_KEYWORD, AS_LABEL)
