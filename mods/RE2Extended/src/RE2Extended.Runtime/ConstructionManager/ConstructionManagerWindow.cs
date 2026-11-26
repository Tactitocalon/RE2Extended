using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using UnityEngine;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal enum ConstructionManagerCategory
    {
        Food,
        Medicine,
        WeaponsItems,
        BuildingBlocks,
        Components,
        Devices,
        DecorationBlocks
    }

    internal enum ConstructionManagerGridType
    {
        All,
        BA,
        CV,
        SV,
        HV
    }

    internal sealed class ConstructionManagerTemplate
    {
        internal readonly int ItemType;
        internal readonly string InternalName;
        internal readonly ConstructionManagerItemDefinition Definition;

        private bool classified;
        private ConstructionManagerCategory category;
        private bool placeable;
        private int gridMask;

        internal ConstructionManagerTemplate(int itemType, string internalName,
            ConstructionManagerItemDefinition definition)
        {
            ItemType = itemType;
            InternalName = internalName;
            Definition = definition;
        }

        internal string DisplayName
        {
            get { return Definition == null ? InternalName : Definition.DisplayName; }
        }

        internal string Description
        {
            get { return Definition == null ? "No description available." : Definition.Description; }
        }

        internal string IconName
        {
            get { return Definition == null ? InternalName : Definition.IconName; }
        }

        internal ConstructionManagerIcon Icon
        {
            get { return Definition == null ? null : Definition.Icon; }
        }

        internal ConstructionManagerCategory Category
        {
            get
            {
                Classify();
                return category;
            }
        }

        internal bool MatchesGrid(ConstructionManagerGridType grid)
        {
            Classify();
            if (grid == ConstructionManagerGridType.All) return true;
            if (!placeable || gridMask == 0) return true;

            int bit = GridBit(grid);
            return bit != 0 && (gridMask & bit) != 0;
        }

        private void Classify()
        {
            if (classified) return;
            classified = true;

            string raw = Definition == null ? null : Definition.Category;
            string value = (raw ?? string.Empty).Trim();
            if (Contains(value, "Food") || Contains(value, "Ingredient"))
                category = ConstructionManagerCategory.Food;
            else if (Contains(value, "Medical") || Contains(value, "Medicine"))
                category = ConstructionManagerCategory.Medicine;
            else if (Contains(value, "Weapon") || Contains(value, "Items"))
                category = ConstructionManagerCategory.WeaponsItems;
            else if (Contains(value, "Building"))
                category = ConstructionManagerCategory.BuildingBlocks;
            else if (Contains(value, "Component"))
                category = ConstructionManagerCategory.Components;
            else if (Contains(value, "Deco"))
                category = ConstructionManagerCategory.DecorationBlocks;
            else if (Contains(value, "Device") || Contains(value, "Farming") ||
                     value.StartsWith("cpg", StringComparison.OrdinalIgnoreCase))
                category = ConstructionManagerCategory.Devices;
            else
                category = ConstructionManagerCategory.Components;

            int nativeMask = Definition == null ? -1 : Definition.GridMask;
            if (nativeMask < 0) return;
            placeable = true;
            gridMask = nativeMask;
        }

        private static int GridBit(ConstructionManagerGridType grid)
        {
            switch (grid)
            {
                case ConstructionManagerGridType.BA: return 1;
                case ConstructionManagerGridType.CV: return 2;
                case ConstructionManagerGridType.SV: return 4;
                case ConstructionManagerGridType.HV: return 8;
                default: return 0;
            }
        }

        private static bool Contains(string value, string part)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class ConstructionManagerWindow
    {
        private const int WindowId = 0x434D;
        private const int TemplateResultLimit = 800;

        private readonly IModApi api;
        private readonly ConstructionManagerItemCatalog itemCatalog;
        private readonly ConstructionManagerRecipeCatalog recipeCatalog;
        private readonly ConstructionManagerSession session = new ConstructionManagerSession();
        private readonly List<ConstructionManagerTemplate> templates =
            new List<ConstructionManagerTemplate>();

        private ConstructionJob selectedJob;
        private string templateSearch = string.Empty;
        private string status = "Phase 1 UI spike: jobs are in-memory only.";
        private Rect windowRect = new Rect(40f, 40f, 1120f, 660f);
        private bool rectInitialized;
        private bool templatesLoaded;
        private Vector2 jobScroll;
        private Vector2 queueScroll;
        private Vector2 templateScroll;
        private string hoveredName;
        private string hoveredDescription;
        private ConstructionManagerCategory selectedCategory = ConstructionManagerCategory.Components;
        private ConstructionManagerGridType selectedGrid = ConstructionManagerGridType.All;

        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle headerStyle;
        private GUIStyle windowStyle;
        private GUIStyle boxStyle;
        private GUIStyle buttonStyle;
        private GUIStyle selectedButtonStyle;
        private GUIStyle cellStyle;
        private GUIStyle textFieldStyle;
        private GUIStyle iconFallbackStyle;
        private GUIStyle queueNameStyle;
        private GUIStyle tooltipBoxStyle;
        private GUIStyle tooltipTitleStyle;
        private GUIStyle tooltipTextStyle;
        private bool stylesReady;

        internal Action CloseRequested;

        internal ConstructionManagerWindow(IModApi api, Assembly game)
        {
            this.api = api;

            string failure;
            itemCatalog = ConstructionManagerItemCatalog.TryCreate(game, api, out failure);
            if (itemCatalog == null)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Item icons/descriptions unavailable: " + (failure ?? "catalog unavailable") + ".");
            }

            recipeCatalog = ConstructionManagerRecipeCatalog.TryCreate(game, api, out failure);
            if (recipeCatalog == null)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Constructor recipe filter unavailable; template list may include uncraftable entries: " +
                    (failure ?? "recipe catalog unavailable") + ".");
            }
        }

        internal bool Visible { get; private set; }

        internal void Open()
        {
            Visible = true;
            EnsureTemplates();
            if (selectedJob == null && session.Jobs.Count > 0)
                selectedJob = session.Jobs[0];
        }

        internal void Close()
        {
            Visible = false;
        }

        internal void Draw()
        {
            if (!Visible) return;

            try
            {
                EnsureStyles();
                if (!rectInitialized)
                {
                    rectInitialized = true;
                    float width = Math.Min(Screen.width, Math.Max(640f, Screen.width * 0.90f));
                    float height = Math.Min(Screen.height, Math.Max(480f, Screen.height * 0.90f));
                    windowRect = new Rect((Screen.width - width) * 0.5f,
                        (Screen.height - height) * 0.5f, width, height);
                }

                GUI.depth = -1000;
                windowRect = GUI.Window(WindowId, windowRect, DrawWindowContents,
                    "Construction Manager", windowStyle);
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Window draw failed: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void DrawWindowContents(int id)
        {
            hoveredName = null;
            hoveredDescription = null;

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Job Manager prototype. Data is not persisted and Start does not distribute items yet.",
                smallStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(76), GUILayout.Height(26)))
            {
                Action close = CloseRequested;
                if (close != null) close();
                else Close();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawJobPane();
            DrawQueuePane();
            DrawTemplatePane();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label(status ?? string.Empty, boxStyle, GUILayout.Height(28));
            DrawTooltip();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 90f, 24f));
        }

        private void DrawJobPane()
        {
            GUILayout.BeginVertical(boxStyle, GUILayout.Width(270f), GUILayout.ExpandHeight(true));
            GUILayout.Label("Jobs", headerStyle);
            jobScroll = GUILayout.BeginScrollView(jobScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < session.Jobs.Count; i++)
            {
                ConstructionJob job = session.Jobs[i];
                string label = job.Name + "   |   " + job.Type + "   |   " + job.StatusText;
                GUIStyle style = ReferenceEquals(job, selectedJob) ? selectedButtonStyle : buttonStyle;
                if (GUILayout.Button(label, style, GUILayout.Height(28)))
                    selectedJob = job;
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New Job", buttonStyle, GUILayout.Height(26)))
            {
                selectedJob = session.CreateJob();
                status = "Created " + selectedJob.Name + " (Pending).";
            }
            if (GUILayout.Button("Cancel Job", buttonStyle, GUILayout.Height(26)))
                CancelSelectedJob();
            GUILayout.EndHorizontal();

            if (selectedJob != null)
            {
                GUILayout.Space(6);
                GUILayout.Label("Selected job name", labelStyle);
                string newName = GUILayout.TextField(selectedJob.Name ?? string.Empty, 40, textFieldStyle);
                if (!string.Equals(newName, selectedJob.Name, StringComparison.Ordinal))
                    selectedJob.Name = string.IsNullOrEmpty(newName) ? selectedJob.Name : newName;

                GUILayout.Label("Type: Once", smallStyle);
                GUILayout.Label("Status: " + selectedJob.StatusText, smallStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawQueuePane()
        {
            GUILayout.BeginVertical(boxStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Label("Job Queue (inputs/recipes only)", headerStyle);

            if (selectedJob == null)
            {
                GUILayout.Label("Select or create a job.", labelStyle);
            }
            else
            {
                GUILayout.Label(selectedJob.Name + "  [" + selectedJob.StatusText + "]  items=" +
                    selectedJob.TotalItemCount, labelStyle);

                queueScroll = GUILayout.BeginScrollView(queueScroll, GUILayout.ExpandHeight(true));
                int removeIndex = -1;
                for (int i = 0; i < selectedJob.Items.Count; i++)
                {
                    ConstructionJobItem item = selectedJob.Items[i];
                    GUILayout.BeginHorizontal(boxStyle);
                    Rect iconRect = GUILayoutUtility.GetRect(40f, 40f,
                        GUILayout.Width(40f), GUILayout.Height(40f));
                    DrawIcon(iconRect, item.IconName, item.ItemName);
                    if (iconRect.Contains(Event.current.mousePosition))
                        SetHoveredItem(item.InternalName, item.ItemType, item.ItemName);

                    GUILayout.Label(item.ItemName, queueNameStyle, GUILayout.ExpandWidth(true),
                        GUILayout.Height(40f));
                    GUILayout.Label("Qty", smallStyle, GUILayout.Width(28f), GUILayout.Height(40f));
                    item.Count = Math.Max(1, TextIntField(item.Count, 64f));
                    if (GUILayout.Button("Remove", buttonStyle, GUILayout.Width(76f), GUILayout.Height(30f)))
                        removeIndex = i;
                    GUILayout.EndHorizontal();
                }
                if (removeIndex >= 0)
                {
                    selectedJob.Items.RemoveAt(removeIndex);
                    status = "Removed queue row.";
                }
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                bool canStart = selectedJob.Status == ConstructionJobStatus.Pending &&
                                selectedJob.Items.Count > 0;
                bool oldEnabled = GUI.enabled;
                GUI.enabled = canStart;
                if (GUILayout.Button("Start Job", buttonStyle, GUILayout.Height(30)))
                {
                    selectedJob.Status = ConstructionJobStatus.Started;
                    status = "Started " + selectedJob.Name +
                             ". Phase 1 does not distribute to constructors.";
                }
                GUI.enabled = oldEnabled;
                if (GUILayout.Button("Clear Queue", buttonStyle, GUILayout.Height(30)))
                {
                    selectedJob.Items.Clear();
                    status = "Cleared job queue.";
                }
                GUILayout.EndHorizontal();

                if (selectedJob.Status == ConstructionJobStatus.Started)
                {
                    GUILayout.Label("Started jobs cannot be started again. Cancel Job removes only this manager record.",
                        smallStyle);
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawTemplatePane()
        {
            float paneWidth = Math.Max(360f, windowRect.width * 0.40f);
            GUILayout.BeginVertical(boxStyle, GUILayout.Width(paneWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Templates", headerStyle);
            DrawCategoryFilters();
            DrawGridTypeFilters();
            templateSearch = GUILayout.TextField(templateSearch ?? string.Empty, 40, textFieldStyle);

            if (!templatesLoaded || templates.Count == 0)
                EnsureTemplates();

            float innerWidth = paneWidth - 28f;
            const float gap = 6f;
            float cellSize = Math.Max(42f, Mathf.Floor((innerWidth - gap * 4f) / 5f));
            string search = templateSearch ?? string.Empty;

            templateScroll = GUILayout.BeginScrollView(templateScroll, GUILayout.ExpandHeight(true));
            int shown = 0;
            int column = 0;
            for (int i = 0; i < templates.Count && shown < TemplateResultLimit; i++)
            {
                ConstructionManagerTemplate template = templates[i];
                if (template.Category != selectedCategory ||
                    !template.MatchesGrid(selectedGrid))
                    continue;
                if (search.Length > 0 &&
                    template.InternalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (column == 0) GUILayout.BeginHorizontal();
                Rect cell = GUILayoutUtility.GetRect(cellSize, cellSize,
                    GUILayout.Width(cellSize), GUILayout.Height(cellSize));
                DrawTemplateCell(cell, template);

                shown++;
                column++;
                if (column == 5)
                {
                    GUILayout.EndHorizontal();
                    column = 0;
                }
            }
            if (column > 0) GUILayout.EndHorizontal();

            if (shown >= TemplateResultLimit)
                GUILayout.Label("Refine the search; result display is capped at " + TemplateResultLimit + ".",
                    smallStyle);
            if (shown == 0)
                GUILayout.Label("No matching templates.", smallStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawCategoryFilters()
        {
            GUILayout.Label("Category", smallStyle);
            GUILayout.BeginHorizontal();
            CategoryToggle(ConstructionManagerCategory.Food, "Food",
                "Show food items and food ingredients.");
            CategoryToggle(ConstructionManagerCategory.Medicine, "Medicine",
                "Show medical items and medical ingredients.");
            CategoryToggle(ConstructionManagerCategory.WeaponsItems, "Weapons & Items",
                "Show weapons, ammunition and hand-held items.");
            CategoryToggle(ConstructionManagerCategory.BuildingBlocks, "Building Blocks",
                "Show structural building blocks.");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            CategoryToggle(ConstructionManagerCategory.Components, "Components",
                "Show processed components and ingredients.");
            CategoryToggle(ConstructionManagerCategory.Devices, "Devices",
                "Show functional devices and ship equipment.");
            CategoryToggle(ConstructionManagerCategory.DecorationBlocks, "Decoration Blocks",
                "Show decorative blocks and consoles.");
            GUILayout.EndHorizontal();
        }

        private void CategoryToggle(ConstructionManagerCategory category, string label, string tooltip)
        {
            bool next = FilterToggle(selectedCategory == category, label, label, tooltip);
            if (next && selectedCategory != category) selectedCategory = category;
        }

        private void DrawGridTypeFilters()
        {
            GUILayout.Label("Grid Type", smallStyle);
            GUILayout.BeginHorizontal();
            GridToggle(ConstructionManagerGridType.All, "All",
                "Show templates for every grid type.");
            GridToggle(ConstructionManagerGridType.BA, "BA",
                "Show templates usable on a base.");
            GridToggle(ConstructionManagerGridType.CV, "CV",
                "Show templates usable on a capital vessel.");
            GridToggle(ConstructionManagerGridType.SV, "SV",
                "Show templates usable on a small vessel.");
            GridToggle(ConstructionManagerGridType.HV, "HV",
                "Show templates usable on a hover vessel.");
            GUILayout.EndHorizontal();
        }

        private void GridToggle(ConstructionManagerGridType grid, string label, string tooltip)
        {
            bool next = FilterToggle(selectedGrid == grid, label, "Grid Type: " + label, tooltip);
            if (next && selectedGrid != grid) selectedGrid = grid;
        }

        private bool FilterToggle(bool selected, string label, string tooltipName, string tooltipText)
        {
            GUIStyle style = selected ? selectedButtonStyle : buttonStyle;
            bool next = GUILayout.Toggle(selected, label, style,
                GUILayout.ExpandWidth(true), GUILayout.Height(24));
            Rect rect = GUILayoutUtility.GetLastRect();
            if (rect.Contains(Event.current.mousePosition))
            {
                hoveredName = tooltipName;
                hoveredDescription = tooltipText;
            }
            return next;
        }

        private void DrawTemplateCell(Rect cell, ConstructionManagerTemplate template)
        {
            if (GUI.Button(cell, GUIContent.none, cellStyle))
                AddTemplate(template, ClickAmount(Event.current));

            ConstructionManagerIcon icon = template.Icon;
            Rect iconRect = new Rect(cell.x + cell.width * 0.14f, cell.y + cell.height * 0.14f,
                cell.width * 0.72f, cell.height * 0.72f);
            DrawIcon(icon, iconRect, template.DisplayName);

            if (cell.Contains(Event.current.mousePosition))
            {
                hoveredName = template.DisplayName;
                hoveredDescription = template.Description;
            }
        }

        private void DrawIcon(Rect rect, string iconName, string fallbackText)
        {
            ConstructionManagerIcon icon = itemCatalog == null ? null : itemCatalog.GetIcon(iconName);
            DrawIcon(icon, rect, fallbackText);
        }

        private void DrawIcon(ConstructionManagerIcon icon, Rect rect, string fallbackText)
        {
            if (icon != null)
            {
                GUI.DrawTextureWithTexCoords(rect, icon.Texture, icon.Uv, true);
                return;
            }

            GUI.Box(rect, GUIContent.none, boxStyle);
            string text = string.IsNullOrEmpty(fallbackText) ? "?" : fallbackText.Substring(0, 1);
            GUI.Label(rect, text, iconFallbackStyle);
        }

        private void SetHoveredItem(string internalName, int itemType, string fallbackName)
        {
            ConstructionManagerItemDefinition definition = itemCatalog == null
                ? null
                : itemCatalog.Get(internalName, itemType);
            hoveredName = definition == null ? fallbackName : definition.DisplayName;
            hoveredDescription = definition == null ? "No description available." : definition.Description;
        }

        private void AddTemplate(ConstructionManagerTemplate template, int amount)
        {
            if (selectedJob == null)
            {
                status = "Create or select a job before adding templates.";
                return;
            }

            if (amount < 1) amount = 1;
            ConstructionJobItem existing = selectedJob.Items.FirstOrDefault(item =>
                item.ItemType == template.ItemType &&
                string.Equals(item.InternalName, template.InternalName, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Count = existing.Count > int.MaxValue - amount
                    ? int.MaxValue
                    : existing.Count + amount;
            }
            else
            {
                selectedJob.Items.Add(new ConstructionJobItem(template.ItemType, template.InternalName,
                    template.DisplayName, template.IconName, amount));
            }

            status = "Added " + amount + " x " + template.DisplayName + " to " + selectedJob.Name + ".";
        }

        private static int ClickAmount(Event current)
        {
            if (current == null) return 1;
            bool shift = current.shift;
            bool control = current.control;
            if (shift && control) return 1000;
            if (shift) return 10;
            if (control) return 100;
            return 1;
        }

        private void CancelSelectedJob()
        {
            if (selectedJob == null)
            {
                status = "No job selected.";
                return;
            }

            string name = selectedJob.Name;
            bool wasStarted = selectedJob.Status == ConstructionJobStatus.Started;
            session.RemoveJob(selectedJob);
            selectedJob = session.Jobs.FirstOrDefault();
            status = wasStarted
                ? "Removed started job " + name + ". Constructor queues were not changed."
                : "Removed pending job " + name + ".";
        }

        private void DrawTooltip()
        {
            if (string.IsNullOrEmpty(hoveredName)) return;

            float width = Math.Min(430f, windowRect.width - 32f);
            float textHeight = tooltipTextStyle.CalcHeight(
                new GUIContent(hoveredDescription ?? string.Empty), width - 20f);
            float height = Math.Min(280f, Math.Max(78f, textHeight + 62f));
            Vector2 mouse = Event.current.mousePosition;
            float x = Math.Min(mouse.x + 18f, windowRect.width - width - 12f);
            float y = Math.Min(mouse.y + 18f, windowRect.height - height - 12f);
            x = Math.Max(12f, x);
            y = Math.Max(12f, y);

            Rect rect = new Rect(x, y, width, height);
            GUI.Box(rect, GUIContent.none, tooltipBoxStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, 24f),
                hoveredName, tooltipTitleStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 33f, rect.width - 20f, rect.height - 41f),
                hoveredDescription ?? string.Empty, tooltipTextStyle);
        }

        private void EnsureTemplates()
        {
            if (templatesLoaded) return;

            try
            {
                templates.Clear();
                Dictionary<string, int> mapping = api.Application == null
                    ? null
                    : api.Application.GetBlockAndItemMapping();

                if (mapping != null)
                {
                    foreach (KeyValuePair<string, int> entry in mapping)
                    {
                        if (string.IsNullOrEmpty(entry.Key)) continue;
                        if (recipeCatalog != null && !recipeCatalog.IsCraftable(entry.Value))
                            continue;

                        ConstructionManagerItemDefinition definition = itemCatalog == null
                            ? null
                            : itemCatalog.Get(entry.Key, entry.Value);
                        templates.Add(new ConstructionManagerTemplate(entry.Value, entry.Key, definition));
                    }
                }

                templates.Sort((a, b) =>
                {
                    int orderA = recipeCatalog == null ? int.MaxValue : recipeCatalog.OrderOf(a.ItemType);
                    int orderB = recipeCatalog == null ? int.MaxValue : recipeCatalog.OrderOf(b.ItemType);
                    if (orderA != orderB) return orderA.CompareTo(orderB);
                    return string.Compare(a.InternalName, b.InternalName, StringComparison.OrdinalIgnoreCase);
                });

                if (templates.Count == 0)
                {
                    templates.Add(new ConstructionManagerTemplate(-1, "SteelPlate", null));
                    templates.Add(new ConstructionManagerTemplate(-1, "TitanPlates", null));
                    templates.Add(new ConstructionManagerTemplate(-1, "Electronics", null));
                    templates.Add(new ConstructionManagerTemplate(-1, "Computer", null));
                    templates.Add(new ConstructionManagerTemplate(-1, "CapacitorComponent", null));
                }

                templatesLoaded = true;
                ConstructionManagerDiagnostics.Log(api,
                    "Template catalog loaded: " + templates.Count + " constructor-craftable names" +
                    (recipeCatalog == null ? " (recipe filter unavailable)" : " (recipe outputs=" + recipeCatalog.Count + ")") + ".");
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Template catalog unavailable: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private int TextIntField(int value, float width)
        {
            string text = GUILayout.TextField(value.ToString(), 6, textFieldStyle, GUILayout.Width(width));
            int parsed;
            return int.TryParse(text, out parsed) && parsed > 0 ? parsed : value;
        }

        private void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            labelStyle = GUI.skin.label;
            smallStyle = GUI.skin.label;
            headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            buttonStyle = GUI.skin.button;
            selectedButtonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            cellStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 2, 2)
            };
            boxStyle = GUI.skin.box;
            textFieldStyle = GUI.skin.textField;
            windowStyle = GUI.skin.window;

            iconFallbackStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            queueNameStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            tooltipBoxStyle = GUI.skin.box;
            tooltipTitleStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold };
            tooltipTextStyle = new GUIStyle(labelStyle) { wordWrap = true, richText = false };
        }

        internal void Dispose()
        {
            Visible = false;
        }
    }

    public sealed class ConstructionManagerOverlay : MonoBehaviour
    {
        internal ConstructionManagerFeature Owner;

        private void OnGUI()
        {
            ConstructionManagerFeature owner = Owner;
            if (owner == null) return;

            try
            {
                owner.DrawWindow();
            }
            catch
            {
                // A UI exception must not escape a Unity callback.
            }
        }
    }
}
