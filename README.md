# Find / Trade

> [!WARNING]
> **AI-assisted project notice**
> This fork was developed with the help of AI tooling: **Gemini**, driven through **Google Antigravity**.
> Code, tests, translations, specifications and this document may be AI-generated. Review the content and test it in your own environment before relying on it.

A [RimWorld](https://rimworldgame.com/) mod that finds items and trades them automatically. It combines the item filters of TD Find Library with trade rules (Export, Import, Maintain), colonist barter AI, and assisted manual trading.

| Item | Details |
| :--- | :--- |
| Package ID | `Memegoddess.FindAndTrade` |
| Supported RimWorld versions | 1.6, 1.7 |
| Dependencies | [Harmony](https://github.com/pardeike/HarmonyRimWorld/releases/latest) (`brrainz.harmony`), [TD Find Library](https://github.com/MemeGoddess/RimWorld-TDFindLib) (`Memegoddess.TDFindLib`) |

## Features

- **Trade rules**: Each rule pairs a search filter with a mode.
  - *Export*: sell matching items down to a threshold.
  - *Import*: buy matching items up to a target stock.
  - *Maintain*: keep stock within `[Import, Export]`.
- **Aggregation**: Thresholds apply per `ThingDef` or to the total of everything a rule matches.
- **Junk designation**: Mark items to be sold unconditionally.
- **Autonomous barter**: Colonists with the Warden work type trade with caravans and orbital ships.
- **Assisted manual trading**: The vanilla trade dialog is pre-filled according to your rules, and each row is tagged with the rule that caused it.
- **Presets**: Save, load (replace or append), delete, and copy or paste rule sets through the clipboard.

For the full specification, see [`.agents/memories/spec.md`](.agents/memories/spec.md).

## Differences from the upstream fork source

This repository is a fork of [MemeGoddess/FindAndTrade](https://github.com/MemeGoddess/FindAndTrade).
The comparison baseline is commit [`b96fdc1`](https://github.com/MemeGoddess/FindAndTrade/commit/b96fdc1bda0fdca09795d4348bbe95eed9aa6951) and earlier, which holds the upstream state. Everything after it is specific to this fork.

| Aspect | Upstream (up to `b96fdc1`) | This fork |
| :--- | :--- | :--- |
| Design approach | Feature-driven implementation, with trade logic concentrated in large classes such as `TradeDealProcessor` and `CacheUtility`. | Testability-driven design. Trade logic is split into single-responsibility components behind interfaces (SOLID). |
| Trade processing | Budget balancing, inventory aggregation and order calculation live inside `TradeDealProcessor`. | Extracted into `Source/Processing`: `ITradeBudgetNormalizer`, `ITradeInventoryAggregator`, and `ITradeOrderCalculator`, each with an implementation. |
| Caravan routes | Route logic is coupled to `SettlementTracker` and `InProgressRoute`. | Route calculation sits behind `ITradeRouteCalculator`. |
| Presets | Not available. | Import and export of rule presets (`Source/Presets`): XML files in `SaveData`, clipboard transfer, and a management dialog. Pawn and trader references are never stored, so presets work across saves. |
| Statistics | None. | `StatisticsUtility` for statistical helpers. |
| Tests | None. | Unit-test project `Tests/` (`MGAutoSell.Tests.csproj`) covering rules, price ranges, cache, caravans and settlements, extensions, trade deals, queries and presets. |
| Documentation | None. | Specification in `.agents/memories`, plus AI agent rules in `.agents/skills` (general, documentation, git commit messages and pull requests). |
| Localization | English, Simplified Chinese, Traditional Chinese. | Adds Japanese, and extends the English and Chinese strings for the preset UI. |

> [!NOTE]
> Per-commit details are in the Git history. Use `git diff b96fdc1 HEAD` to see every change this fork made.

## Building and testing

The sources are in `Source/` (`MGAutoSell.csproj`), and the solution file is `AutoSell.sln`. Run the unit tests with:

```powershell
dotnet test Tests/MGAutoSell.Tests.csproj
```

## Authors and credits

| Name | Role |
| :--- | :--- |
| **Hiroaki Yamamoto** | Maintainer of this fork. Directs the AI-assisted development. |
| **Gemini** | AI model (via Google Antigravity) that wrote code, tests and documentation for this fork. |
| **MemeGoddess** | Original author of Find / Trade, the upstream of this fork: [MemeGoddess/FindAndTrade](https://github.com/MemeGoddess/FindAndTrade) |

Translation contributors in the upstream history include HawnHan (Simplified Chinese), Zhuma5 (bug fix), and iF (Traditional Chinese).
