using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using HarmonyLib;
using UnityEngine;

namespace RE2Extended.Runtime.ConstructionManager
{
    internal sealed class ConstructionManagerFeature : IDisposable
    {
        private const float PendingOpenSeconds = 2.5f;
        private const float ManagerCacheSeconds = 0.5f;

        private readonly IModApi api;
        private readonly ConstructionManagerBlock managerBlock;
        private readonly ConstructionManagerProbe probe;
        private readonly ConstructionManagerNativeModalLease lease;
        private readonly ConstructionManagerWindow window;
        private readonly ConstructionManagerMenuEntry menuEntry;

        private GameObject overlayObject;
        private bool disposed;
        private bool failureLogged;
        private float pendingOpenUntil;
        private int cachedManagerStructureId = int.MinValue;
        private bool cachedManagerAvailable;
        private float nextManagerCheck;
        private float suppressNativeUntil;
        private bool inventoryNativeSeen;

        private ConstructionManagerFeature(IModApi api,
            ConstructionManagerBlock managerBlock,
            ConstructionManagerProbe probe,
            ConstructionManagerNativeModalLease lease,
            ConstructionManagerWindow window,
            ConstructionManagerMenuEntry menuEntry)
        {
            this.api = api;
            this.managerBlock = managerBlock;
            this.probe = probe;
            this.lease = lease;
            this.window = window;
            this.menuEntry = menuEntry;
        }

        internal static ConstructionManagerFeature TryInstall(Assembly game, IModApi api,
            Harmony harmony, ApplicationMode mode, bool knownClient)
        {
            if (!knownClient || (mode != ApplicationMode.Client && mode != ApplicationMode.SinglePlayer))
            {
                ConstructionManagerDiagnostics.Log(api,
                    "Phase 1 UI not installed in mode " + mode + " (knownClient=" + knownClient + ").");
                return null;
            }

            try
            {
                string failure;
                ConstructionManagerGameAccess access = ConstructionManagerGameAccess.Create(game, api, out failure);
                if (access == null)
                {
                    ConstructionManagerDiagnostics.Warning(api,
                        "Construction Manager disabled: " + (failure ?? "game access unavailable") + ".");
                    return null;
                }

                string modRoot = GetModRoot();
                if (string.IsNullOrEmpty(modRoot))
                {
                    ConstructionManagerDiagnostics.Warning(api,
                        "Construction Manager disabled: mod root unavailable.");
                    return null;
                }

                var managerBlock = new ConstructionManagerBlock(api, access);
                var probe = new ConstructionManagerProbe(api, game, access, managerBlock, modRoot);

                ConstructionManagerNativeModalLease lease = null;
                ConstructionManagerWindow window = null;
                ConstructionManagerMenuEntry menuEntry = null;

                string leaseFailure;
                lease = ConstructionManagerNativeModalLease.TryCreate(game, out leaseFailure);
                if (lease == null)
                {
                    ConstructionManagerDiagnostics.Warning(api,
                        "Manager UI disabled: native modal lease unavailable: " +
                        (leaseFailure ?? "unknown contract failure") + ". Probe remains available.");
                }
                else
                {
                    window = new ConstructionManagerWindow(api, game);
                    try
                    {
                        menuEntry = new ConstructionManagerMenuEntry();
                    }
                    catch (Exception error)
                    {
                        ConstructionManagerDiagnostics.Warning(api,
                            "Escape-menu entry unavailable; F8 remains available: " +
                            error.GetType().Name + ": " + error.Message);
                        menuEntry = null;
                    }
                }

                var feature = new ConstructionManagerFeature(api, managerBlock, probe,
                    lease, window, menuEntry);
                if (window != null)
                    window.CloseRequested = feature.Close;

                feature.Start();
                ConstructorRegistry.LogWhitelist(api);
                ConstructionManagerDiagnostics.Log(api,
                    "Phase 1 installed: block=" + ConstructionManagerBlock.BlockName +
                    " trigger=DeviceUsed/F8/Escape-menu modalLease=" + (lease != null) +
                    " probe=" + probe.Enabled + " nativeMode=" + mode);
                return feature;
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "Construction Manager disabled after install error: " +
                    error.GetType().Name + ": " + error.Message);
                return null;
            }
        }

        private void Start()
        {
            if (window != null)
            {
                try
                {
                    overlayObject = new GameObject("RE2Extended_ConstructionManagerOverlay");
                    UnityEngine.Object.DontDestroyOnLoad(overlayObject);
                    ConstructionManagerOverlay overlay = overlayObject.AddComponent<ConstructionManagerOverlay>();
                    overlay.Owner = this;
                }
                catch (Exception error)
                {
                    ConstructionManagerDiagnostics.Warning(api,
                        "Overlay unavailable: " + error.GetType().Name + ": " + error.Message);
                    overlayObject = null;
                }
            }

            try
            {
                api.GameEvent += OnGameEvent;
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "GameEvent subscription failed: " + error.GetType().Name + ": " + error.Message);
            }

            if (api.Application != null)
            {
                api.Application.Update += OnUpdate;
                api.Application.OnPlayfieldLoaded += OnPlayfieldLoaded;
                api.Application.OnPlayfieldUnloading += OnPlayfieldUnloading;
            }
        }

        internal void DrawWindow()
        {
            if (window == null || lease == null) return;
            if (!window.Visible || !lease.Top) return;
            window.Draw();
        }

        private void OnUpdate()
        {
            if (disposed) return;

            try
            {
                probe.Tick();
                if (lease != null) lease.Conceal();

                IStructure structure = GetLocalStructure();
                UpdateMenuEntry(structure);

                if (window != null && window.Visible)
                {
                    if (lease == null || !lease.Top)
                    {
                        CloseVisual("native-modal-lost");
                        return;
                    }

                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F8))
                        Close();
                    return;
                }

                if (Input.GetKeyDown(KeyCode.F8))
                    RequestOpen(structure);

                if (suppressNativeUntil > 0f)
                {
                    if (Time.unscaledTime > suppressNativeUntil)
                    {
                        suppressNativeUntil = 0f;
                    }
                    else if (lease != null)
                    {
                        if (lease.HasForeignWindow)
                        {
                            inventoryNativeSeen = true;
                            lease.CloseForeignTop();
                        }
                    }
                }

                if (pendingOpenUntil > 0f)
                {
                    if (Time.unscaledTime > pendingOpenUntil)
                    {
                        pendingOpenUntil = 0f;
                        suppressNativeUntil = 0f;
                        inventoryNativeSeen = false;
                        ConstructionManagerDiagnostics.Log(api,
                            "Open request expired while waiting for the native window stack to clear.");
                    }
                    else if (!inventoryNativeSeen && suppressNativeUntil > 0f &&
                             Time.unscaledTime <= suppressNativeUntil)
                    {
                        // Give the native container window a chance to appear and be closed.
                    }
                    else
                    {
                        TryOpenPending(structure);
                    }
                }
            }
            catch (Exception error)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    ConstructionManagerDiagnostics.Warning(api,
                        "Update handler skipped after error: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }

        private void UpdateMenuEntry(IStructure structure)
        {
            if (menuEntry == null || lease == null || window == null) return;

            GameObject nativeMenu = null;
            bool hasNativeMenu = lease.TryGetEntryMenu(out nativeMenu);
            bool eligible = !window.Visible && pendingOpenUntil <= 0f &&
                            hasNativeMenu && HasManagerCached(structure);
            menuEntry.Sync(eligible ? nativeMenu : null, eligible);

            if (!menuEntry.Clicked) return;
            menuEntry.Clicked = false;
            if (eligible && lease.CloseEntryMenu(nativeMenu))
            {
                ConstructionManagerDiagnostics.Log(api,
                    "Escape-menu entry clicked; closing native menu before opening manager.");
                RequestOpen(structure);
            }
            menuEntry.Hide();
        }

        private bool HasManagerCached(IStructure structure)
        {
            if (structure == null) return false;

            float now = Time.unscaledTime;
            if (structure.Id != cachedManagerStructureId || now >= nextManagerCheck)
            {
                cachedManagerStructureId = structure.Id;
                nextManagerCheck = now + ManagerCacheSeconds;
                cachedManagerAvailable = managerBlock.ExistsOn(structure);
            }
            return cachedManagerAvailable;
        }

        private void RequestOpen(IStructure structure)
        {
            if (lease == null || window == null)
            {
                TryShowGameMessage("Construction Manager UI is unavailable on this build.");
                return;
            }

            if (window.Visible) return;

            if (structure == null || !managerBlock.ExistsOn(structure))
            {
                TryShowGameMessage("No Construction Manager on this structure.");
                return;
            }

            pendingOpenUntil = Time.unscaledTime + PendingOpenSeconds;
            suppressNativeUntil = 0f;
            inventoryNativeSeen = false;
            ConstructionManagerDiagnostics.Log(api,
                "Manager open requested; waiting for an empty native window stack.");
            TryOpenPending(structure);
        }

        private void TryOpenPending(IStructure structure)
        {
            if (lease == null || window == null || window.Visible || pendingOpenUntil <= 0f)
                return;

            if (!lease.CanOpen)
            {
                lease.Conceal();
                return;
            }

            if (structure == null || !managerBlock.ExistsOn(structure))
            {
                pendingOpenUntil = 0f;
                TryShowGameMessage("No Construction Manager on this structure.");
                return;
            }

            var config = new DialogConfig
            {
                TitleText = string.Empty,
                BodyText = string.Empty,
                ButtonTexts = new string[0],
                ButtonIdxForEsc = 0,
                ButtonIdxForEnter = -1,
                MaxChars = 0
            };

            DialogActionHandler handler = (button, link, input, player, custom) =>
            {
                if (window != null && window.Visible) Close();
            };

            bool accepted;
            try
            {
                accepted = api.GUI != null && api.GUI.ShowDialog(config, handler, 731025);
            }
            catch (Exception error)
            {
                pendingOpenUntil = 0f;
                ConstructionManagerDiagnostics.Warning(api,
                    "Native modal lease creation failed: " + error.GetType().Name + ": " + error.Message);
                return;
            }

            if (!accepted || !lease.Adopt(config, handler))
            {
                pendingOpenUntil = 0f;
                ConstructionManagerDiagnostics.Warning(api,
                    "Native modal lease was not accepted; manager window stays closed.");
                try { lease.CloseTop(); } catch { }
                return;
            }

            pendingOpenUntil = 0f;
            if (menuEntry != null) menuEntry.Hide();
            window.Open();
            ConstructionManagerDiagnostics.Log(api,
                "Manager window opened with native modal lease; IMGUI supplies the visual surface.");
        }

        private void Close()
        {
            pendingOpenUntil = 0f;
            suppressNativeUntil = 0f;
            inventoryNativeSeen = false;
            if (window != null) window.Close();
            if (lease != null)
            {
                try { lease.CloseTop(); } catch { }
                lease.Conceal();
            }
            if (menuEntry != null) menuEntry.Hide();
        }

        private void CloseVisual(string reason)
        {
            if (window != null) window.Close();
            if (menuEntry != null) menuEntry.Hide();
            ConstructionManagerDiagnostics.Log(api, "Manager window closed: " + reason + ".");
        }

        private void OnGameEvent(GameEventType type, object arg1, object arg2,
            object arg3, object arg4, object arg5)
        {
            if (disposed) return;

            try
            {
                probe.ObserveGameEvent(type, arg1, arg2, arg3, arg4, arg5);

                IStructure structure = GetLocalStructure();
                if (structure == null) return;

                if ((type == GameEventType.DeviceUsed ||
                     type == GameEventType.DeviceNamePowered) &&
                    managerBlock.IsManagerUse(arg1))
                {
                    if (managerBlock.ExistsOn(structure))
                    {
                        ConstructionManagerDiagnostics.Log(api,
                            "Manager open requested from " + type + "(" + arg1 + ").");
                        RequestOpen(structure);
                    }
                    return;
                }

                if (type == GameEventType.InventoryOpened &&
                    managerBlock.IsManagerEventPosition(structure, arg1, arg2, arg3, arg4, arg5))
                {
                    ConstructionManagerDiagnostics.Log(api,
                        "Manager inventory interaction detected; suppressing native container and opening manager.");
                    pendingOpenUntil = Time.unscaledTime + PendingOpenSeconds;
                    suppressNativeUntil = Time.unscaledTime + 1.5f;
                    inventoryNativeSeen = false;
                    return;
                }

                if (type == GameEventType.BlockChanged &&
                    managerBlock.IsManagerBlockEvent(structure, arg5))
                {
                    ConstructionManagerDiagnostics.Log(api,
                        "Manager open requested from BlockChanged at the manager block.");
                    RequestOpen(structure);
                }
            }
            catch (Exception error)
            {
                ConstructionManagerDiagnostics.Warning(api,
                    "GameEvent handler skipped after error: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void OnPlayfieldLoaded(IPlayfield playfield)
        {
            if (disposed) return;
            try
            {
                probe.Tick();
            }
            catch
            {
                // Probe contains its own diagnostics.
            }
        }

        private void OnPlayfieldUnloading(IPlayfield playfield)
        {
            try
            {
                Close();
            }
            catch
            {
                // Playfield transitions must not be blocked by UI cleanup.
            }
        }

        private IStructure GetLocalStructure()
        {
            try
            {
                IApplication application = api.Application;
                if (application == null) return null;
                IPlayer player = application.LocalPlayer;
                return player == null ? null : player.CurrentStructure;
            }
            catch
            {
                return null;
            }
        }

        private void TryShowGameMessage(string text)
        {
            try
            {
                if (api.GUI != null)
                    api.GUI.ShowGameMessage(text, 0, 3f);
            }
            catch
            {
                // A failed hint must not affect the manager.
            }

            ConstructionManagerDiagnostics.Log(api, text);
        }

        private static string GetModRoot()
        {
            string runtimeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return runtimeFolder == null ? null : Path.GetDirectoryName(runtimeFolder);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            try { Close(); } catch { }

            try
            {
                if (api.Application != null)
                {
                    api.Application.Update -= OnUpdate;
                    api.Application.OnPlayfieldLoaded -= OnPlayfieldLoaded;
                    api.Application.OnPlayfieldUnloading -= OnPlayfieldUnloading;
                }
            }
            catch
            {
                // Shutdown must not throw into the game.
            }

            try
            {
                api.GameEvent -= OnGameEvent;
            }
            catch
            {
                // Shutdown must not throw into the game.
            }

            try
            {
                if (window != null) window.Dispose();
            }
            catch
            {
                // Shutdown must not throw into the game.
            }

            try
            {
                if (menuEntry != null) menuEntry.Dispose();
            }
            catch
            {
                // Shutdown must not throw into the game.
            }

            try
            {
                if (overlayObject != null)
                {
                    ConstructionManagerOverlay overlay = overlayObject.GetComponent<ConstructionManagerOverlay>();
                    if (overlay != null) overlay.Owner = null;
                    UnityEngine.Object.Destroy(overlayObject);
                }
            }
            catch
            {
                // Shutdown must not throw into the game.
            }
            finally
            {
                overlayObject = null;
            }
        }
    }
}
