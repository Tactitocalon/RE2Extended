using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eleon.Modding;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal sealed class ConstructionManagerRecipeInput
    {
        internal readonly int ItemType;
        internal readonly int Count;

        internal ConstructionManagerRecipeInput(int itemType, int count)
        {
            ItemType = itemType;
            Count = count;
        }
    }

    // Uses the same native entry point the constructor uses to build its template
    // list: SymbolService.AllocateDockingPane(constructorClassName).
    internal sealed class ConstructionManagerRecipeCatalog
    {
        private readonly IModApi api;
        // Native target tokens for the whitelisted constructor families. T3 is
        // included through SmallC as well as AdvC because its class is the small
        // advanced variant; the union is the safest first-release list.
        private static readonly string[] TargetTokens =
        {
            "BaseC",
            "SmallC",
            "LargeC",
            "AdvC"
        };

        private readonly MethodInfo allocateMethod;
        private readonly Type nodeProviderType;
        private readonly PropertyInfo outputItemTypeProperty;
        private readonly FieldInfo globalRecipesField;
        private readonly MethodInfo cleanActivatorMethod;
        private readonly FieldInfo craftTimeField;
        private readonly FieldInfo inputsField;
        private readonly Type paneFactoryType;
        private readonly FieldInfo paneOptionsField;
        private readonly FieldInfo paneQuantityField;
        private readonly FieldInfo optionsItemTypeField;
        private readonly PropertyInfo outputCountProperty;

        private readonly HashSet<int> craftableItemTypes = new HashSet<int>();
        private readonly Dictionary<int, int> orderByItemType = new Dictionary<int, int>();
        private readonly Dictionary<int, object> recipesByItemType = new Dictionary<int, object>();
        private readonly Dictionary<int, List<string>> tooltipCache = new Dictionary<int, List<string>>();
        private bool loaded;
        private bool failureLogged;

        private ConstructionManagerRecipeCatalog(IModApi api, MethodInfo allocateMethod,
            Type nodeProviderType, PropertyInfo outputItemTypeProperty,
            FieldInfo globalRecipesField, MethodInfo cleanActivatorMethod,
            FieldInfo craftTimeField, FieldInfo inputsField, Type paneFactoryType,
            FieldInfo paneOptionsField, FieldInfo paneQuantityField,
            FieldInfo optionsItemTypeField, PropertyInfo outputCountProperty)
        {
            this.api = api;
            this.allocateMethod = allocateMethod;
            this.nodeProviderType = nodeProviderType;
            this.outputItemTypeProperty = outputItemTypeProperty;
            this.globalRecipesField = globalRecipesField;
            this.cleanActivatorMethod = cleanActivatorMethod;
            this.craftTimeField = craftTimeField;
            this.inputsField = inputsField;
            this.paneFactoryType = paneFactoryType;
            this.paneOptionsField = paneOptionsField;
            this.paneQuantityField = paneQuantityField;
            this.optionsItemTypeField = optionsItemTypeField;
            this.outputCountProperty = outputCountProperty;
        }

        internal static ConstructionManagerRecipeCatalog TryCreate(Assembly game, IModApi api, out string failure)
        {
            failure = null;
            try
            {
                Type symbolServiceType = FindType(game, "SymbolService");
                Type nodeProviderType = FindType(game, "NodeProvider");
                if (symbolServiceType == null || nodeProviderType == null)
                {
                    failure = "SymbolService/NodeProvider missing";
                    return null;
                }

                MethodInfo allocate = symbolServiceType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "AllocateDockingPane" &&
                                         m.GetParameters().Length == 1 &&
                                         m.GetParameters()[0].ParameterType == typeof(string));
                PropertyInfo outputProperty = nodeProviderType.GetProperty(
                    "outputItemType",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo globalRecipes = symbolServiceType
                    .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType.IsGenericType &&
                        f.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                        f.FieldType.GetGenericArguments()[0] == nodeProviderType);
                MethodInfo cleanActivator = nodeProviderType.GetMethod(
                    "CleanActivator",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(bool) }, null);
                FieldInfo craftTime = nodeProviderType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.IsPublic && f.FieldType == typeof(float));
                PropertyInfo outputCountProperty = nodeProviderType.GetProperty(
                    "outputCount",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                Type paneFactoryType = FindType(game, "PaneFactory");
                Type optionsSerializerType = FindType(game, "OptionsSerializer");
                FieldInfo inputsField = paneFactoryType == null
                    ? null
                    : nodeProviderType
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType.IsGenericType &&
                            f.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                            f.FieldType.GetGenericArguments()[0] == paneFactoryType);
                FieldInfo paneOptionsField = paneFactoryType == null || optionsSerializerType == null
                    ? null
                    : paneFactoryType
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == optionsSerializerType);
                FieldInfo paneQuantityField = paneFactoryType == null
                    ? null
                    : paneFactoryType
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == typeof(int) && f.Name == "keywordsID");
                FieldInfo optionsItemTypeField = optionsSerializerType == null
                    ? null
                    : optionsSerializerType.GetField("colorInitialized",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (allocate == null || outputProperty == null)
                {
                    failure = "SymbolService.AllocateDockingPane(string) contract incomplete";
                    return null;
                }

                return new ConstructionManagerRecipeCatalog(api, allocate,
                    nodeProviderType, outputProperty, globalRecipes,
                    cleanActivator, craftTime, inputsField, paneFactoryType,
                    paneOptionsField, paneQuantityField, optionsItemTypeField,
                    outputCountProperty);
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
                return null;
            }
        }

        private static Type FindType(Assembly game, string name)
        {
            Type direct = game.GetType("Assembly-CSharp." + name, false);
            if (direct != null) return direct;
            direct = game.GetType(name, false);
            if (direct != null) return direct;
            return game.GetTypes().FirstOrDefault(t => t != null && t.Name == name);
        }

        internal bool IsCraftable(int itemType)
        {
            EnsureLoaded();
            return craftableItemTypes.Contains(itemType);
        }

        internal int Count
        {
            get
            {
                EnsureLoaded();
                return craftableItemTypes.Count;
            }
        }

        internal int OrderOf(int itemType)
        {
            EnsureLoaded();
            int order;
            return orderByItemType.TryGetValue(itemType, out order) ? order : int.MaxValue;
        }

        internal float CraftTime(int itemType)
        {
            EnsureLoaded();
            object recipe;
            if (craftTimeField == null || !recipesByItemType.TryGetValue(itemType, out recipe))
                return -1f;
            try { return Convert.ToSingle(craftTimeField.GetValue(recipe)); }
            catch { return -1f; }
        }

        internal List<ConstructionManagerRecipeInput> Inputs(int itemType)
        {
            EnsureLoaded();
            object recipe;
            if (!recipesByItemType.TryGetValue(itemType, out recipe) ||
                inputsField == null || paneFactoryType == null ||
                paneOptionsField == null || paneQuantityField == null ||
                optionsItemTypeField == null)
                return null;

            try
            {
                IEnumerable inputs = inputsField.GetValue(recipe) as IEnumerable;
                if (inputs == null) return null;

                var result = new List<ConstructionManagerRecipeInput>();
                foreach (object pane in inputs)
                {
                    if (pane == null || pane.GetType() != paneFactoryType) continue;
                    object options = paneOptionsField.GetValue(pane);
                    if (options == null) continue;
                    int id = Convert.ToInt32(optionsItemTypeField.GetValue(options));
                    int count = Convert.ToInt32(paneQuantityField.GetValue(pane));
                    if (id > 0 && count > 0)
                        result.Add(new ConstructionManagerRecipeInput(id, count));
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        internal int OutputCount(int itemType)
        {
            EnsureLoaded();
            object recipe;
            if (outputCountProperty == null || !recipesByItemType.TryGetValue(itemType, out recipe))
                return 0;
            try { return Convert.ToInt32(outputCountProperty.GetValue(recipe, null)); }
            catch { return 0; }
        }

        internal List<string> TooltipLines(int itemType)
        {
            EnsureLoaded();
            List<string> cached;
            if (tooltipCache.TryGetValue(itemType, out cached)) return cached;

            object recipe;
            if (cleanActivatorMethod == null || !recipesByItemType.TryGetValue(itemType, out recipe))
                return null;

            try
            {
                IEnumerable lines = cleanActivatorMethod.Invoke(recipe, new object[] { false }) as IEnumerable;
                if (lines == null) return null;
                var result = new List<string>();
                foreach (object line in lines)
                {
                    string text = line as string;
                    if (!string.IsNullOrEmpty(text)) result.Add(text);
                }
                tooltipCache[itemType] = result;
                return result;
            }
            catch
            {
                return null;
            }
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            try
            {
                for (int c = 0; c < TargetTokens.Length; c++)
                {
                    string target = TargetTokens[c];
                    int before = craftableItemTypes.Count;
                    object result;
                    try { result = allocateMethod.Invoke(null, new object[] { target }); }
                    catch (Exception error)
                    {
                        ConstructionManagerDiagnostics.Warning(api,
                            "Recipe lookup failed for constructor target " + target + ": " +
                            error.GetType().Name + ": " + error.Message);
                        continue;
                    }

                    IEnumerable recipes = result as IEnumerable;
                    if (recipes == null) continue;

                    foreach (object recipe in recipes)
                    {
                        if (recipe == null || recipe.GetType() != nodeProviderType) continue;
                        object output = outputItemTypeProperty.GetValue(recipe, null);
                        if (!(output is int)) continue;
                        int outputItemType = (int)output;
                        if (outputItemType > 0) craftableItemTypes.Add(outputItemType);
                    }

                    ConstructionManagerDiagnostics.Log(api,
                        "Constructor recipe target " + target + ": +" +
                        (craftableItemTypes.Count - before) + " outputs.");
                }

                BuildNativeOrder();
                ConstructionManagerDiagnostics.Log(api,
                    "Recipe catalog loaded: unique constructor-craftable outputs=" +
                    craftableItemTypes.Count + " ordered=" + orderByItemType.Count + ".");
            }
            catch (Exception error)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    ConstructionManagerDiagnostics.Warning(api,
                        "Recipe catalog unavailable: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }

        private void BuildNativeOrder()
        {
            orderByItemType.Clear();
            recipesByItemType.Clear();
            try
            {
                IEnumerable recipes = globalRecipesField == null
                    ? null
                    : globalRecipesField.GetValue(null) as IEnumerable;
                if (recipes != null)
                {
                    int index = 0;
                    foreach (object recipe in recipes)
                    {
                        if (recipe == null || recipe.GetType() != nodeProviderType) continue;
                        object output = outputItemTypeProperty.GetValue(recipe, null);
                        if (!(output is int)) continue;
                        int itemType = (int)output;
                        if (itemType <= 0 || !craftableItemTypes.Contains(itemType) ||
                            orderByItemType.ContainsKey(itemType))
                        {
                            index++;
                            continue;
                        }
                        orderByItemType[itemType] = index++;
                        recipesByItemType[itemType] = recipe;
                    }
                }
            }
            catch
            {
                orderByItemType.Clear();
                recipesByItemType.Clear();
            }

            if (orderByItemType.Count > 0) return;

            int fallback = 0;
            foreach (int itemType in craftableItemTypes)
                orderByItemType[itemType] = fallback++;
        }
    }
}
