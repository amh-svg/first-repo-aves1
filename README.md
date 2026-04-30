# AVES Revit Plugin

A productivity add-in for Autodesk Revit that automates common family management, sheet creation, and tagging workflows.

---

## Installation

1. Download the latest installer from the [Releases](https://github.com/amh-svg/first-repo-aves1/releases/tag/v1.0.0) page
2. Run `AVES_Setup_vX.X.X.exe`
3. The installer will automatically detect your Revit version and install the plugin
4. Restart Revit — the **AVES** tab will appear in the ribbon

**Supported Revit versions:** 2025

---

## Ribbon Overview

### 🗂️ Families

| Button | Description |
|---|---|
| **Add Shared Parameter** | Adds a shared parameter to selected families |
| **Remove Shared Parameters** | Removes shared parameters from selected families |
| **Fill Family Parameters** | Populates parameter values on selected family instances |
| **Rename Families Parameters** | Renames families and types based on the `Item Number` and `PMO_Description` parameters. Target format: `{Item Number} _ {PMO_Description}` |

---

### 📄 Sheet Creation

| Button | Description |
|---|---|
| **Assembly Drawings** | Automatically creates sheets for assembly drawings |
| **Vendor Drawings** | Automatically creates sheets for vendor drawings |
| **MTQ Drawings** | Automatically creates sheets for MTQ drawings |

---

### 🏷️ Tags

| Button | Description |
|---|---|
| **Tag 3D View MTQ** | Tags elements in 3D views for MTQ workflows |
| **2D Dimensions MTQ** | Places 2D dimensions on MTQ sheets |

---

## Requirements

- Autodesk Revit 2022–2025
- Windows 10 or later
- .NET 8.0 or later

## License

© AVES. All rights reserved.
