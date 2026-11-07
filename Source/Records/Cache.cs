using System;
using System.Collections.Generic;
using System.Text;
using MGAutoSell.Filter;
using UnityEngine;
using Verse;

namespace MGAutoSell.Records;

/// <summary>
/// Immutable snapshot of all sellable items, potential buy items, expected total silver, active negotiator,
/// and per-rule usage counts for a map.
/// </summary>
/// <param name="Items">Sorted list of items queued for sale.</param>
/// <param name="PotentialItems">List of potential buyable items matched by import/maintain rules.</param>
/// <param name="TotalSilver">Total expected silver value and formatted label.</param>
/// <param name="Trader">Negotiator record used for price calculations.</param>
/// <param name="Rules">Dictionary mapping each active <see cref="TradeRule"/> to its <see cref="RuleUsage"/> summary.</param>
public record ItemsToSell(
    List<SellRecord> Items,
    List<PotentialItem> PotentialItems,
    ItemAndLabel<float> TotalSilver,
    TraderRecord Trader,
    Dictionary<TradeRule, RuleUsage> Rules);

/// <summary>
/// Represents a single <see cref="ThingDef"/> entry in the sell preview list along with its quantity, total value, and unit price.
/// </summary>
/// <param name="Item">The <see cref="ThingDef"/> being sold.</param>
/// <param name="Count">The total quantity to be sold.</param>
/// <param name="Total">Total silver value and formatted label.</param>
/// <param name="Price">Unit silver price and formatted label.</param>
public record SellRecord(ThingDef Item, int Count, ItemAndLabel<float> Total, ItemAndLabel<float> Price);

/// <summary>
/// Represents a colonist capable of acting as a trade negotiator along with their social trade improvement bonus.
/// </summary>
/// <param name="Pawn">The colonist <see cref="Verse.Pawn"/>.</param>
/// <param name="Name">Short display name of the colonist.</param>
/// <param name="Icon">Lazy delegate returning the colonist's portrait texture.</param>
/// <param name="ImprovementLabel">Formatted percentage string for trade price improvement.</param>
/// <param name="Improvement">Raw trade price improvement stat value.</param>
/// <param name="IsLeader">Indicates whether the colonist holds the faction leader role.</param>
public record TraderRecord(
    Pawn Pawn,
    string Name,
    Func<Texture> Icon,
    string ImprovementLabel,
    float Improvement,
    bool IsLeader);

/// <summary>
/// Stores the aggregated stack count for a single <see cref="ThingDef"/> matched by a <see cref="TradeRule"/>.
/// </summary>
/// <param name="Item">The matched <see cref="ThingDef"/>.</param>
/// <param name="Count">Total stack count on the map.</param>
public record RuleRecord(ThingDef Item, int Count);

/// <summary>
/// Represents a <see cref="ThingDef"/> that could potentially be purchased by an active buy rule.
/// </summary>
/// <param name="Item">The potential <see cref="ThingDef"/>.</param>
/// <param name="Rule">Formatted label of the rule that matches the item.</param>
public record PotentialItem(ThingDef Item, string Rule);

/// <summary>
/// Associates a strongly typed value with its pre-formatted UI display string to avoid repeated string allocations during rendering.
/// </summary>
/// <typeparam name="T">The type of the underlying value.</typeparam>
/// <param name="Value">The raw value.</param>
/// <param name="Label">The pre-formatted display string.</param>
public record ItemAndLabel<T>(T Value, string Label);

/// <summary>
/// Summarizes the matched items and quantity range (<c>min</c> to <c>max</c>) for a <see cref="TradeRule"/>.
/// </summary>
public record RuleUsage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RuleUsage"/> record and pre-formats <see cref="ItemsList"/>.
    /// </summary>
    /// <param name="Items">The list of per-<see cref="ThingDef"/> counts.</param>
    /// <param name="Range">The minimum and maximum counts and their display labels.</param>
    public RuleUsage(List<RuleRecord> Items, (ItemAndLabel<int> min, ItemAndLabel<int> max) Range)
    {
        this.Items = Items ?? [];
        this.Range = Range;
        ItemsList = FormatItemsList(this.Items);
    }

    /// <summary>
    /// Gets the list of matched <see cref="RuleRecord"/> entries.
    /// </summary>
    public List<RuleRecord> Items { get; init; }

    /// <summary>
    /// Gets the minimum and maximum item counts and their formatted labels.
    /// </summary>
    public (ItemAndLabel<int> min, ItemAndLabel<int> max) Range { get; init; }

    /// <summary>
    /// Gets the pre-formatted bulleted list of matched items and quantities for tooltips.
    /// </summary>
    public string ItemsList { get; init; }

    /// <summary>
    /// Deconstructs this <see cref="RuleUsage"/> into its component <paramref name="Items"/> and <paramref name="Range"/>.
    /// </summary>
    /// <param name="Items">Receives <see cref="Items"/>.</param>
    /// <param name="Range">Receives <see cref="Range"/>.</param>
    public void Deconstruct(out List<RuleRecord> Items, out (ItemAndLabel<int> min, ItemAndLabel<int> max) Range)
    {
        Items = this.Items;
        Range = this.Range;
    }

    private static string FormatItemsList(List<RuleRecord> items)
    {
        if (items == null || items.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            var entry = items[i];
            string itemLabel = entry.Item != null
                ? (!string.IsNullOrEmpty(entry.Item.label) ? entry.Item.LabelCap.ToString() : entry.Item.defName ?? string.Empty)
                : string.Empty;
            sb.Append("- ").Append(itemLabel).Append(" x").Append(entry.Count);
        }

        return sb.ToString();
    }
}
