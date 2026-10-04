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
                // Spec 040 (HIS-14): a draft is stored as "not executed" and never counted as a run.
                recordRequest.IsDraft ? (int)Core.Models.History.ExecutionStatus.NotExecuted : recordRequest.Status,
                recordRequest.ErrorMessage,
                recordRequest.Source,
                recordRequest.TabTitle,
                sessionKey: recordRequest.SessionKey);

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
                Limit = searchRequest.Limit > 0 ? searchRequest.Limit : 100,
                IsOpen = searchRequest.IsOpen,
                NameFilter = searchRequest.NameFilter,
                CamelCaseTokens = searchRequest.CamelCaseTokens,
                PathFilter = searchRequest.PathFilter,
                SqlOnly = searchRequest.SqlOnly
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

            var (entries, totalCount, hasMore) = await _database.SearchPageAsync(filter);

            Log.Debug("History search: {Count} results returned (total={Total}), SearchText={Search}, Server={Server}",
                entries.Count, totalCount, searchRequest.SearchText, searchRequest.Server);

            return CreateSearchResponse(request.RequestId, new HistorySearchResponse
            {
                Success = true,
                Entries = entries.ToArray(),
                TotalCount = totalCount,
                HasMore = hasMore,
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

                case HistoryActions.GetEntries:
                {
                    // Spec 040 (HIS-14): restore on start reopens these.
                    var entries = await _database.GetEntriesAsync(actionRequest.EntryIds ?? Array.Empty<long>());
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        Entries = entries.ToArray()
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

                    // Spec 040 (HIS-04): GroupScope stars the whole grouped query.
                    var isFavorite = actionRequest.GroupScope == true
                        ? await _database.ToggleFavoriteGroupAsync(actionRequest.EntryIds[0])
                        : await _database.ToggleFavoriteAsync(actionRequest.EntryIds[0]);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        IsFavorite = isFavorite
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

                    // Spec 040 (HIS-04): GroupScope deletes the whole grouped query of EntryIds[0].
                    var deletedCount = actionRequest.GroupScope == true
                        ? await _database.DeleteGroupAsync(actionRequest.EntryIds[0])
                        : await _database.DeleteEntriesAsync(actionRequest.EntryIds);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        DeletedCount = deletedCount
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

                case HistoryActions.RemoveOlderThan:
                {
                    if (actionRequest.EntryIds.Length == 0)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "EntryIds required for RemoveOlderThan"
                        });
                    }

                    var removed = await _database.DeleteEntriesOlderThanAsync(
                        actionRequest.EntryIds[0], actionRequest.KeepFavorites ?? true);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        DeletedCount = removed
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

                case HistoryActions.Rename:
                {
                    if (actionRequest.EntryIds.Length == 0)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "EntryIds required for Rename"
                        });
                    }

                    if (string.IsNullOrEmpty(actionRequest.NewName))
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "NewName required for Rename"
                        });
                    }

                    await _database.UpdateTabTitleAsync(actionRequest.EntryIds[0], actionRequest.NewName);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true
                    });
                }

                case HistoryActions.GetVersions:
                {
                    if (actionRequest.EntryIds.Length == 0)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "EntryIds required for GetVersions"
                        });
                    }

                    // Spec 040 (HIS-04): GroupScope lists the grouped query's runs and snapshots,
                    // each with the server and database it ran on (HIS-12).
                    var versions = actionRequest.GroupScope == true
                        ? (await _database.GetVersionsForGroupAsync(actionRequest.EntryIds[0]))
                            .Select(v => new HistoryVersionDto { Id = v.Id, SqlText = v.SqlText, SavedAt = v.SavedAt, Server = v.Server, Database = v.Database })
                        : (await _database.GetVersionsAsync(actionRequest.EntryIds[0]))
                            .Select(v => new HistoryVersionDto { Id = v.Id, SqlText = v.SqlText, SavedAt = v.SavedAt });
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        Versions = versions.ToArray()
                    });
                }

                case HistoryActions.SetOpenStatus:
                {
                    // Spec 040 (HIS-02): by session key and owning shell — every run of the query.
                    if (!string.IsNullOrEmpty(actionRequest.SessionKey) && actionRequest.OwnerPid.HasValue
                        && actionRequest.IsOpen.HasValue)
                    {
                        await _database.SetOpenStatusBySessionAsync(
                            actionRequest.SessionKey!, actionRequest.IsOpen.Value, actionRequest.OwnerPid.Value);
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = true
                        });
                    }

                    if (actionRequest.EntryIds.Length == 0 || !actionRequest.IsOpen.HasValue)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "EntryIds and IsOpen required for SetOpenStatus"
                        });
                    }

                    await _database.SetOpenStatusAsync(actionRequest.EntryIds, actionRequest.IsOpen.Value);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true
                    });
                }

                case HistoryActions.SaveVersion:
                {
                    if (string.IsNullOrEmpty(actionRequest.NewName) || string.IsNullOrEmpty(actionRequest.SqlText))
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "NewName (source path) and SqlText required for SaveVersion"
                        });
                    }

                    var saved = await _database.SaveVersionBySourceAsync(
                        actionRequest.NewName, actionRequest.SqlText, actionRequest.SessionKey);
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = saved
                    });
                }

                case HistoryActions.GetFilterValues:
                {
                    // Spec 040 (HIS-07): the lists behind the filter menu.
                    var (servers, databases) = await _database.GetFilterValuesAsync();
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        Servers = servers.ToArray(),
                        Databases = databases.ToArray(),
                    });
                }

                case HistoryActions.ReconcileOpen:
                {
                    // Spec 040 (HIS-02): sent once by a shell after the engine connects.
                    if (!actionRequest.OwnerPid.HasValue)
                    {
                        return CreateActionResponse(request.RequestId, new HistoryActionResponse
                        {
                            Success = false,
                            Error = "OwnerPid required for ReconcileOpen"
                        });
                    }

                    var restorable = await _database.ReconcileOpenAsync(
                        actionRequest.OwnerPid.Value, actionRequest.OpenSessionKeys ?? Array.Empty<string>());
                    return CreateActionResponse(request.RequestId, new HistoryActionResponse
                    {
                        Success = true,
                        RestorableEntryIds = restorable
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
            Limit = int.MaxValue, // Export: no pagination
            NameFilter = f.NameFilter
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
