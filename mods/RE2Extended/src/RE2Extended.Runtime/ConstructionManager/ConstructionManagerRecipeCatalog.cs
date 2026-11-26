using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eleon.Modding;

namespace RE2Extended.Runtime.ConstructionManager
{
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

        private readonly HashSet<int> craftableItemTypes = new HashSet<int>();
        private readonly Dictionary<int, int> orderByItemType = new Dictionary<int, int>();
        private bool loaded;
        private bool failureLogged;

        private ConstructionManagerRecipeCatalog(IModApi api, MethodInfo allocateMethod,
            Type nodeProviderType, PropertyInfo outputItemTypeProperty,
            FieldInfo globalRecipesField)
        {
            this.api = api;
            this.allocateMethod = allocateMethod;
            this.nodeProviderType = nodeProviderType;
            this.outputItemTypeProperty = outputItemTypeProperty;
            this.globalRecipesField = globalRecipesField;
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

                if (allocate == null || outputProperty == null)
                {
                    failure = "SymbolService.AllocateDockingPane(string) contract incomplete";
                    return null;
                }

                return new ConstructionManagerRecipeCatalog(api, allocate,
                    nodeProviderType, outputProperty, globalRecipes);
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
                    }
                }
            }
            catch
            {
                orderByItemType.Clear();
            }

            if (orderByItemType.Count > 0) return;

            int fallback = 0;
            foreach (int itemType in craftableItemTypes)
                orderByItemType[itemType] = fallback++;
        }
    }
}
