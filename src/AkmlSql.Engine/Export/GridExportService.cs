#nullable enable
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Productivity;
using ClosedXML.Excel;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Export;

/// <summary>
/// Engine-side handler for GridExport (message type 68).
/// Generates .xlsx files using ClosedXML. Other formats (CSV, JSON, etc.)
/// are handled directly by the shell and do not reach the engine.
/// </summary>
public class GridExportService
{
    /// <summary>Maximum number of rows allowed in a single export to prevent excessive memory use.</summary>
    private const int MaxExportRows = 100_000;

    /// <summary>Maximum number of columns allowed in a single export.</summary>
    private const int MaxExportColumns = 1_000;

    /// <summary>
    /// Handles a GridExport RPC request and returns the result.
    /// </summary>
    public async Task<RpcMessage?> HandleAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateResponse(request.RequestId, new GridExportResponse
                {
                    Success = false,
                    Error = "Payload is required."
                });
            }

            var exportRequest = MessagePackSerializer.Deserialize<GridExportRequest>(request.Payload);
            var format = (GridExportFormat)exportRequest.Format;

            if (format != GridExportFormat.Xlsx)
            {
                return CreateResponse(request.RequestId, new GridExportResponse
                {
                    Success = false,
                    Error = $"Engine-side export only supports XLSX format. Received: {format}"
                });
            }

            if (string.IsNullOrWhiteSpace(exportRequest.OutputPath))
            {
                return CreateResponse(request.RequestId, new GridExportResponse
                {
                    Success = false,
                    Error = "OutputPath is required."
                });
            }

            // Validate path is absolute
            if (!Path.IsPathRooted(exportRequest.OutputPath))
            {
                return CreateResponse(request.RequestId, new GridExportResponse
                {
                    Success = false,
                    Error = "OutputPath must be an absolute path."
                });
            }

            // Canonicalize to prevent path traversal
            var canonicalPath = Path.GetFullPath(exportRequest.OutputPath);

            // Guard against excessive data sizes before allocating resources
            if (exportRequest.Rows != null && exportRequest.Rows.Length > MaxExportRows)
            {
                return CreateResponse(request.RequestId, new GridExportResponse
                {
                    Success = false,
                    Error = $"Export row count ({exportRequest.Rows.Length:N0}) exceeds the maximum allowed ({MaxExportRows:N0}). Please filter the result set before exporting."
                });
            }

            if (exportRequest.ColumnHeaders != null && exportRequest.ColumnHeaders.Length > MaxExportColumns)
            {
                return CreateResponse(request.RequestId, new GridExportResponse
                {
                    Success = false,
                    Error = $"Export column count ({exportRequest.ColumnHeaders.Length:N0}) exceeds the maximum allowed ({MaxExportColumns:N0})."
                });
            }

            var response = await Task.Run(() => GenerateXlsx(
                canonicalPath,
                exportRequest.ColumnHeaders,
                exportRequest.Rows,
                exportRequest.ColumnTypes));

            return CreateResponse(request.RequestId, response);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GridExportService: failed to handle export request");
            return CreateResponse(request.RequestId, new GridExportResponse
            {
                Success = false,
                Error = $"Export failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Generates an .xlsx file using ClosedXML.
    /// </summary>
    private static GridExportResponse GenerateXlsx(
        string outputPath,
        string[] columnHeaders,
        string[][] rows,
        string[] columnTypes)
    {
        try
        {
            // Ensure output directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Query Results");

            // Write headers (row 1) with bold style
            for (int col = 0; col < columnHeaders.Length; col++)
            {
                var cell = worksheet.Cell(1, col + 1);
                cell.Value = columnHeaders[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }

            // Write data rows starting at row 2
            for (int rowIdx = 0; rowIdx < rows.Length; rowIdx++)
            {
                var row = rows[rowIdx];
                for (int colIdx = 0; colIdx < row.Length && colIdx < columnHeaders.Length; colIdx++)
                {
                    var cell = worksheet.Cell(rowIdx + 2, colIdx + 1);
                    var value = row[colIdx];

                    if (string.IsNullOrEmpty(value) || value == "NULL")
                    {
                        cell.Value = Blank.Value;
                        continue;
                    }

                    // Apply type-appropriate formatting based on column type
                    var colType = colIdx < columnTypes.Length
                        ? columnTypes[colIdx]?.ToLowerInvariant() ?? ""
                        : "";

                    SetTypedCellValue(cell, value, colType);
                }
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            // Enable auto-filter on header row
            if (columnHeaders.Length > 0 && rows.Length > 0)
            {
                worksheet.Range(1, 1, rows.Length + 1, columnHeaders.Length).SetAutoFilter();
            }

            // Freeze header row
            worksheet.SheetView.FreezeRows(1);

            workbook.SaveAs(outputPath);

            Log.Information("GridExportService: exported {RowCount} rows to {Path}", rows.Length, outputPath);

            return new GridExportResponse
            {
                Success = true,
                OutputPath = outputPath,
                RowCount = rows.Length
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GridExportService: XLSX generation failed");
            return new GridExportResponse
            {
                Success = false,
                Error = $"XLSX generation failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Sets a cell value with type-appropriate formatting based on the SQL column type.
    /// </summary>
    private static void SetTypedCellValue(IXLCell cell, string value, string colType)
    {
        switch (colType)
        {
            case "int" or "bigint" or "smallint" or "tinyint":
                if (long.TryParse(value, out var longVal))
                {
                    cell.Value = longVal;
                    cell.Style.NumberFormat.NumberFormatId = 1; // 0
                }
                else
                {
                    cell.Value = value;
                }
                break;

            case "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real":
                if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
                {
                    cell.Value = dblVal;
                    if (colType is "money" or "smallmoney")
                    {
                        cell.Style.NumberFormat.Format = "#,##0.00";
                    }
                    else
                    {
                        cell.Style.NumberFormat.Format = "0.####";
                    }
                }
                else
                {
                    cell.Value = value;
                }
                break;

            case "bit":
                if (value is "1" or "True" or "true")
                    cell.Value = true;
                else if (value is "0" or "False" or "false")
                    cell.Value = false;
                else
                    cell.Value = value;
                break;

            case "date":
                if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dateVal))
                {
                    cell.Value = dateVal;
                    cell.Style.NumberFormat.Format = "yyyy-MM-dd";
                }
                else
                {
                    cell.Value = value;
                }
                break;

            case "datetime" or "datetime2" or "smalldatetime":
                if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dtVal))
                {
                    cell.Value = dtVal;
                    cell.Style.NumberFormat.Format = "yyyy-MM-dd HH:mm:ss";
                }
                else
                {
                    cell.Value = value;
                }
                break;

            case "time":
                if (TimeSpan.TryParse(value, out var tsVal))
                {
                    cell.Value = tsVal;
                    cell.Style.NumberFormat.Format = "HH:mm:ss";
                }
                else
                {
                    cell.Value = value;
                }
                break;

            default:
                cell.Value = value;
                break;
        }
    }

    private static RpcMessage CreateResponse(int requestId, GridExportResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.GridExportResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }
}
