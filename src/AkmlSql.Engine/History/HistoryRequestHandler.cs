using System.Globalization;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.History;

/// <summary>
/// Handles MessageType 40 (HistoryRecord) and MessageType 41 (HistorySearch) requests from the shell.
/// Deserializes requests, delegates to the SQLite history database, and returns responses.
/// </summary>
public class HistoryRequestHandler(HistoryDatabase database)
{
    private readonly HistoryDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>
    /// Handles a HistoryRecord RPC request. Inserts the execution record into the
    /// history database and returns a <see cref="HistoryRecordResponse"/>.
    /// </summary>
    public async Task<RpcMessage?> HandleRecordAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateRecordResponse(request.RequestId, new HistoryRecordResponse
                {
                    Success = false,
                    Error = "Payload required"
                });
            }

            var recordRequest = MessagePackSerializer.Deserialize<HistoryRecordRequest>(request.Payload);

            var entryId = await _database.InsertEntryAsync(
                recordRequest.SqlText,
                recordRequest.Truncated,
                recordRequest.Server,
                recordRequest.Database,
                recordRequest.Username,
                recordRequest.DurationMs,
                recordRequest.RowCount,
                recordRequest.Status,
                recordRequest.ErrorMessage,
                recordRequest.Source,
                recordRequest.TabTitle);

            Log.Debug("History record saved: EntryId={EntryId}, Server={Server}, Database={Database}",
                entryId, recordRequest.Server, recordRequest.Database);

            // If RequestId is 0, this was a fire-and-forget notification — no response needed
            if (request.RequestId == 0)
            {
                return null;
            }

            return CreateRecordResponse(request.RequestId, new HistoryRecordResponse
            {
                Success = true,
                EntryId = entryId
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to record history entry");

            // If fire-and-forget, silently fail
            if (request.RequestId == 0)
            {
                return null;
            }

            return CreateRecordResponse(request.RequestId, new HistoryRecordResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Handles a HistorySearch RPC request. Deserializes the search request, converts
    /// it to a <see cref="HistoryFilter"/>, executes the search, and returns the results.
    /// </summary>
    public async Task<RpcMessage?> HandleSearchAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateSearchResponse(request.RequestId, new HistorySearchResponse
                {
                    Success = false,
                    Error = "Payload required"
                });
            }

            var searchRequest = MessagePackSerializer.Deserialize<HistorySearchRequest>(request.Payload);

            var filter = new HistoryFilter
            {
                SearchText = searchRequest.SearchText,
                Server = searchRequest.Server,
                Database = searchRequest.Database,
                Status = searchRequest.Status,
                FavoritesOnly = searchRequest.FavoritesOnly,
                Deduplicate = searchRequest.Deduplicate,
                Offset = searchRequest.Offset,
                Limit = searchRequest.Limit > 0 ? searchRequest.Limit : 100
            };

            // Parse ISO 8601 date strings to DateTime
            if (!string.IsNullOrEmpty(searchRequest.DateFrom) &&
                DateTime.TryParse(searchRequest.DateFrom, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dateFrom))
            {
                filter.DateFrom = dateFrom;
            }

            if (!string.IsNullOrEmpty(searchRequest.DateTo) &&
                DateTime.TryParse(searchRequest.DateTo, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dateTo))
            {
                filter.DateTo = dateTo;
            }

            var (entries, totalCount) = await _database.SearchAsync(filter);

            Log.Debug("History search: {Count} results returned (total={Total}), SearchText={Search}, Server={Server}",
                entries.Count, totalCount, searchRequest.SearchText, searchRequest.Server);

            return CreateSearchResponse(request.RequestId, new HistorySearchResponse
            {
                Success = true,
                Entries = entries.ToArray(),
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to execute history search");

            return CreateSearchResponse(request.RequestId, new HistorySearchResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Handles a HistoryAction RPC request (MessageType 42). Dispatches on the Action field:
    /// GetFullSql(0), ToggleFavorite(1), Delete(2), Export(3), GetDiff(4), DeleteAll(5).
    /// </summary>
    public async Task<RpcMessage?> HandleActionAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateActionResponse(request.RequestId, new HistoryActionResponse
                {
                    Success = false,
                    Error = "Payload required"
                });
            }

            var actionRequest = MessagePackSerializer.Deserialize<HistoryActionRequest>(request.Payload);

            switch (actionRequest.Action)
            {
                case HistoryActions.GetFullSql:
                {
                    if (actionRequest.EntryIds.Length == 0)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "EntryIds required for GetFullSql"
                        });
                    }

                    var fullSql = await _database.GetFullSqlAsync(actionRequest.EntryIds[0]);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = fullSql != null,
                        FullSqlText = fullSql,
                        Error = fullSql == null ? "Entry not found" : null
                    });
                }

                case HistoryActions.GetDiff:
                {
                    if (actionRequest.EntryIds.Length < 2)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "Exactly 2 EntryIds required for GetDiff"
                        });
                    }

                    var (sql1, sql2) = await _database.GetEntriesForDiffAsync(
                        actionRequest.EntryIds[0], actionRequest.EntryIds[1]);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = sql1 != null && sql2 != null,
                        DiffLeftSql = sql1,
                        DiffRightSql = sql2,
                        Error = (sql1 == null || sql2 == null) ? "One or both entries not found" : null
                    });
                }

                case HistoryActions.ToggleFavorite:
                {
                    if (actionRequest.EntryIds.Length == 0)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "EntryIds required for ToggleFavorite"
                        });
                    }

                    await _database.ToggleFavoriteAsync(actionRequest.EntryIds[0]);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true
                    });
                }

                case HistoryActions.Delete:
                {
                    if (actionRequest.EntryIds.Length == 0)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "EntryIds required for Delete"
                        });
                    }

                    await _database.DeleteEntriesAsync(actionRequest.EntryIds);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true
                    });
                }

                case HistoryActions.DeleteAll:
                {
                    await _database.DeleteAllNonFavoriteAsync();
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true
                    });
                }

                case HistoryActions.Export:
                {
                    if (string.IsNullOrEmpty(actionRequest.ExportPath))
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "ExportPath required for Export"
                        });
                    }

                    if (!actionRequest.ExportFormat.HasValue)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "ExportFormat required for Export"
                        });
                    }

                    // Validate export path is absolute
                    if (!Path.IsPathRooted(actionRequest.ExportPath))
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "ExportPath must be an absolute path"
                        });
                    }

                    // Canonicalize and validate the path
                    var canonicalPath = Path.GetFullPath(actionRequest.ExportPath);

                    // Build filter from the optional Filter field, or use an empty filter
                    var filter = BuildFilterFromRequest(actionRequest);

                    var exportFormat = (ExportFormat)actionRequest.ExportFormat.Value;
                    await _database.ExportAsync(filter, exportFormat, canonicalPath);

                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        ExportPath = canonicalPath
                    });
                }

                default:
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = false,
                        Error = $"Unknown action: {actionRequest.Action}"
                    });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle history action");
            return CreateActionResponse(request.RequestId, new HistoryActionResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Legacy HandleAsync method — delegates to HandleRecordAsync for backward compatibility.
    /// </summary>
    public Task<RpcMessage?> HandleAsync(RpcMessage request)
    {
        return HandleRecordAsync(request);
    }

    private static RpcMessage CreateRecordResponse(int requestId, HistoryRecordResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.HistoryRecordResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }

    private static RpcMessage CreateSearchResponse(int requestId, HistorySearchResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.HistorySearchResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }

    private static RpcMessage CreateActionResponse(int requestId, HistoryActionResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.HistoryActionResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }

    /// <summary>
    /// Builds a <see cref="HistoryFilter"/> from the optional Filter field on an action request.
    /// Falls back to an empty filter if none is provided.
    /// </summary>
    private static HistoryFilter BuildFilterFromRequest(HistoryActionRequest request)
    {
        if (request.Filter == null)
        {
            return new HistoryFilter();
        }

        var f = request.Filter;
        var filter = new HistoryFilter
        {
            SearchText = f.SearchText,
            Server = f.Server,
            Database = f.Database,
            Status = f.Status,
            FavoritesOnly = f.FavoritesOnly,
            Deduplicate = f.Deduplicate,
            Offset = 0,
            Limit = int.MaxValue // Export: no pagination
        };

        if (!string.IsNullOrEmpty(f.DateFrom) &&
            DateTime.TryParse(f.DateFrom, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dateFrom))
        {
            filter.DateFrom = dateFrom;
        }

        if (!string.IsNullOrEmpty(f.DateTo) &&
            DateTime.TryParse(f.DateTo, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dateTo))
        {
            filter.DateTo = dateTo;
        }

        return filter;
    }
}
