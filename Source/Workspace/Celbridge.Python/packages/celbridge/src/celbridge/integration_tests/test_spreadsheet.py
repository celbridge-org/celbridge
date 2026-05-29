import json

import pytest

from celbridge.cel_proxy import CelError

from .helpers import close_if_open, delete_if_exists


WORKBOOK = "TestSpreadsheet/sheet.xlsx"


@pytest.fixture(autouse=True)
def workspace(explorer, document):
    delete_if_exists(explorer, "TestSpreadsheet")
    explorer.create_folder("TestSpreadsheet")
    explorer.create_file(WORKBOOK)
    yield
    close_if_open(document, WORKBOOK)
    delete_if_exists(explorer, "TestSpreadsheet")


class TestSpreadsheet:

    # spreadsheet_get_info

    def test_get_info_empty_workbook(self, spreadsheet):
        info = spreadsheet.get_info(WORKBOOK)
        assert len(info["sheets"]) == 1
        sheet = info["sheets"][0]
        assert sheet["name"] == "Sheet1"
        assert sheet["position"] == 1
        assert sheet["rowCount"] == 0
        assert sheet.get("usedRange") is None
        assert sheet["frozenRows"] == 0
        assert sheet["frozenColumns"] == 0

    def test_get_info_reports_frozen_panes(self, spreadsheet):
        spreadsheet.freeze_panes(WORKBOOK, "Sheet1", rows=2, columns=1)
        info = spreadsheet.get_info(WORKBOOK)
        sheet = info["sheets"][0]
        assert sheet["frozenRows"] == 2
        assert sheet["frozenColumns"] == 1

    # spreadsheet_read_sheet

    def test_read_sheet_empty(self, spreadsheet):
        result = spreadsheet.read_sheet(WORKBOOK, "Sheet1")
        assert result["totalRowCount"] == 0
        assert result["rows"] == []

    def test_read_sheet_with_data(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "month,sales\nJan,100\nFeb,200\n"}],
        )
        result = spreadsheet.read_sheet(WORKBOOK, "Sheet1", headers=True)
        assert result["totalRowCount"] == 2
        first_row = result["rows"][0]
        assert first_row["month"] == "Jan"
        assert first_row["sales"] == 100

    # spreadsheet_export_csv

    def test_export_csv_inline_empty(self, spreadsheet):
        result = spreadsheet.export_csv(WORKBOOK, "Sheet1")
        assert result == ""

    def test_export_csv_inline_with_data(self, spreadsheet):
        spreadsheet.append_rows(WORKBOOK, "Sheet1", [["A", "B"], ["C", "D"]])
        result = spreadsheet.export_csv(WORKBOOK, "Sheet1")
        assert isinstance(result, str)
        assert result.endswith("\r\n")
        assert "A" in result

    def test_export_csv_destination(self, spreadsheet, file):
        spreadsheet.append_rows(WORKBOOK, "Sheet1", [["x", "y"], ["1", "2"]])
        dest = "TestSpreadsheet/export.csv"
        result = spreadsheet.export_csv(WORKBOOK, "Sheet1", destination=dest)
        assert isinstance(result, dict)
        assert result["rowCount"] == 2
        assert result["columnCount"] == 2
        assert result["byteCount"] > 0
        # Tool responses emit resource keys in canonical "root:path" form.
        assert result["destination"] == f"project:{dest}"
        info = file.get_info(dest)
        assert info["type"] == "file"

    def test_export_csv_invalid_destination(self, spreadsheet):
        with pytest.raises(CelError):
            spreadsheet.export_csv(
                WORKBOOK, "Sheet1", destination="\\invalid\\path"
            )

    # spreadsheet_write_cells

    def test_write_cells(self, spreadsheet):
        result = spreadsheet.write_cells(
            WORKBOOK, "Sheet1", [{"cell": "B2", "value": 99}]
        )
        assert result["cellCount"] == 1
        read_result = spreadsheet.read_sheet(WORKBOOK, "Sheet1", range="B2")
        assert read_result["rows"][0][0] == 99.0

    # spreadsheet_append_rows

    def test_append_rows(self, spreadsheet):
        result = spreadsheet.append_rows(
            WORKBOOK, "Sheet1", [["Jan", 100], ["Feb", 200]]
        )
        assert result["appendedRowCount"] == 2
        assert result["firstRow"] == 1
        assert result["lastRow"] == 2

    # spreadsheet_import_csv

    def test_import_csv_multi_sheet(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Q1", "Q2"])
        result = spreadsheet.import_csv(
            WORKBOOK,
            [
                {"sheet": "Q1", "csvText": "month,total\nJan,100\n"},
                {"sheet": "Q2", "csvText": "month,total\nApr,200\n"},
            ],
        )
        assert result["importsApplied"] == 2
        assert result["totalRowCount"] == 4  # header + 1 data row per sheet
        assert result["sheetsCreated"] == 0

    # spreadsheet_add_sheets

    def test_add_sheets(self, spreadsheet):
        result = spreadsheet.add_sheets(WORKBOOK, ["Data", "Summary"])
        assert "Data" in result["sheets"]
        assert "Summary" in result["sheets"]

    def test_add_sheets_duplicate_in_batch_fails(self, spreadsheet):
        with pytest.raises(CelError):
            spreadsheet.add_sheets(WORKBOOK, ["NewSheet", "NewSheet"])

    def test_add_sheets_collision_with_existing_fails(self, spreadsheet):
        with pytest.raises(CelError):
            spreadsheet.add_sheets(WORKBOOK, ["Sheet1"])

    # spreadsheet_remove_sheet

    def test_remove_sheet(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Extra"])
        result = spreadsheet.remove_sheet(WORKBOOK, "Extra")
        assert result["sheet"] == "Extra"
        info = spreadsheet.get_info(WORKBOOK)
        names = [s["name"] for s in info["sheets"]]
        assert "Extra" not in names

    def test_remove_last_sheet_fails(self, spreadsheet):
        with pytest.raises(CelError):
            spreadsheet.remove_sheet(WORKBOOK, "Sheet1")

    # spreadsheet_rename_sheet

    def test_rename_sheet(self, spreadsheet):
        result = spreadsheet.rename_sheet(WORKBOOK, "Sheet1", "Sales")
        assert result["previousName"] == "Sheet1"
        assert result["newName"] == "Sales"
        info = spreadsheet.get_info(WORKBOOK)
        names = [s["name"] for s in info["sheets"]]
        assert "Sales" in names
        assert "Sheet1" not in names

    # spreadsheet_move_sheet

    def test_move_sheet(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["A", "B", "C"])
        result = spreadsheet.move_sheet(WORKBOOK, "C", 1)
        assert result["position"] == 1
        info = spreadsheet.get_info(WORKBOOK)
        assert info["sheets"][0]["name"] == "C"

    # formula recalculation

    def test_formula_recalculates_on_save(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK, "Sheet1",
            [{"cell": "A1", "value": 10}, {"cell": "A2", "value": 20}],
        )
        spreadsheet.write_cells(
            WORKBOOK, "Sheet1",
            [{"cell": "A3", "value": "=SUM(A1:A2)", "isFormula": True}],
        )
        result = spreadsheet.read_sheet(WORKBOOK, "Sheet1", range="A3")
        assert result["rows"][0][0] == 30.0

    # spreadsheet_format_ranges

    def test_format_ranges_text_and_background(self, spreadsheet):
        edits = [
            {
                "sheet": "Sheet1",
                "range": "A1",
                "format": {
                    "textFormat": {"bold": True},
                    "backgroundColor": "#FF0000",
                },
            }
        ]
        result = spreadsheet.format_ranges(WORKBOOK, edits)
        assert result["editsApplied"] == 1
        assert result["propertiesApplied"] > 0

    def test_format_ranges_borders(self, spreadsheet):
        edits = [
            {
                "sheet": "Sheet1",
                "range": "A1",
                "format": {
                    "borders": {
                        "top": {"style": "SOLID", "color": "#000000"},
                        "bottom": {"style": "DASHED", "color": "#888888"},
                    }
                },
            }
        ]
        result = spreadsheet.format_ranges(WORKBOOK, edits)
        assert result["editsApplied"] == 1

    def test_format_ranges_column_width_and_autofit(self, spreadsheet):
        edits = [
            {
                "sheet": "Sheet1",
                "range": "A",
                "format": {"columnWidth": 20, "autoFitColumns": True},
            }
        ]
        result = spreadsheet.format_ranges(WORKBOOK, edits)
        assert result["autoFitApplied"]

    def test_format_ranges_unknown_color_raises(self, spreadsheet):
        with pytest.raises(CelError):
            spreadsheet.format_ranges(
                WORKBOOK,
                [
                    {
                        "sheet": "Sheet1",
                        "range": "A1",
                        "format": {"backgroundColor": "not-a-color"},
                    }
                ],
            )

    # spreadsheet_read_format

    def test_read_format_round_trips_through_format_ranges(self, spreadsheet):
        spreadsheet.format_ranges(
            WORKBOOK,
            [
                {
                    "sheet": "Sheet1",
                    "range": "A1",
                    "format": {"textFormat": {"bold": True}, "backgroundColor": "#FFFF00"},
                }
            ],
        )
        format_grid = spreadsheet.read_format(WORKBOOK, "Sheet1", "A1")
        assert "rows" in format_grid
        cell_spec = format_grid["rows"][0][0]
        assert cell_spec.get("textFormat", {}).get("bold", False)
        result = spreadsheet.format_ranges(
            WORKBOOK,
            [{"sheet": "Sheet1", "range": "B1", "format": cell_spec}],
        )
        assert result["editsApplied"] == 1

    # spreadsheet_freeze_panes

    def test_freeze_panes_rows_columns_and_clear(self, spreadsheet):
        result = spreadsheet.freeze_panes(WORKBOOK, "Sheet1", rows=1)
        assert result["rows"] == 1
        assert result["columns"] == 0

        result = spreadsheet.freeze_panes(WORKBOOK, "Sheet1", rows=1, columns=2)
        assert result["rows"] == 1
        assert result["columns"] == 2

        result = spreadsheet.freeze_panes(WORKBOOK, "Sheet1", rows=0, columns=0)
        assert result["rows"] == 0
        assert result["columns"] == 0

    # spreadsheet_set_active_view

    def test_set_active_view_persists_sheet_and_selection(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Summary"])
        result = spreadsheet.set_active_view(WORKBOOK, "Summary", range="A1")
        assert result["sheet"] == "Summary"
        assert result["range"] == "A1"

    # spreadsheet_delete

    def test_delete_rows_shifts_remaining_rows_up(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "a\nb\nc\nd\ne\n"}],
        )
        result = spreadsheet.delete(
            WORKBOOK,
            [{"sheet": "Sheet1", "range": "2:3"}],
        )
        assert result["deletedRowCount"] == 2

        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1")["rows"]
        assert [row[0] for row in rows] == ["a", "d", "e"]

    def test_delete_uses_original_coordinates_across_operations(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "\n".join(f"row{i}" for i in range(1, 13)) + "\n"}],
        )
        result = spreadsheet.delete(
            WORKBOOK,
            [
                {"sheet": "Sheet1", "range": "3:5"},
                {"sheet": "Sheet1", "range": "10"},
            ],
        )
        assert result["deletedRowCount"] == 4

        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1")["rows"]
        assert [row[0] for row in rows] == [
            "row1", "row2", "row6", "row7", "row8", "row9", "row11", "row12",
        ]

    # spreadsheet_clear

    def test_clear_range_leaves_other_cells_alone(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "a,b,c\n1,2,3\n4,5,6\n"}],
        )
        result = spreadsheet.clear(
            WORKBOOK,
            [{"sheet": "Sheet1", "range": "B2:C2"}],
        )
        assert result["cellCount"] == 2

        # import_csv infers numeric fields by default, so plain integers
        # round-trip as ints.
        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1")["rows"]
        assert rows[0] == ["a", "b", "c"]
        assert rows[1][0] == 1
        assert rows[1][1] is None
        assert rows[1][2] is None
        assert rows[2] == [4, 5, 6]

    def test_clear_empty_range_clears_entire_sheet(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "a,b\n1,2\n"}],
        )
        spreadsheet.clear(WORKBOOK, [{"sheet": "Sheet1", "range": ""}])

        result = spreadsheet.read_sheet(WORKBOOK, "Sheet1")
        assert result["totalRowCount"] == 0

    # spreadsheet_get_active_view

    def test_get_active_view_round_trips_through_set_active_view(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Summary"])
        spreadsheet.set_active_view(
            WORKBOOK,
            "Summary",
            range="B2:D4",
            active_cell="C3",
            top_left_cell="A1",
        )

        view = spreadsheet.get_active_view(WORKBOOK)
        assert view["sheet"] == "Summary"
        assert view["range"] == "B2:D4"
        assert view["activeCell"] == "C3"
        assert view["topLeftCell"] == "A1"

        # Round-trip the get response back through set_active_view; the workbook
        # state should still match.
        spreadsheet.set_active_view(
            WORKBOOK,
            view["sheet"],
            range=view["range"],
            active_cell=view["activeCell"],
            top_left_cell=view["topLeftCell"],
        )

        view_again = spreadsheet.get_active_view(WORKBOOK)
        assert view_again == view

    def test_set_active_view_multi_range_round_trips(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Multi"])
        spreadsheet.set_active_view(
            WORKBOOK,
            "Multi",
            ranges_json=json.dumps(["A7:B8", "A12:B13"]),
            active_cell="A7",
        )

        view = spreadsheet.get_active_view(WORKBOOK)
        assert view["sheet"] == "Multi"
        assert view["range"] == "A7:B8"
        assert view["ranges"] == ["A7:B8", "A12:B13"]
        assert view["activeCell"] == "A7"

        # Round-trip the ranges back through set_active_view.
        spreadsheet.set_active_view(
            WORKBOOK,
            view["sheet"],
            ranges_json=json.dumps(view["ranges"]),
            active_cell=view["activeCell"],
        )

        view_again = spreadsheet.get_active_view(WORKBOOK)
        assert view_again["ranges"] == ["A7:B8", "A12:B13"]
        assert view_again["activeCell"] == "A7"

    def test_get_active_view_single_range_includes_ranges_array(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Solo"])
        spreadsheet.set_active_view(
            WORKBOOK,
            "Solo",
            range="C5:D7",
        )

        view = spreadsheet.get_active_view(WORKBOOK)
        assert view["range"] == "C5:D7"
        assert view["ranges"] == ["C5:D7"]

    # spreadsheet_insert

    def test_insert_rows_shifts_existing_rows_down(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "row1\nrow2\nrow3\n"}],
        )
        result = spreadsheet.insert(
            WORKBOOK,
            [{"sheet": "Sheet1", "range": "2:3"}],
        )
        assert result["insertedRowCount"] == 2

        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1")["rows"]
        assert rows[0] == ["row1"]
        assert rows[3] == ["row2"]
        assert rows[4] == ["row3"]

    def test_insert_columns_shifts_existing_columns_right(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [
                {"cell": "A1", "value": "col1"},
                {"cell": "B1", "value": "col2"},
                {"cell": "C1", "value": "col3"},
            ],
        )
        result = spreadsheet.insert(
            WORKBOOK,
            [{"sheet": "Sheet1", "range": "B"}],
        )
        assert result["insertedColumnCount"] == 1

        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1")["rows"]
        # B1 is now blank; the original col2 has shifted to C1.
        assert rows[0][0] == "col1"
        assert rows[0][1] is None
        assert rows[0][2] == "col2"

    # spreadsheet_find

    def test_find_returns_matches_across_sheets(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Other"])
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": "Hello World"}],
        )
        spreadsheet.write_cells(
            WORKBOOK,
            "Other",
            [{"cell": "B5", "value": "Hello, friend"}],
        )

        result = spreadsheet.find(WORKBOOK, "Hello")
        assert result["matchCount"] == 2
        cells = sorted((m["sheet"], m["cell"]) for m in result["matches"])
        assert cells == [("Other", "B5"), ("Sheet1", "A1")]

    def test_find_match_entire_cell_contents_only(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [
                {"cell": "A1", "value": "foo"},
                {"cell": "A2", "value": "foobar"},
            ],
        )
        result = spreadsheet.find(
            WORKBOOK,
            "foo",
            sheet="Sheet1",
            match_entire_cell_contents=True,
        )
        assert result["matchCount"] == 1
        assert result["matches"][0]["cell"] == "A1"

    # spreadsheet_sort

    def test_sort_orders_rows_by_column(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "Charlie\nAlpha\nBravo\n"}],
        )
        result = spreadsheet.sort(
            WORKBOOK,
            "Sheet1",
            "A1:A3",
            [{"column": "A", "ascending": True}],
        )
        assert result["rowCount"] == 3

        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1")["rows"]
        assert [row[0] for row in rows] == ["Alpha", "Bravo", "Charlie"]

    def test_sort_with_header_row_keeps_header_in_place(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "Name\nCharlie\nAlpha\nBravo\n"}],
        )
        result = spreadsheet.sort(
            WORKBOOK,
            "Sheet1",
            "A1:A4",
            [{"column": "A", "ascending": True}],
            has_header_row=True,
        )
        assert result["rowCount"] == 3

        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1")["rows"]
        assert [row[0] for row in rows] == ["Name", "Alpha", "Bravo", "Charlie"]

    # spreadsheet_duplicate_sheet

    def test_duplicate_sheet_copies_values_and_appends(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": "header"}, {"cell": "A2", "value": 42}],
        )
        result = spreadsheet.duplicate_sheet(
            WORKBOOK,
            "Sheet1",
            "Sheet1Copy",
        )
        assert result["newSheet"] == "Sheet1Copy"
        assert result["position"] == 2

        info = spreadsheet.get_info(WORKBOOK)
        sheet_names = [s["name"] for s in info["sheets"]]
        assert sheet_names == ["Sheet1", "Sheet1Copy"]

        rows = spreadsheet.read_sheet(WORKBOOK, "Sheet1Copy")["rows"]
        assert rows[0][0] == "header"
        assert rows[1][0] == 42

    def test_duplicate_sheet_collision_fails(self, spreadsheet):
        spreadsheet.add_sheets(WORKBOOK, ["Existing"])
        with pytest.raises(CelError):
            spreadsheet.duplicate_sheet(
                WORKBOOK,
                "Sheet1",
                "Existing",
            )

    # spreadsheet_set_auto_filter

    def test_set_auto_filter_applies_to_used_range(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "name,total\nAlpha,1\nBravo,2\n"}],
        )
        result = spreadsheet.set_auto_filter(WORKBOOK, "Sheet1")
        assert result["enabled"]
        assert result["filterRange"] == "A1:B3"

    def test_set_auto_filter_disabled_clears_existing(self, spreadsheet):
        spreadsheet.import_csv(
            WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "name,total\nAlpha,1\n"}],
        )
        spreadsheet.set_auto_filter(WORKBOOK, "Sheet1")

        result = spreadsheet.set_auto_filter(
            WORKBOOK,
            "Sheet1",
            enabled=False,
        )
        assert not result["enabled"]
        assert result["filterRange"] == ""

    # spreadsheet_set_conditional_formatting

    def test_set_conditional_formatting_adds_greater_than_rule(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": 50}, {"cell": "A2", "value": 150}],
        )
        result = spreadsheet.set_conditional_formatting(
            WORKBOOK,
            "Sheet1",
            "A1:A2",
            [{"type": "greaterThan", "value": 100, "backgroundColor": "#FFCCCC"}],
        )
        assert result["rulesApplied"] == 1
        assert result["rulesRemoved"] == 0

    def test_set_conditional_formatting_clear_existing_replaces_rules(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": 50}],
        )
        spreadsheet.set_conditional_formatting(
            WORKBOOK,
            "Sheet1",
            "A1:A10",
            [{"type": "greaterThan", "value": 0, "backgroundColor": "#FFCCCC"}],
        )

        replace = spreadsheet.set_conditional_formatting(
            WORKBOOK,
            "Sheet1",
            "A1:A10",
            [{"type": "lessThan", "value": 10, "backgroundColor": "#CCFFCC"}],
            clear_existing=True,
        )
        assert replace["rulesApplied"] == 1
        assert replace["rulesRemoved"] == 1

    def test_set_conditional_formatting_top_rule(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [{"cell": f"A{row}", "value": row * 10} for row in range(1, 11)],
        )
        result = spreadsheet.set_conditional_formatting(
            WORKBOOK,
            "Sheet1",
            "A1:A10",
            [{"type": "top", "value": 3, "backgroundColor": "#CCFFCC"}],
        )
        assert result["rulesApplied"] == 1

    def test_set_conditional_formatting_top_rule_rejects_non_integer(self, spreadsheet):
        with pytest.raises(CelError):
            spreadsheet.set_conditional_formatting(
                WORKBOOK,
                "Sheet1",
                "A1:A10",
                [{"type": "top", "value": 2.5}],
            )

    def test_set_conditional_formatting_color_scale_custom_thresholds(self, spreadsheet):
        spreadsheet.write_cells(
            WORKBOOK,
            "Sheet1",
            [{"cell": f"A{row}", "value": row} for row in range(1, 11)],
        )
        result = spreadsheet.set_conditional_formatting(
            WORKBOOK,
            "Sheet1",
            "A1:A10",
            [
                {
                    "type": "colorScale3",
                    "lowColor": "#FF0000",
                    "midColor": "#FFFF00",
                    "highColor": "#00FF00",
                    "lowType": "number",
                    "lowValue": "0",
                    "midType": "percentile",
                    "midValue": "50",
                    "highType": "number",
                    "highValue": "10",
                }
            ],
        )
        assert result["rulesApplied"] == 1
