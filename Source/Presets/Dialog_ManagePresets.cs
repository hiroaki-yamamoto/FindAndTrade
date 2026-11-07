using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TD_Find_Lib;
using UnityEngine;
using Verse;

namespace MGAutoSell.Presets
{
    /// <summary>
    /// Modal dialog for managing trade rule presets (saving, loading, appending, deleting, and clipboard sync).
    /// </summary>
    public class Dialog_ManagePresets : Window
    {
        private readonly TradeRulesGameComp comp;
        private readonly Action onModified;

        private List<TradeRulePresetHeader> presetHeaders = new();
        private Vector2 scrollPosition = Vector2.zero;
        private string newPresetName = "";

        public override Vector2 InitialSize => new(660f, 520f);

        public Dialog_ManagePresets(TradeRulesGameComp comp, Action onModified = null)
        {
            this.comp = comp;
            this.onModified = onModified;
            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            RefreshPresets();
        }

        private void RefreshPresets()
        {
            presetHeaders = TradeRulePresetStorage.GetPresetHeaders();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            var titleRect = inRect.TopPartPixels(30f);
            Widgets.Label(titleRect, "MGAutoSell.ManagePresetsTitle".Translate());
            Text.Font = GameFont.Small;

            // Open Folder button on top-right
            var openFolderRect = new Rect(titleRect.xMax - 140f, titleRect.y, 140f, 26f);
            if (Widgets.ButtonText(openFolderRect, "MGAutoSell.OpenFolder".Translate()))
            {
                Application.OpenURL(TradeRulePresetStorage.PresetDirectory);
            }

            // Save new preset bar
            var saveBarRect = new Rect(inRect.x, titleRect.yMax + 10f, inRect.width, 30f);
            DrawSaveBar(saveBarRect);

            // Preset list scroll view
            var listRect = new Rect(inRect.x, saveBarRect.yMax + 12f, inRect.width, inRect.height - saveBarRect.yMax - 54f);
            DrawPresetList(listRect);

            // Bottom bar (Clipboard actions & Close button)
            var bottomRect = new Rect(inRect.x, inRect.yMax - 32f, inRect.width, 32f);
            DrawBottomBar(bottomRect);
        }

        private void DrawSaveBar(Rect rect)
        {
            var labelWidth = 90f;
            var buttonWidth = 160f;
            var inputWidth = rect.width - labelWidth - buttonWidth - 16f;

            var labelRect = new Rect(rect.x, rect.y + 4f, labelWidth, 24f);
            Widgets.Label(labelRect, "MGAutoSell.PresetName".Translate() + ":");

            var inputRect = new Rect(labelRect.xMax + 8f, rect.y, inputWidth, 28f);
            newPresetName = Widgets.TextField(inputRect, newPresetName);

            var buttonRect = new Rect(inputRect.xMax + 8f, rect.y, buttonWidth, 28f);
            if (Widgets.ButtonText(buttonRect, "MGAutoSell.SaveCurrentAsPreset".Translate()))
            {
                SavePresetAction(newPresetName);
            }
        }

        private void SavePresetAction(string name)
        {
            if (name.NullOrEmpty())
            {
                Messages.Message("MGAutoSell.PresetNameEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (TradeRulePresetStorage.PresetExists(name))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "MGAutoSell.PresetOverwriteConfirm".Translate(name),
                    () => PerformSave(name),
                    destructive: true
                ));
            }
            else
            {
                PerformSave(name);
            }
        }

        private void PerformSave(string name)
        {
            if (TradeRulePresetStorage.SavePreset(name, comp.tradeRules, out var error))
            {
                Messages.Message("MGAutoSell.PresetSaved".Translate(name), MessageTypeDefOf.PositiveEvent, false);
                newPresetName = "";
                RefreshPresets();
            }
            else
            {
                Messages.Message("MGAutoSell.PresetSaveFailed".Translate(error ?? ""), MessageTypeDefOf.RejectInput, false);
            }
        }

        private void DrawPresetList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var innerRect = rect.ContractedBy(4f);

            if (!presetHeaders.Any())
            {
                var noPresetsRect = new Rect(innerRect.x, innerRect.y + 20f, innerRect.width, 30f);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(noPresetsRect, "MGAutoSell.NoPresetsFound".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            var rowHeight = 34f;
            var totalHeight = presetHeaders.Count * rowHeight;
            var viewRect = new Rect(0, 0, innerRect.width - (totalHeight > innerRect.height ? 16f : 0f), totalHeight);

            Widgets.BeginScrollView(innerRect, ref scrollPosition, viewRect);

            for (int i = 0; i < presetHeaders.Count; i++)
            {
                var header = presetHeaders[i];
                var rowRect = new Rect(0, i * rowHeight, viewRect.width, rowHeight);

                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(rowRect);

                var contentRect = rowRect.ContractedBy(2f);

                // Preset name & modified date
                var dateStr = header.LastWriteTime != DateTime.MinValue ? header.LastWriteTime.ToString("yyyy-MM-dd HH:mm") : "";
                var nameRect = new Rect(contentRect.x + 4f, contentRect.y + 5f, contentRect.width - 250f, contentRect.height);
                Widgets.Label(nameRect, header.Name);

                var dateRect = new Rect(contentRect.x + contentRect.width - 360f, contentRect.y + 6f, 100f, contentRect.height);
                GUI.color = Color.gray;
                Widgets.Label(dateRect, dateStr);
                GUI.color = Color.white;

                // Action buttons
                var btnReplaceRect = new Rect(contentRect.xMax - 250f, contentRect.y + 2f, 95f, 26f);
                if (Widgets.ButtonText(btnReplaceRect, "MGAutoSell.LoadOverwrite".Translate()))
                {
                    ConfirmLoad(header, overwrite: true);
                }
                TooltipHandler.TipRegion(btnReplaceRect, "MGAutoSell.LoadOverwriteTooltip".Translate());

                var btnAppendRect = new Rect(btnReplaceRect.xMax + 6f, contentRect.y + 2f, 95f, 26f);
                if (Widgets.ButtonText(btnAppendRect, "MGAutoSell.LoadAppend".Translate()))
                {
                    ConfirmLoad(header, overwrite: false);
                }
                TooltipHandler.TipRegion(btnAppendRect, "MGAutoSell.LoadAppendTooltip".Translate());

                var btnDeleteRect = new Rect(btnAppendRect.xMax + 6f, contentRect.y + 2f, 26f, 26f);
                if (Widgets.ButtonImage(btnDeleteRect, FindTex.Trash))
                {
                    ConfirmDelete(header);
                }
                TooltipHandler.TipRegion(btnDeleteRect, "MGAutoSell.DeletePresetTooltip".Translate());
            }

            Widgets.EndScrollView();
        }

        private void ConfirmLoad(TradeRulePresetHeader header, bool overwrite)
        {
            if (overwrite && comp.tradeRules.Count > 0)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "MGAutoSell.PresetLoadOverwriteConfirm".Translate(header.Name),
                    () => PerformLoad(header, overwrite: true),
                    destructive: true
                ));
            }
            else
            {
                PerformLoad(header, overwrite);
            }
        }

        private void PerformLoad(TradeRulePresetHeader header, bool overwrite)
        {
            var preset = TradeRulePresetStorage.LoadPreset(header.FilePath, out var error);
            if (preset != null)
            {
                TradeRulePresetStorage.ApplyPreset(comp, preset, overwrite);
                onModified?.Invoke();
                Messages.Message("MGAutoSell.PresetLoaded".Translate(header.Name, preset.rules.Count), MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                Messages.Message("MGAutoSell.PresetLoadFailed".Translate(error ?? ""), MessageTypeDefOf.RejectInput, false);
            }
        }

        private void ConfirmDelete(TradeRulePresetHeader header)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "MGAutoSell.PresetDeleteConfirm".Translate(header.Name),
                () =>
                {
                    if (TradeRulePresetStorage.DeletePreset(header.FilePath, out var error))
                    {
                        Messages.Message("MGAutoSell.PresetDeleted".Translate(header.Name), MessageTypeDefOf.TaskCompletion, false);
                        RefreshPresets();
                    }
                    else
                    {
                        Messages.Message("MGAutoSell.PresetDeleteFailed".Translate(error ?? ""), MessageTypeDefOf.RejectInput, false);
                    }
                },
                destructive: true
            ));
        }

        private void DrawBottomBar(Rect rect)
        {
            var btnWidth = 150f;
            var curX = rect.x;

            if (Widgets.ButtonText(new Rect(curX, rect.y, btnWidth, rect.height), "MGAutoSell.ExportToClipboard".Translate()))
            {
                if (TradeRulePresetStorage.CopyToClipboard("Clipboard", comp.tradeRules))
                {
                    Messages.Message("MGAutoSell.ExportedToClipboard".Translate(), MessageTypeDefOf.TaskCompletion, false);
                }
            }
            curX += btnWidth + 10f;

            if (Widgets.ButtonText(new Rect(curX, rect.y, btnWidth, rect.height), "MGAutoSell.ImportFromClipboard".Translate()))
            {
                var preset = TradeRulePresetStorage.PasteFromClipboard(out var error);
                if (preset != null)
                {
                    var pName = preset.name.NullOrEmpty() ? "Clipboard" : preset.name;
                    if (comp.tradeRules.Count > 0)
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "MGAutoSell.PresetLoadOverwriteConfirm".Translate(pName),
                            () =>
                            {
                                TradeRulePresetStorage.ApplyPreset(comp, preset, overwrite: true);
                                onModified?.Invoke();
                                Messages.Message("MGAutoSell.PresetLoaded".Translate(pName, preset.rules.Count), MessageTypeDefOf.PositiveEvent, false);
                            },
                            destructive: true
                        ));
                    }
                    else
                    {
                        TradeRulePresetStorage.ApplyPreset(comp, preset, overwrite: true);
                        onModified?.Invoke();
                        Messages.Message("MGAutoSell.PresetLoaded".Translate(pName, preset.rules.Count), MessageTypeDefOf.PositiveEvent, false);
                    }
                }
                else
                {
                    Messages.Message("MGAutoSell.ClipboardEmptyOrInvalid".Translate(error ?? ""), MessageTypeDefOf.RejectInput, false);
                }
            }

            var closeRect = new Rect(rect.xMax - 100f, rect.y, 100f, rect.height);
            if (Widgets.ButtonText(closeRect, "Close".Translate()))
            {
                Close();
            }
        }
    }
}
