using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using UnityEngine;

namespace RE2Extended.Runtime.ConstructionManager
{
    // Owns a real, empty native dialog as the cursor/input lease, then hides its
    // canvas and renders the Construction Manager with IMGUI. This follows the
    // proven Trade Ledger pattern, but resolves the native UI members by shape
    // rather than copying metadata tokens or third-party implementation code.
    internal sealed class ConstructionManagerNativeModalLease
    {
        private readonly object manager;
        private readonly Type entryType;
        private readonly Type designerType;
        private readonly FieldInfo stackField;
        private readonly FieldInfo registryField;
        private readonly FieldInfo entryRootField;
        private readonly FieldInfo entryCanvasField;
        private readonly FieldInfo entryControllerField;
        private readonly FieldInfo designerConfigField;
        private readonly FieldInfo designerHandlerField;
        private readonly MethodInfo closeTopMethod;

        private string key;
        private GameObject ownedRoot;
        private CanvasGroup ownedCanvas;
        private DialogConfig ownedConfig;
        private DialogActionHandler ownedHandler;

        private ConstructionManagerNativeModalLease(object manager, Type entryType, Type designerType,
            FieldInfo stackField, FieldInfo registryField, FieldInfo entryRootField,
            FieldInfo entryCanvasField, FieldInfo entryControllerField,
            FieldInfo designerConfigField, FieldInfo designerHandlerField,
            MethodInfo closeTopMethod)
        {
            this.manager = manager;
            this.entryType = entryType;
            this.designerType = designerType;
            this.stackField = stackField;
            this.registryField = registryField;
            this.entryRootField = entryRootField;
            this.entryCanvasField = entryCanvasField;
            this.entryControllerField = entryControllerField;
            this.designerConfigField = designerConfigField;
            this.designerHandlerField = designerHandlerField;
            this.closeTopMethod = closeTopMethod;
        }

        internal static ConstructionManagerNativeModalLease TryCreate(Assembly game, out string failure)
        {
            failure = null;
            try
            {
                Type managerType = FindType(game, "RegistryTreeConnectionToken");
                Type designerType = FindType(game, "StreamDesignerConnectionToken");
                if (managerType == null || designerType == null)
                {
                    failure = "RegistryTreeConnectionToken/StreamDesignerConnectionToken missing";
                    return null;
                }

                MethodInfo getManager = managerType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.ReturnType == managerType && m.GetParameters().Length == 0)
                    .SingleOrDefault();
                if (getManager == null)
                {
                    failure = "native UI manager getter is missing or ambiguous";
                    return null;
                }

                object manager = getManager.Invoke(null, null);
                if (manager == null)
                {
                    failure = "native UI manager unavailable";
                    return null;
                }

                FieldInfo stackField = managerType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType == typeof(Stack<string>));
                FieldInfo registryField = managerType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType.IsGenericType &&
                        f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                        f.FieldType.GetGenericArguments()[0] == typeof(string) &&
                        f.FieldType.GetGenericArguments()[1].DeclaringType == managerType);

                if (stackField == null || registryField == null)
                {
                    failure = "native window stack/registry fields missing";
                    return null;
                }

                Type entryType = registryField.FieldType.GetGenericArguments()[1];
                FieldInfo rootField = entryType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType == typeof(GameObject) &&
                        !string.Equals(f.Name, "colorInitialized", StringComparison.Ordinal));
                FieldInfo canvasField = entryType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType == typeof(CanvasGroup));
                FieldInfo controllerField = entryType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType.Name == "OutlineAttributeConnectionToken");

                FieldInfo configField = designerType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType == typeof(DialogConfig));
                FieldInfo handlerField = designerType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType == typeof(DialogActionHandler));

                MethodInfo closeTop = managerType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "CloseXmlFile" &&
                        m.ReturnType == typeof(bool) && m.GetParameters().Length == 0);

                if (rootField == null || canvasField == null || controllerField == null ||
                    configField == null || handlerField == null || closeTop == null)
                {
                    failure = "native modal ownership contract incomplete";
                    return null;
                }

                return new ConstructionManagerNativeModalLease(manager, entryType, designerType,
                    stackField, registryField, rootField, canvasField, controllerField,
                    configField, handlerField, closeTop);
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

        private Stack<string> Stack
        {
            get { return (Stack<string>)stackField.GetValue(manager); }
        }

        private IDictionary Registry
        {
            get { return (IDictionary)registryField.GetValue(manager); }
        }

        internal bool CanOpen
        {
            get
            {
                try
                {
                    Stack<string> stack = Stack;
                    return stack != null && stack.Count == 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal bool Present
        {
            get
            {
                if (string.IsNullOrEmpty(key)) return false;
                try
                {
                    Stack<string> stack = Stack;
                    IDictionary registry = Registry;
                    if (stack == null || registry == null || !stack.Contains(key) || !registry.Contains(key))
                        return false;

                    object entry = registry[key];
                    object controller = entryControllerField.GetValue(entry);
                    if (controller == null || controller.GetType() != designerType)
                        return false;

                    GameObject currentRoot = entryRootField.GetValue(entry) as GameObject;
                    return ReferenceEquals(currentRoot, ownedRoot) &&
                           ReferenceEquals(designerConfigField.GetValue(controller), ownedConfig) &&
                           ReferenceEquals(designerHandlerField.GetValue(controller), ownedHandler);
                }
                catch
                {
                    return false;
                }
            }
        }

        internal bool Top
        {
            get
            {
                try
                {
                    Stack<string> stack = Stack;
                    return Present && stack != null && stack.Count > 0 && stack.Peek() == key;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal bool HasForeignWindow
        {
            get
            {
                try
                {
                    Stack<string> stack = Stack;
                    return stack != null && stack.Count > 0 && !Top;
                }
                catch
                {
                    return true;
                }
            }
        }

        internal bool TryGetEntryMenu(out GameObject menu)
        {
            menu = null;
            try
            {
                Stack<string> stack = Stack;
                IDictionary registry = Registry;
                if (stack == null || registry == null || stack.Count != 1 || stack.Peek() != "InGameMenu")
                    return false;
                if (!registry.Contains("InGameMenu")) return false;

                object entry = registry["InGameMenu"];
                GameObject root = entryRootField.GetValue(entry) as GameObject;
                if (root == null || !root.activeInHierarchy) return false;

                menu = root;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal bool CloseEntryMenu(GameObject expected)
        {
            GameObject current;
            if (!TryGetEntryMenu(out current) || !ReferenceEquals(current, expected))
                return false;

            try
            {
                return (bool)closeTopMethod.Invoke(manager, null);
            }
            catch
            {
                return false;
            }
        }

        internal bool Adopt(DialogConfig config, DialogActionHandler handler)
        {
            try
            {
                Stack<string> stack = Stack;
                if (stack == null || stack.Count != 1) return false;

                key = stack.Peek();
                ownedConfig = config;
                ownedHandler = handler;

                IDictionary registry = Registry;
                if (registry == null || !registry.Contains(key)) return false;

                object entry = registry[key];
                GameObject root = entryRootField.GetValue(entry) as GameObject;
                object controller = entryControllerField.GetValue(entry);
                if (root == null || controller == null || controller.GetType() != designerType)
                    return false;
                if (!ReferenceEquals(designerConfigField.GetValue(controller), config) ||
                    !ReferenceEquals(designerHandlerField.GetValue(controller), handler))
                    return false;

                ownedCanvas = entryCanvasField.GetValue(entry) as CanvasGroup;
                if (ownedCanvas == null) return false;

                ownedRoot = root;
                Conceal();
                return Present;
            }
            catch
            {
                return false;
            }
        }

        internal void Conceal()
        {
            try
            {
                if (ownedCanvas != null && Present)
                    ownedCanvas.alpha = 0f;
            }
            catch
            {
                // Visual concealment is best-effort; the lease remains valid.
            }
        }

        internal bool CloseTop()
        {
            try
            {
                if (!Top) return false;
                bool result = (bool)closeTopMethod.Invoke(manager, null);
                Conceal();
                return result;
            }
            catch
            {
                return false;
            }
        }
    }
}
