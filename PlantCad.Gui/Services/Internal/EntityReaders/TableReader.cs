using System;
using System.Linq;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class TableReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is TableEntity;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not TableEntity te)
            return;

        try
        {
            int rows = te.Rows?.Count ?? 0;
            int cols = te.Columns?.Count ?? 0;
            if (rows <= 0 || cols <= 0 || te.Rows == null || te.Columns == null)
            {
                return;
            }

            var rowsList = te.Rows!;
            var colsList = te.Columns!;

            var rowHeights = rowsList.Select(r => r.Height).ToList();
            var colWidths = colsList.Select(colm => colm.Width).ToList();

            var cells = new string?[rows, cols];
            CadTextHAlign[,]? hAligns = null;
            CadTextVAlign[,]? vAligns = null;
            uint?[,]? bgColors = null;
            bool[,]? wraps = null;
            bool anyAlign = false,
                anyBg = false,
                anyWrap = false;

            for (int r = 0; r < rows; r++)
            {
                var row = rowsList[r];
                if (row == null)
                {
                    continue;
                }

                var cellsList = row.Cells;
                for (int col = 0; col < cols; col++)
                {
                    string? text = null;
                    var cell =
                        (cellsList != null && col >= 0 && col < cellsList.Count)
                            ? cellsList[col]
                            : null;
                    var colm = colsList[col];
                    var tableFmt = te.Content?.CellStyleOverride ?? new TableEntity.CellStyle();
                    var tableStyle = te.Content?.CellStyleOverride ?? new TableEntity.CellStyle();
                    if (cell?.Content != null && cell.Content.Value != null)
                    {
                        var val = cell.Content.Value;
                        if (!string.IsNullOrWhiteSpace(val.FormatedValue))
                        {
                            text = val.FormatedValue;
                        }
                        else if (!string.IsNullOrWhiteSpace(val.Text))
                        {
                            text = val.Text;
                        }
                        else if (val.Value != null)
                        {
                            text = val.Value.ToString();
                        }
                    }

                    cells[r, col] = text;

                    // Alignment
                    var (ha, va) = TryGetAlignment(cell, row, colm, tableFmt, tableStyle);
                    if (ha.HasValue || va.HasValue)
                    {
                        anyAlign = true;
                        hAligns ??= new CadTextHAlign[rows, cols];
                        vAligns ??= new CadTextVAlign[rows, cols];
                        if (ha.HasValue)
                            hAligns[r, col] = ha.Value;
                        if (va.HasValue)
                            vAligns[r, col] = va.Value;
                    }

                    // Background color
                    var bg = TryGetBackground(cell, row, colm, tableStyle);
                    if (bg.HasValue)
                    {
                        anyBg = true;
                        bgColors ??= new uint?[rows, cols];
                        bgColors[r, col] = bg.Value;
                    }

                    // Wrap
                    var w = TryGetWrap(cell, row, colm, tableStyle);
                    if (w.HasValue)
                    {
                        anyWrap = true;
                        wraps ??= new bool[rows, cols];
                        wraps[r, col] = w.Value;
                    }
                }
            }

            int[,]? rowSpan = null;
            int[,]? colSpan = null;
            var merged = te.Content?.MergedCellRanges;
            if (merged != null && merged.Count > 0)
            {
                rowSpan = new int[rows, cols];
                colSpan = new int[rows, cols];
                // Default all as anchors of 1x1
                for (int r = 0; r < rows; r++)
                for (int col = 0; col < cols; col++)
                {
                    rowSpan[r, col] = 1;
                    colSpan[r, col] = 1;
                }

                foreach (var rng in merged)
                {
                    int r0 = Math.Clamp(rng.TopRowIndex, 0, rows - 1);
                    int c0 = Math.Clamp(rng.LeftColumnIndex, 0, cols - 1);
                    int r1 = Math.Clamp(rng.BottomRowIndex, 0, rows - 1);
                    int c1 = Math.Clamp(rng.RightColumnIndex, 0, cols - 1);
                    int rs = Math.Max(1, r1 - r0 + 1);
                    int cs = Math.Max(1, c1 - c0 + 1);
                    // Set anchor
                    rowSpan[r0, c0] = rs;
                    colSpan[r0, c0] = cs;
                    // Mark covered cells (excluding anchor) as 0
                    for (int rr = r0; rr <= r1; rr++)
                    for (int cc = c0; cc <= c1; cc++)
                    {
                        if (rr == r0 && cc == c0)
                            continue;
                        rowSpan[rr, cc] = 0;
                        colSpan[rr, cc] = 0;
                    }
                }
            }

            // Compute outer bounds using insert position and total sizes (axis-aligned)
            double width = colWidths.Sum();
            double height = rowHeights.Sum();
            double tx = te.InsertPoint.X;
            double ty = te.InsertPoint.Y;
            var bounds = new Avalonia.Rect(tx, ty, Math.Max(0, width), Math.Max(0, height));

            context.Tables.Add(
                new CadTable
                {
                    Id = te.Handle.ToString(),
                    Layer = te.Layer?.Name ?? string.Empty,
                    Bounds = bounds,
                    Rows = rows,
                    Columns = cols,
                    RowHeights = rowHeights,
                    ColumnWidths = colWidths,
                    HeaderRowCount = 0,
                    Cells = cells,
                    CellRowSpan = rowSpan,
                    CellColSpan = colSpan,
                    CellHAlign = anyAlign ? hAligns : null,
                    CellVAlign = anyAlign ? vAligns : null,
                    CellBackgroundArgb = anyBg ? bgColors : null,
                    CellWrap = anyWrap ? wraps : null,
                }
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read TABLE entity.", ex);
        }
    }

    private static uint ToArgb(ACadSharp.Color c)
    {
        return (0xFFu << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    }

    private static (CadTextHAlign? ha, CadTextVAlign? va) MapAlignmentInt(int alignment)
    {
        // Best-effort mapping consistent with common CAD table alignment codes
        return alignment switch
        {
            1 => (CadTextHAlign.Left, CadTextVAlign.Top),
            2 => (CadTextHAlign.Center, CadTextVAlign.Top),
            3 => (CadTextHAlign.Right, CadTextVAlign.Top),
            4 => (CadTextHAlign.Left, CadTextVAlign.Middle),
            5 => (CadTextHAlign.Center, CadTextVAlign.Middle),
            6 => (CadTextHAlign.Right, CadTextVAlign.Middle),
            7 => (CadTextHAlign.Left, CadTextVAlign.Bottom),
            8 => (CadTextHAlign.Center, CadTextVAlign.Bottom),
            9 => (CadTextHAlign.Right, CadTextVAlign.Bottom),
            _ => (null, null),
        };
    }

    private static (CadTextHAlign? ha, CadTextVAlign? va) TryGetAlignment(
        TableEntity.Cell? cell,
        TableEntity.Row row,
        TableEntity.Column colm,
        TableEntity.ContentFormat tableFmt,
        TableEntity.CellStyle tableStyle
    )
    {
        // Cell content format
        if (cell?.Content?.Format != null)
        {
            var fmt = cell.Content.Format;
            if (
                fmt.PropertyOverrideFlags.HasFlag(TableEntity.TableCellStylePropertyFlags.Alignment)
            )
            {
                var r = MapAlignmentInt(fmt.Alignment);
                if (r.ha.HasValue || r.va.HasValue)
                    return r;
            }
        }

        // Cell style override
        if (
            cell?.StyleOverride != null
            && cell.StyleOverride.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.Alignment
            )
        )
        {
            var r = MapAlignmentInt(cell.StyleOverride.Alignment);
            if (r.ha.HasValue || r.va.HasValue)
                return r;
        }

        // Row style override
        if (
            row?.CellStyleOverride != null
            && row.CellStyleOverride.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.Alignment
            )
        )
        {
            var r = MapAlignmentInt(row.CellStyleOverride.Alignment);
            if (r.ha.HasValue || r.va.HasValue)
                return r;
        }

        // Column style override
        if (
            colm?.StyleOverride != null
            && colm.StyleOverride.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.Alignment
            )
        )
        {
            var r = MapAlignmentInt(colm.StyleOverride.Alignment);
            if (r.ha.HasValue || r.va.HasValue)
                return r;
        }

        // Table level style/content
        if (
            tableFmt != null
            && tableFmt.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.Alignment
            )
        )
        {
            var r = MapAlignmentInt(tableFmt.Alignment);
            if (r.ha.HasValue || r.va.HasValue)
                return r;
        }

        if (
            tableStyle != null
            && tableStyle.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.Alignment
            )
        )
        {
            var r = MapAlignmentInt(tableStyle.Alignment);
            if (r.ha.HasValue || r.va.HasValue)
                return r;
        }

        return (null, null);
    }

    private static uint? TryGetBackground(
        TableEntity.Cell? cell,
        TableEntity.Row row,
        TableEntity.Column colm,
        TableEntity.CellStyle tableStyle
    )
    {
        // Cell style override
        if (
            cell?.StyleOverride != null
            && cell.StyleOverride.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.BackgroundColor
            )
        )
        {
            return ToArgb(cell.StyleOverride.BackgroundColor);
        }

        // Row style override
        if (
            row?.CellStyleOverride != null
            && row.CellStyleOverride.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.BackgroundColor
            )
        )
        {
            return ToArgb(row.CellStyleOverride.BackgroundColor);
        }

        // Column style override
        if (
            colm?.StyleOverride != null
            && colm.StyleOverride.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.BackgroundColor
            )
        )
        {
            return ToArgb(colm.StyleOverride.BackgroundColor);
        }

        // Table style default
        if (
            tableStyle != null
            && tableStyle.PropertyOverrideFlags.HasFlag(
                TableEntity.TableCellStylePropertyFlags.BackgroundColor
            )
        )
        {
            return ToArgb(tableStyle.BackgroundColor);
        }

        return null;
    }

    private static bool? TryGetWrap(
        TableEntity.Cell? cell,
        TableEntity.Row row,
        TableEntity.Column colm,
        TableEntity.CellStyle tableStyle
    )
    {
        // Heuristic: Flow layout implies wrapping permitted.
        const TableEntity.TableCellContentLayoutFlags Flow = TableEntity
            .TableCellContentLayoutFlags
            .Flow;
        if (cell?.StyleOverride != null && cell.StyleOverride.ContentLayoutFlags.HasFlag(Flow))
            return true;
        if (
            row?.CellStyleOverride != null
            && row.CellStyleOverride.ContentLayoutFlags.HasFlag(Flow)
        )
            return true;
        if (colm?.StyleOverride != null && colm.StyleOverride.ContentLayoutFlags.HasFlag(Flow))
            return true;
        if (tableStyle != null && tableStyle.ContentLayoutFlags.HasFlag(Flow))
            return true;
        return null;
    }
}
