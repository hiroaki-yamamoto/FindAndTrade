---
id: setting_save_spec
title: Setting Save/Load Specification
content: The specification for saving and loading settings in this mod.
---

# Import/Export Specifications

- The import/export feature must be structured so that configurations can be
  shared across different save data.
- For the data format used by the import/export feature, adopt RimWorld's
  standard serialization format if one exists; if not, any format is acceptable.
- The import/export feature must allow saving and deleting multiple configuration presets.
- Access to the import/export feature must be available directly through this mod's in-game UI.

# Import/Export Targets

- Trade rule groups and all of their contents
- Trade quantities and their conditions
- Whether rules are enabled or disabled

> [!IMPORTANT]
> This mod does not import or export assigned traders.
>
> **Reason**: Trader information depends on the specific save data and cannot be migrated to other save files.
