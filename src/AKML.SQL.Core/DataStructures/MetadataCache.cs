using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.DataStructures
{
    /// <summary>
    /// High-performance metadata cache using Trie data structure for fast prefix lookups.
    /// Implements Story 4.2: Database Schema Harvesting & Caching.
    /// </summary>
    public class MetadataCache
    {
        private readonly ILogger<MetadataCache> _logger;
        private readonly ConcurrentDictionary<string, DatabaseCache> _databaseCaches;
        private readonly SemaphoreSlim _loadLock;
        private readonly TimeSpan _cacheExpiration;

        public MetadataCache(ILogger<MetadataCache> logger)
        {
            _logger = logger;
            _databaseCaches = new ConcurrentDictionary<string, DatabaseCache>(StringComparer.OrdinalIgnoreCase);
            _loadLock = new SemaphoreSlim(1, 1);
            _cacheExpiration = TimeSpan.FromMilliseconds(AkmlConstants.Timeouts.MetadataCache);
        }

        /// <summary>
        /// Gets or creates a cache for a specific database connection.
        /// </summary>
        /// <param name="connectionKey">Unique key for the connection (server/database).</param>
        /// <returns>The database cache.</returns>
        public DatabaseCache GetOrCreateCache(string connectionKey)
        {
            return _databaseCaches.GetOrAdd(connectionKey, key =>
            {
                _logger.LogDebug("Creating new metadata cache for: {ConnectionKey}", key);
                return new DatabaseCache(key, _cacheExpiration);
            });
        }

        /// <summary>
        /// Checks if a cache exists and is valid for the connection.
        /// </summary>
        public bool HasValidCache(string connectionKey)
        {
            return _databaseCaches.TryGetValue(connectionKey, out var cache) && !cache.IsExpired;
        }

        /// <summary>
        /// Clears cache for a specific connection.
        /// </summary>
        public void ClearCache(string connectionKey)
        {
            if (_databaseCaches.TryRemove(connectionKey, out var cache))
            {
                cache.Clear();
                _logger.LogInformation("Cleared metadata cache for: {ConnectionKey}", connectionKey);
            }
        }

        /// <summary>
        /// Clears all caches.
        /// </summary>
        public void ClearAllCaches()
        {
            foreach (var cache in _databaseCaches.Values)
            {
                cache.Clear();
            }
            _databaseCaches.Clear();
            _logger.LogInformation("Cleared all metadata caches");
        }

        /// <summary>
        /// Gets statistics about the cache.
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            var stats = new CacheStatistics
            {
                TotalDatabases = _databaseCaches.Count
            };

            foreach (var cache in _databaseCaches.Values)
            {
                var dbStats = cache.GetStatistics();
                stats.TotalTables += dbStats.TableCount;
                stats.TotalColumns += dbStats.ColumnCount;
                stats.TotalProcedures += dbStats.ProcedureCount;
                stats.TotalFunctions += dbStats.FunctionCount;
            }

            return stats;
        }
    }

    /// <summary>
    /// Cache for a single database's metadata with Trie-based lookups.
    /// </summary>
    public class DatabaseCache
    {
        private readonly string _connectionKey;
        private readonly TimeSpan _expiration;
        private DateTime _loadedAt;

        // Trie structures for fast prefix lookups
        private readonly Trie<SchemaObjectInfo> _tablesTrie;
        private readonly Trie<SchemaObjectInfo> _viewsTrie;
        private readonly Trie<ColumnInfo> _columnsTrie;
        private readonly Trie<ProcedureInfo> _proceduresTrie;
        private readonly Trie<FunctionInfo> _functionsTrie;
        private readonly Trie<SchemaInfo> _schemasTrie;

        // Full collections for enumeration
        private readonly ConcurrentDictionary<string, SchemaObjectInfo> _tables;
        private readonly ConcurrentDictionary<string, SchemaObjectInfo> _views;
        private readonly ConcurrentDictionary<string, List<ColumnInfo>> _tableColumns;
        private readonly ConcurrentDictionary<string, ProcedureInfo> _procedures;
        private readonly ConcurrentDictionary<string, FunctionInfo> _functions;
        private readonly ConcurrentDictionary<string, SchemaInfo> _schemas;

        public DatabaseCache(string connectionKey, TimeSpan expiration)
        {
            _connectionKey = connectionKey;
            _expiration = expiration;
            _loadedAt = DateTime.MinValue;

            // Initialize tries (case-insensitive for SQL objects)
            _tablesTrie = new Trie<SchemaObjectInfo>(caseSensitive: false);
            _viewsTrie = new Trie<SchemaObjectInfo>(caseSensitive: false);
            _columnsTrie = new Trie<ColumnInfo>(caseSensitive: false);
            _proceduresTrie = new Trie<ProcedureInfo>(caseSensitive: false);
            _functionsTrie = new Trie<FunctionInfo>(caseSensitive: false);
            _schemasTrie = new Trie<SchemaInfo>(caseSensitive: false);

            // Initialize dictionaries
            _tables = new ConcurrentDictionary<string, SchemaObjectInfo>(StringComparer.OrdinalIgnoreCase);
            _views = new ConcurrentDictionary<string, SchemaObjectInfo>(StringComparer.OrdinalIgnoreCase);
            _tableColumns = new ConcurrentDictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            _procedures = new ConcurrentDictionary<string, ProcedureInfo>(StringComparer.OrdinalIgnoreCase);
            _functions = new ConcurrentDictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);
            _schemas = new ConcurrentDictionary<string, SchemaInfo>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets whether the cache has expired.
        /// </summary>
        public bool IsExpired => DateTime.UtcNow - _loadedAt > _expiration;

        /// <summary>
        /// Gets when the cache was loaded.
        /// </summary>
        public DateTime LoadedAt => _loadedAt;

        /// <summary>
        /// Marks the cache as loaded.
        /// </summary>
        public void MarkLoaded()
        {
            _loadedAt = DateTime.UtcNow;
        }

        #region Add Methods

        public void AddSchema(SchemaInfo schema)
        {
            var key = schema.Name;
            _schemas[key] = schema;
            _schemasTrie.Add(key, schema);
        }

        public void AddTable(SchemaObjectInfo table)
        {
            var key = $"{table.Schema}.{table.Name}";
            _tables[key] = table;
            _tablesTrie.Add(table.Name, table);
            _tablesTrie.Add(key, table); // Also index with schema prefix
        }

        public void AddView(SchemaObjectInfo view)
        {
            var key = $"{view.Schema}.{view.Name}";
            _views[key] = view;
            _viewsTrie.Add(view.Name, view);
            _viewsTrie.Add(key, view);
        }

        public void AddColumn(string tableKey, ColumnInfo column)
        {
            var columns = _tableColumns.GetOrAdd(tableKey, _ => new List<ColumnInfo>());
            lock (columns)
            {
                columns.Add(column);
            }

            // Index column by name for global search
            var columnKey = $"{tableKey}.{column.Name}";
            _columnsTrie.Add(column.Name, column);
            _columnsTrie.Add(columnKey, column);
        }

        public void AddProcedure(ProcedureInfo procedure)
        {
            var key = $"{procedure.Schema}.{procedure.Name}";
            _procedures[key] = procedure;
            _proceduresTrie.Add(procedure.Name, procedure);
            _proceduresTrie.Add(key, procedure);
        }

        public void AddFunction(FunctionInfo function)
        {
            var key = $"{function.Schema}.{function.Name}";
            _functions[key] = function;
            _functionsTrie.Add(function.Name, function);
            _functionsTrie.Add(key, function);
        }

        #endregion

        #region Lookup Methods

        /// <summary>
        /// Searches tables by prefix with fuzzy matching.
        /// </summary>
        public IEnumerable<TrieSearchResult<SchemaObjectInfo>> SearchTables(string prefix, int maxResults = 50)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return _tablesTrie.GetAllValues(maxResults)
                    .Select(r => new TrieSearchResult<SchemaObjectInfo>(r.Key, r.Value, 50));
            }
            return _tablesTrie.FuzzySearch(prefix, maxResults);
        }

        /// <summary>
        /// Searches views by prefix with fuzzy matching.
        /// </summary>
        public IEnumerable<TrieSearchResult<SchemaObjectInfo>> SearchViews(string prefix, int maxResults = 50)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return _viewsTrie.GetAllValues(maxResults)
                    .Select(r => new TrieSearchResult<SchemaObjectInfo>(r.Key, r.Value, 50));
            }
            return _viewsTrie.FuzzySearch(prefix, maxResults);
        }

        /// <summary>
        /// Searches both tables and views by prefix.
        /// </summary>
        public IEnumerable<TrieSearchResult<SchemaObjectInfo>> SearchTablesAndViews(string prefix, int maxResults = 50)
        {
            var tables = SearchTables(prefix, maxResults);
            var views = SearchViews(prefix, maxResults);

            return tables.Concat(views)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Key)
                .Take(maxResults);
        }

        /// <summary>
        /// Searches columns by prefix.
        /// </summary>
        public IEnumerable<TrieSearchResult<ColumnInfo>> SearchColumns(string prefix, int maxResults = 50)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return _columnsTrie.GetAllValues(maxResults)
                    .Select(r => new TrieSearchResult<ColumnInfo>(r.Key, r.Value, 50));
            }
            return _columnsTrie.FuzzySearch(prefix, maxResults);
        }

        /// <summary>
        /// Gets columns for a specific table.
        /// </summary>
        public IEnumerable<ColumnInfo> GetColumnsForTable(string tableKey)
        {
            if (_tableColumns.TryGetValue(tableKey, out var columns))
            {
                return columns.ToList(); // Return copy to avoid modification
            }
            return Enumerable.Empty<ColumnInfo>();
        }

        /// <summary>
        /// Searches stored procedures by prefix.
        /// </summary>
        public IEnumerable<TrieSearchResult<ProcedureInfo>> SearchProcedures(string prefix, int maxResults = 50)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return _proceduresTrie.GetAllValues(maxResults)
                    .Select(r => new TrieSearchResult<ProcedureInfo>(r.Key, r.Value, 50));
            }
            return _proceduresTrie.FuzzySearch(prefix, maxResults);
        }

        /// <summary>
        /// Searches functions by prefix.
        /// </summary>
        public IEnumerable<TrieSearchResult<FunctionInfo>> SearchFunctions(string prefix, int maxResults = 50)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return _functionsTrie.GetAllValues(maxResults)
                    .Select(r => new TrieSearchResult<FunctionInfo>(r.Key, r.Value, 50));
            }
            return _functionsTrie.FuzzySearch(prefix, maxResults);
        }

        /// <summary>
        /// Gets all schemas.
        /// </summary>
        public IEnumerable<SchemaInfo> GetSchemas()
        {
            return _schemas.Values.ToList();
        }

        /// <summary>
        /// Gets a table by full name (schema.name).
        /// </summary>
        public SchemaObjectInfo? GetTable(string schemaName, string tableName)
        {
            var key = $"{schemaName}.{tableName}";
            return _tables.TryGetValue(key, out var table) ? table : null;
        }

        #endregion

        /// <summary>
        /// Clears all cached data.
        /// </summary>
        public void Clear()
        {
            _tablesTrie.Clear();
            _viewsTrie.Clear();
            _columnsTrie.Clear();
            _proceduresTrie.Clear();
            _functionsTrie.Clear();
            _schemasTrie.Clear();

            _tables.Clear();
            _views.Clear();
            _tableColumns.Clear();
            _procedures.Clear();
            _functions.Clear();
            _schemas.Clear();

            _loadedAt = DateTime.MinValue;
        }

        /// <summary>
        /// Gets statistics about this cache.
        /// </summary>
        public DatabaseCacheStatistics GetStatistics()
        {
            return new DatabaseCacheStatistics
            {
                ConnectionKey = _connectionKey,
                TableCount = _tables.Count,
                ViewCount = _views.Count,
                ColumnCount = _tableColumns.Values.Sum(c => c.Count),
                ProcedureCount = _procedures.Count,
                FunctionCount = _functions.Count,
                SchemaCount = _schemas.Count,
                LoadedAt = _loadedAt,
                IsExpired = IsExpired
            };
        }
    }

    #region Info Classes

    public class SchemaInfo
    {
        public string Name { get; set; } = string.Empty;
        public int ObjectCount { get; set; }
    }

    public class SchemaObjectInfo
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName => $"{Schema}.{Name}";
        public bool IsView { get; set; }
        public string? Description { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    public class ColumnInfo
    {
        public string TableSchema { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
        public string? DefaultValue { get; set; }
        public string? Description { get; set; }
        public int OrdinalPosition { get; set; }

        public string TableFullName => $"{TableSchema}.{TableName}";
        public string DataTypeDisplay
        {
            get
            {
                if (MaxLength.HasValue && MaxLength.Value > 0)
                {
                    var length = MaxLength.Value == -1 ? "MAX" : MaxLength.Value.ToString();
                    return $"{DataType}({length})";
                }
                if (Precision.HasValue && Scale.HasValue)
                {
                    return $"{DataType}({Precision},{Scale})";
                }
                return DataType;
            }
        }
    }

    public class ProcedureInfo
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName => $"{Schema}.{Name}";
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        public string? Description { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    public class FunctionInfo
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName => $"{Schema}.{Name}";
        public string FunctionType { get; set; } = string.Empty; // Scalar, Table, Inline
        public string? ReturnType { get; set; }
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        public string? Description { get; set; }
    }

    public class ParameterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public bool IsOutput { get; set; }
        public bool HasDefault { get; set; }
        public string? DefaultValue { get; set; }
        public int OrdinalPosition { get; set; }
    }

    #endregion

    #region Statistics Classes

    public class CacheStatistics
    {
        public int TotalDatabases { get; set; }
        public int TotalTables { get; set; }
        public int TotalColumns { get; set; }
        public int TotalProcedures { get; set; }
        public int TotalFunctions { get; set; }
    }

    public class DatabaseCacheStatistics
    {
        public string ConnectionKey { get; set; } = string.Empty;
        public int TableCount { get; set; }
        public int ViewCount { get; set; }
        public int ColumnCount { get; set; }
        public int ProcedureCount { get; set; }
        public int FunctionCount { get; set; }
        public int SchemaCount { get; set; }
        public DateTime LoadedAt { get; set; }
        public bool IsExpired { get; set; }
    }

    #endregion
}
