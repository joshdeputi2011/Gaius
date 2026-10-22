"""Read the optional native-test XLSX samples with an independent OOXML library."""
import sys
from pathlib import Path

import openpyxl
from openpyxl.chart.area_chart import AreaChart
from openpyxl.chart.bar_chart import BarChart
from openpyxl.chart.line_chart import LineChart
from openpyxl.chart.pie_chart import DoughnutChart, PieChart
from openpyxl.chart.scatter_chart import ScatterChart

folder = Path(sys.argv[1])
for name in ("all-chart-types.xlsx", "preserved-chart-types.xlsx"):
    sheet = openpyxl.load_workbook(folder / name).active
    preserved = name.startswith("preserved")
    assert len(sheet._charts) == 9
    assert abs(sheet.column_dimensions["A"].width * 7 + 5 - (260 if preserved else 218)) < 1
    assert sheet.row_dimensions[2].height == (54 if preserved else 48.75)
    assert sheet["B2"].value == (99 if preserved else 24)
    assert sheet["B2"].alignment.wrap_text
    assert sheet["D4"].fill.fgColor.rgb.upper() == "FFFFF0AA"
    expected = (BarChart, BarChart, LineChart, AreaChart, ScatterChart, PieChart, DoughnutChart, BarChart, BarChart)
    assert all(isinstance(chart, kind) for chart, kind in zip(sheet._charts, expected))
    assert sheet._charts[0].type == "col" and sheet._charts[1].type == "bar"
    assert sheet._charts[7].grouping == "stacked" and sheet._charts[8].grouping == "stacked"
    assert len(sheet._charts[0].ser) == 2
    print(f"PASS: {name}: nine editable charts, dimensions and styles read by openpyxl")
