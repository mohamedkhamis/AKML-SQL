# Projects and dependencies analysis

This document provides a comprehensive overview of the projects and their dependencies in the context of upgrading to .NETCoreApp,Version=v11.0.

Detailed findings live alongside this file in `assessment/`. This page is the index: read it first, then open only the documents you need.

## Table of Contents

- [Executive Summary](#executive-summary)
  - [Highlevel Metrics](#highlevel-metrics)
  - [Projects Compatibility](#projects-compatibility)
  - [Package Compatibility](#package-compatibility)
  - [API Compatibility](#api-compatibility)
- [Top API Migration Challenges](#top-api-migration-challenges)
  - [Technologies and Features](#technologies-and-features)
  - [Most Frequent API Issues](#most-frequent-api-issues)
- [Detailed Reports](#detailed-reports)
  - [Projects Relationship Graph](assessment/project-graph.md)
  - [Aggregate NuGet packages details](assessment/nuget/aggregate-packages.md)
  - [Most Frequent API Issues (complete list)](assessment/api-issues/most-frequent-api-issues.md)
  - [Project Details](#project-details)

## Executive Summary

### Highlevel Metrics

| Metric | Count | Status |
| :--- | :---: | :--- |
| Total Projects | 27 | All require upgrade |
| Total NuGet Packages | 320 | 6 need upgrade |
| Total Code Files | 1933 |  |
| Total Code Files with Incidents | 724 |  |
| Total Lines of Code | 357232 |  |
| Total Number of Issues | 106121 |  |
| Proposed Target Framework | net11.0, net11.0-windows, netstandard2.0;net10.0;net11.0 |  |
| Estimated LOC to modify | 106074+ | at least 29.7% of codebase |

### Projects Compatibility

| Difficulty | Projects | Percentage |
| :--- | :---: | :---: |
| 🟡 Medium | 3 | 11.1% |
| 🟢 Low | 24 | 88.9% |
| ***Total Projects*** | ***27*** | ***100%*** |

🧪 **Test Coverage** — 9 of these projects are risky enough to add behavior-locking tests before upgrading, to catch regressions the upgrade may introduce. Requires the **dotnet-test** plugin. The per-project breakdown is in [the project inventory](assessment/projects/index.md).

This repository has 27 projects. The per-project compatibility table is in [the project inventory](assessment/projects/index.md).

### Package Compatibility

| Status | Count | Percentage |
| :--- | :---: | :---: |
| ✅ Compatible | 314 | 98.1% |
| ⚠️ Incompatible | 4 | 1.2% |
| 🔄 Upgrade Recommended | 2 | 0.6% |
| ***Total NuGet Packages*** | ***320*** | ***100%*** |

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 103715 | High - Require code changes |
| 🟡 Source Incompatible | 2098 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 261 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 332917 |  |
| ***Total APIs Analyzed*** | ***438991*** |  |

## Top API Migration Challenges

### Technologies and Features

| Technology | Issues | Percentage | Migration Path |
| :--- | :---: | :---: | :--- |
| WPF (Windows Presentation Foundation) | 47565 | 44.8% | WPF APIs for building Windows desktop applications with XAML-based UI that are available in .NET on Windows. WPF provides rich desktop UI capabilities with data binding and styling. Enable Windows Desktop support: Option 1 (Recommended): Target net9.0-windows; Option 2: Add <UseWindowsDesktop>true</UseWindowsDesktop>. |
| Windows Forms | 23959 | 22.6% | Windows Forms APIs for building Windows desktop applications with traditional Forms-based UI that are available in .NET on Windows. Enable Windows Desktop support: Option 1 (Recommended): Target net9.0-windows; Option 2: Add <UseWindowsDesktop>true</UseWindowsDesktop>; Option 3 (Legacy): Use Microsoft.NET.Sdk.WindowsDesktop SDK. |
| Windows Forms Legacy Controls | 4096 | 3.9% | Legacy Windows Forms controls that have been removed from .NET Core/5+ including StatusBar, DataGrid, ContextMenu, MainMenu, MenuItem, and ToolBar. These controls were replaced by more modern alternatives. Use ToolStrip, MenuStrip, ContextMenuStrip, and DataGridView instead. |
| GDI+ / System.Drawing | 1632 | 1.5% | System.Drawing APIs for 2D graphics, imaging, and printing that are available via NuGet package System.Drawing.Common. Note: Not recommended for server scenarios due to Windows dependencies; consider cross-platform alternatives like SkiaSharp or ImageSharp for new code. |

### Most Frequent API Issues

| API | Count | Percentage | Category |
| :--- | :---: | :---: | :--- |
| T:System.Windows.Thickness | 3241 | 3.1% | Binary Incompatible |
| T:System.Windows.Media.SolidColorBrush | 2660 | 2.5% | Binary Incompatible |
| T:System.Windows.DependencyProperty | 2610 | 2.5% | Binary Incompatible |
| T:System.Windows.Controls.TextBlock | 2584 | 2.4% | Binary Incompatible |
| T:System.Windows.Controls.CheckBox | 2387 | 2.3% | Binary Incompatible |
| P:System.Windows.Controls.Panel.Children | 1536 | 1.4% | Binary Incompatible |
| T:System.Windows.Controls.UIElementCollection | 1536 | 1.4% | Binary Incompatible |
| M:System.Windows.Controls.UIElementCollection.Add(System.Windows.UIElement) | 1482 | 1.4% | Binary Incompatible |
| T:System.Windows.VerticalAlignment | 1299 | 1.2% | Binary Incompatible |
| T:System.Windows.Controls.TextBox | 1259 | 1.2% | Binary Incompatible |

The table above is the top 10. See [the complete list](assessment/api-issues/most-frequent-api-issues.md) for every affected API.

## Detailed Reports

- [Projects Relationship Graph](assessment/project-graph.md)
- [Aggregate NuGet packages details](assessment/nuget/aggregate-packages.md)
- [Most Frequent API Issues (complete list)](assessment/api-issues/most-frequent-api-issues.md)

### Project Details

- [All 27 projects](assessment/projects/index.md)

