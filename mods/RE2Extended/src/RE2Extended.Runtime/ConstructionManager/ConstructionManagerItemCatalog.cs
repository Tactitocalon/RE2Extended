using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Eleon.Modding;
using UnityEngine;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal sealed class ConstructionManagerIcon
    {
        internal readonly Texture2D Texture;
        internal readonly Rect Uv;

        internal ConstructionManagerIcon(Texture2D texture, Rect uv)
        {
            Texture = texture;
            Uv = uv;
        }
    }

    internal sealed class ConstructionManagerItemDefinition
    {
        private readonly ConstructionManagerItemCatalog catalog;
        private readonly object definition;
        internal readonly string InternalName;
        internal readonly int ItemType;

        private bool displayNameLoaded;
        private string displayName;
        private bool descriptionLoaded;
        private string description;
        private bool iconNameLoaded;
        private string iconName;
        private bool iconLoaded;
        private ConstructionManagerIcon icon;
        private bool categoryLoaded;
        private string category;
        private bool gridLoaded;
        private int gridMask = -1;
        private bool blockLoaded;
        private bool isBlock;

        internal ConstructionManagerItemDefinition(ConstructionManagerItemCatalog catalog,
            object definition, string internalName, int itemType)
        {
            this.catalog = catalog;
            this.definition = definition;
            InternalName = internalName;
            ItemType = itemType;
        }

        internal string DisplayName
        {
            get
            {
                if (!displayNameLoaded)
                {
                    displayNameLoaded = true;
                    string custom = IsBlock
                        ? catalog.ReadBlockProperty(ItemType, "CustomName")
                        : null;
                    if (string.IsNullOrEmpty(custom))
                        custom = catalog.ReadProperty(definition, "CustomName");
                    string key = string.IsNullOrEmpty(custom) ? InternalName : custom;
                    string localized = catalog.Localize(key);
                    displayName = string.IsNullOrEmpty(localized) ? InternalName : localized;
                }
                return displayName;
            }
        }

        internal string Description
        {
            get
            {
                if (!descriptionLoaded)
                {
                    descriptionLoaded = true;
                    string info = IsBlock
                        ? catalog.ReadBlockProperty(ItemType, "Info")
                        : null;
                    if (string.IsNullOrEmpty(info))
                        info = catalog.ReadProperty(definition, "Info");
                    description = catalog.Localize(info);
                    if (string.IsNullOrEmpty(description))
                        description = "No description available.";
                }
                return description;
            }
        }

        internal string IconName
        {
            get
            {
                if (!iconNameLoaded)
                {
                    iconNameLoaded = true;
                    string custom = IsBlock
                        ? catalog.ReadBlockProperty(ItemType, "CustomIcon")
                        : null;
                    if (string.IsNullOrEmpty(custom))
                        custom = catalog.ReadProperty(definition, "CustomIcon");
                    iconName = string.IsNullOrEmpty(custom) ? InternalName : custom;
                }
                return iconName;
            }
        }

        internal string Category
        {
            get
            {
                if (!categoryLoaded)
                {
                    categoryLoaded = true;
                    if (IsBlock)
                        category = catalog.ReadBlockCategory(ItemType);
                    if (string.IsNullOrEmpty(category))
                        category = catalog.ReadProperty(definition, "Category");
                }
                return category;
            }
        }

        internal int GridMask
        {
            get
            {
                if (!gridLoaded)
                {
                    gridLoaded = true;
                    gridMask = IsBlock ? catalog.ReadBlockGridMask(ItemType) : -1;
                }
                return gridMask;
            }
        }

        private bool IsBlock
        {
            get
            {
                if (!blockLoaded)
                {
                    blockLoaded = true;
                    isBlock = catalog.IsBlockDefinition(definition);
                }
                return isBlock;
            }
        }

        internal ConstructionManagerIcon Icon
        {
            get
            {
                if (!iconLoaded)
                {
                    iconLoaded = true;
                    icon = catalog.GetIcon(IconName);
                    if (icon == null && !string.Equals(IconName, InternalName, StringComparison.Ordinal))
                        icon = catalog.GetIcon(InternalName);
                }
                return icon;
            }
        }
    }

    internal sealed class ConstructionManagerItemCatalog
    {
        private static readonly Regex Markup = new Regex(
            @"\[(?:/?(?:c|b|i|u|s|sup|sub)|-|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\]",
            RegexOptions.CultureInvariant);

        private readonly IModApi api;
        private readonly Assembly game;
        private readonly Type addinEditorType;
        private readonly Type dynamicPropertiesType;
        private readonly FieldInfo definitionsField;
        private readonly FieldInfo propertiesField;
        private readonly FieldInfo nameField;
        private readonly MethodInfo hasKeyMethod;
        private readonly MethodInfo getStringMethod;
        private readonly MethodInfo localizeMethod;
        private readonly MethodInfo localizeExistsMethod;
        private readonly ConstructionManagerIconCatalog icons;
        private readonly Type registryOptionsType;
        private readonly FieldInfo registryDefinitionsField;
        private readonly FieldInfo registryPropertiesField;
        private readonly FieldInfo registryCategoryField;
        private readonly FieldInfo registryScopeField;

        private readonly Dictionary<string, object> definitions =
            new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConstructionManagerItemDefinition> wrappers =
            new Dictionary<string, ConstructionManagerItemDefinition>(StringComparer.Ordinal);
        private bool loaded;
        private bool loadFailureLogged;

        private ConstructionManagerItemCatalog(IModApi api, Assembly game,
            Type addinEditorType, Type dynamicPropertiesType,
            FieldInfo definitionsField, FieldInfo propertiesField, FieldInfo nameField,
            MethodInfo hasKeyMethod, MethodInfo getStringMethod,
            MethodInfo localizeMethod, MethodInfo localizeExistsMethod,
            ConstructionManagerIconCatalog icons, Type registryOptionsType,
            FieldInfo registryDefinitionsField, FieldInfo registryPropertiesField,
            FieldInfo registryCategoryField, FieldInfo registryScopeField)
        {
            this.api = api;
            this.game = game;
            this.addinEditorType = addinEditorType;
            this.dynamicPropertiesType = dynamicPropertiesType;
            this.definitionsField = definitionsField;
            this.propertiesField = propertiesField;
            this.nameField = nameField;
            this.hasKeyMethod = hasKeyMethod;
            this.getStringMethod = getStringMethod;
            this.localizeMethod = localizeMethod;
            this.localizeExistsMethod = localizeExistsMethod;
            this.icons = icons;
            this.registryOptionsType = registryOptionsType;
            this.registryDefinitionsField = registryDefinitionsField;
            this.registryPropertiesField = registryPropertiesField;
            this.registryCategoryField = registryCategoryField;
            this.registryScopeField = registryScopeField;
        }

        internal static ConstructionManagerItemCatalog TryCreate(Assembly game, IModApi api, out string failure)
        {
            failure = null;
            try
            {
                Type addinEditorType = FindType(game, "AddinEditor");
                Type dynamicPropertiesType = FindType(game, "DynamicProperties");
                if (addinEditorType == null || dynamicPropertiesType == null)
                {
                    failure = "AddinEditor/DynamicProperties missing";
                    return null;
                }

                FieldInfo definitionsField = addinEditorType
                    .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == addinEditorType.MakeArrayType());
                FieldInfo propertiesField = addinEditorType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == dynamicPropertiesType);
                FieldInfo nameField = addinEditorType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == typeof(string) && f.Name == "userDataList");

                MethodInfo hasKey = dynamicPropertiesType.GetMethod(
                    "HasKey",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);
                MethodInfo getString = dynamicPropertiesType.GetMethod(
                    "GetStringValue",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);

                Assembly ngui = FindAssembly("NGUIAssembly");
                Type localization = ngui == null ? null : FindType(ngui, "Localization");
                MethodInfo localize = localization == null ? null : localization.GetMethod(
                    "Get",
                    BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(string), typeof(bool) }, null);
                MethodInfo exists = localization == null ? null : localization.GetMethod(
                    "Exists",
                    BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(string) }, null);

                Type registryType = FindType(game, "RegistryOptions");
                FieldInfo registryDefinitionsField = registryType == null
                    ? null
                    : registryType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == registryType.MakeArrayType());
                FieldInfo registryPropertiesField = registryType == null
                    ? null
                    : registryType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == dynamicPropertiesType);
                FieldInfo registryCategoryField = registryType == null
                    ? null
                    : registryType.GetField("optionsID",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo registryScopeField = registryType == null
                    ? null
                    : registryType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType.Name == "ClientScope");

                if (definitionsField == null || propertiesField == null || nameField == null ||
                    hasKey == null || getString == null)
                {
                    failure = "item definition contract incomplete";
                    return null;
                }

                ConstructionManagerIconCatalog icons = ngui == null
                    ? null
                    : ConstructionManagerIconCatalog.TryCreate(game, ngui);
                return new ConstructionManagerItemCatalog(api, game, addinEditorType,
                    dynamicPropertiesType, definitionsField, propertiesField, nameField,
                    hasKey, getString, localize, exists, icons, registryType,
                    registryDefinitionsField, registryPropertiesField,
                    registryCategoryField, registryScopeField);
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
                return null;
            }
        }

        private static Assembly FindAssembly(string name)
        {
            Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));
            if (loaded != null) return loaded;
            try { return Assembly.Load(name); }
            catch { return null; }
        }

        private static Type FindType(Assembly assembly, string name)
        {
            Type direct = assembly.GetType("Assembly-CSharp." + name, false);
            if (direct != null) return direct;
            direct = assembly.GetType(name, false);
            if (direct != null) return direct;
            return assembly.GetTypes().FirstOrDefault(t => t != null && t.Name == name);
        }

        internal ConstructionManagerItemDefinition Get(string internalName, int itemType)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(internalName)) return null;

            string key = itemType + "|" + internalName;
            ConstructionManagerItemDefinition wrapper;
            if (wrappers.TryGetValue(key, out wrapper)) return wrapper;

            object definition;
            if (!definitions.TryGetValue(internalName, out definition)) return null;

            wrapper = new ConstructionManagerItemDefinition(this, definition, internalName, itemType);
            wrappers[key] = wrapper;
            return wrapper;
        }

        internal bool IsBlockDefinition(object definition)
        {
            if (definition == null) return false;
            try
            {
                MethodInfo method = definition.GetType().GetMethod("SplitDirectory",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return method != null && method.ReturnType == typeof(bool) &&
                       method.GetParameters().Length == 0 &&
                       (bool)method.Invoke(definition, null);
            }
            catch
            {
                return false;
            }
        }

        internal string ReadBlockProperty(int itemType, string name)
        {
            object block = GetRegistryDefinition(itemType);
            if (block == null || registryPropertiesField == null) return null;
            try
            {
                object properties = registryPropertiesField.GetValue(block);
                if (properties == null) return null;
                if (!(bool)hasKeyMethod.Invoke(properties, new object[] { name })) return null;
                string value = getStringMethod.Invoke(properties, new object[] { name }) as string;
                return string.IsNullOrEmpty(value) || value.Length > 16384 ? null : value;
            }
            catch
            {
                return null;
            }
        }

        internal string ReadBlockCategory(int itemType)
        {
            object block = GetRegistryDefinition(itemType);
            if (block == null || registryCategoryField == null) return null;
            try { return registryCategoryField.GetValue(block) as string; }
            catch { return null; }
        }

        internal int ReadBlockGridMask(int itemType)
        {
            object block = GetRegistryDefinition(itemType);
            if (block == null || registryScopeField == null) return -1;
            try
            {
                int scope = Convert.ToInt32(registryScopeField.GetValue(block));
                int mask = 0;
                if ((scope & 2) != 0) mask |= 1;   // Base -> BA
                if ((scope & 8) != 0) mask |= 2;   // MS -> CV
                if ((scope & 4) != 0) mask |= 4;   // SS -> SV
                if ((scope & 16) != 0) mask |= 8;  // GV -> HV
                return mask;
            }
            catch
            {
                return -1;
            }
        }

        private object GetRegistryDefinition(int itemType)
        {
            if (itemType <= 0 || registryDefinitionsField == null) return null;
            try
            {
                Array blocks = registryDefinitionsField.GetValue(null) as Array;
                return blocks == null || itemType >= blocks.Length ? null : blocks.GetValue(itemType);
            }
            catch
            {
                return null;
            }
        }

        internal ConstructionManagerIcon GetIcon(string iconName)
        {
            return icons == null ? null : icons.Get(iconName);
        }

        internal string ReadProperty(object definition, string name)
        {
            if (definition == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                object properties = propertiesField.GetValue(definition);
                if (properties == null) return null;
                if (!(bool)hasKeyMethod.Invoke(properties, new object[] { name })) return null;
                string value = getStringMethod.Invoke(properties, new object[] { name }) as string;
                return string.IsNullOrEmpty(value) || value.Length > 16384 ? null : value;
            }
            catch
            {
                return null;
            }
        }

        internal string Localize(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                if (localizeMethod == null || localizeExistsMethod == null)
                    return Plain(key);
                if (!(bool)localizeExistsMethod.Invoke(null, new object[] { key }))
                    return Plain(key);
                string text = localizeMethod.Invoke(null, new object[] { key, true }) as string;
                return string.IsNullOrEmpty(text) || text.Length > 16384 ? Plain(key) : Plain(text);
            }
            catch
            {
                return Plain(key);
            }
        }

        private static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            try { return Markup.Replace(text, string.Empty); }
            catch { return text; }
        }

        private void EnsureLoaded()
        {
            if (loaded) return;

            try
            {
                Dictionary<string, int> mapping = api.Application == null
                    ? null
                    : api.Application.GetBlockAndItemMapping();
                Array array = definitionsField.GetValue(null) as Array;
                if (mapping == null || array == null || array.Length > 65536)
                    return;

                int found = 0;
                foreach (object definition in array)
                {
                    if (definition == null) continue;
                    string name = nameField.GetValue(definition) as string;
                    if (string.IsNullOrEmpty(name) || name.Length > 160) continue;

                    int mappedId;
                    if (!mapping.TryGetValue(name, out mappedId)) continue;
                    definitions[name] = definition;
                    found++;
                }

                loaded = true;
                ConstructionManagerDiagnostics.Log(api,
                    "Item catalog loaded: definitions=" + found + " mapping=" + mapping.Count + ".");
            }
            catch (Exception error)
            {
                if (!loadFailureLogged)
                {
                    loadFailureLogged = true;
                    ConstructionManagerDiagnostics.Warning(api,
                        "Item catalog unavailable: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }
    }

    internal sealed class ConstructionManagerIconCatalog
    {
        private readonly Type atlasType;
        private readonly Type spriteType;
        private object iconManager;
        private readonly FieldInfo managerWorldField;
        private readonly FieldInfo managerIconsField;
        private readonly FieldInfo managerTextureField;
        private readonly FieldInfo managerSpritesField;
        private readonly Dictionary<string, ConstructionManagerIcon> icons =
            new Dictionary<string, ConstructionManagerIcon>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConstructionManagerIcon> iconsInsensitive =
            new Dictionary<string, ConstructionManagerIcon>(StringComparer.OrdinalIgnoreCase);
        private bool loaded;

        private ConstructionManagerIconCatalog(Type atlasType, Type spriteType,
            object iconManager, FieldInfo managerWorldField, FieldInfo managerIconsField,
            FieldInfo managerTextureField, FieldInfo managerSpritesField)
        {
            this.atlasType = atlasType;
            this.spriteType = spriteType;
            this.iconManager = iconManager;
            this.managerWorldField = managerWorldField;
            this.managerIconsField = managerIconsField;
            this.managerTextureField = managerTextureField;
            this.managerSpritesField = managerSpritesField;
        }

        internal static ConstructionManagerIconCatalog TryCreate(Assembly game, Assembly ngui)
        {
            Type atlasType = FindType(ngui, "UIAtlas");
            Type spriteType = FindType(ngui, "UISpriteData");
            if (atlasType == null || spriteType == null) return null;

            object manager = null;
            FieldInfo worldField = null;
            FieldInfo iconsField = null;
            FieldInfo textureField = null;
            FieldInfo spritesField = null;
            try
            {
                Type menuType = FindType(game, "MenuSiteConnectionToken");
                Type processType = FindType(game, "ProcessManagerConnectionToken");
                if (menuType != null && processType != null)
                {
                    worldField = menuType
                        .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == menuType);
                    iconsField = menuType
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == processType);
                    textureField = processType
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType == typeof(Texture2D));
                    spritesField = processType
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => f.FieldType.IsGenericType &&
                            f.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                            f.FieldType.GetGenericArguments()[0] == spriteType);

                    if (worldField != null && iconsField != null &&
                        textureField != null && spritesField != null)
                    {
                        object world = worldField.GetValue(null);
                        if (world != null) manager = iconsField.GetValue(world);
                    }
                }
            }
            catch
            {
                manager = null;
            }

            return new ConstructionManagerIconCatalog(atlasType, spriteType, manager,
                worldField, iconsField, textureField, spritesField);
        }

        private static Type FindType(Assembly assembly, string name)
        {
            Type direct = assembly.GetType("Assembly-CSharp." + name, false);
            if (direct != null) return direct;
            direct = assembly.GetType(name, false);
            if (direct != null) return direct;
            return assembly.GetTypes().FirstOrDefault(t => t != null && t.Name == name);
        }

        internal ConstructionManagerIcon Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureLoaded();

            ConstructionManagerIcon icon;
            if (icons.TryGetValue(name, out icon)) return icon;
            return iconsInsensitive.TryGetValue(name, out icon) ? icon : null;
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            try
            {
                FieldInfo nameField = spriteType.GetField("name");
                FieldInfo xField = spriteType.GetField("x");
                FieldInfo yField = spriteType.GetField("y");
                FieldInfo widthField = spriteType.GetField("width");
                FieldInfo heightField = spriteType.GetField("height");
                if (nameField == null || xField == null || yField == null ||
                    widthField == null || heightField == null)
                    return;

                if (iconManager == null && managerWorldField != null && managerIconsField != null)
                {
                    object world = managerWorldField.GetValue(null);
                    if (world != null) iconManager = managerIconsField.GetValue(world);
                }

                if (iconManager != null && managerTextureField != null && managerSpritesField != null)
                {
                    Texture2D texture = managerTextureField.GetValue(iconManager) as Texture2D;
                    IList sprites = managerSpritesField.GetValue(iconManager) as IList;
                    AddAtlas(texture, sprites, nameField, xField, yField, widthField, heightField);
                }

                if (icons.Count == 0)
                {
                    PropertyInfo textureProperty = atlasType.GetProperty("texture");
                    PropertyInfo spritesProperty = atlasType.GetProperty("spriteList");
                    if (textureProperty == null || spritesProperty == null) return;

                    Array atlases = Resources.FindObjectsOfTypeAll(atlasType);
                    if (atlases == null || atlases.Length > 128) return;

                    foreach (object atlas in atlases)
                    {
                        if (atlas == null) continue;
                        Texture2D texture = textureProperty.GetValue(atlas, null) as Texture2D;
                        IList sprites = spritesProperty.GetValue(atlas, null) as IList;
                        AddAtlas(texture, sprites, nameField, xField, yField, widthField, heightField);
                    }
                }
            }
            catch
            {
                // Icons are optional; missing entries render as placeholders.
            }
        }

        private void AddAtlas(Texture2D texture, IList sprites, FieldInfo nameField,
            FieldInfo xField, FieldInfo yField, FieldInfo widthField, FieldInfo heightField)
        {
            if (texture == null || sprites == null || sprites.Count > 65536) return;

            foreach (object sprite in sprites)
            {
                if (sprite == null || sprite.GetType() != spriteType) continue;
                string name = nameField.GetValue(sprite) as string;
                if (string.IsNullOrEmpty(name) || name.Length > 160) continue;

                int x = Convert.ToInt32(xField.GetValue(sprite));
                int y = Convert.ToInt32(yField.GetValue(sprite));
                int width = Convert.ToInt32(widthField.GetValue(sprite));
                int height = Convert.ToInt32(heightField.GetValue(sprite));
                if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
                    x + width > texture.width || y + height > texture.height)
                    continue;

                var icon = new ConstructionManagerIcon(texture,
                    new Rect((float)x / texture.width,
                        1f - (float)(y + height) / texture.height,
                        (float)width / texture.width,
                        (float)height / texture.height));

                if (!icons.ContainsKey(name)) icons.Add(name, icon);
                if (!iconsInsensitive.ContainsKey(name)) iconsInsensitive.Add(name, icon);
            }
        }
    }
}
