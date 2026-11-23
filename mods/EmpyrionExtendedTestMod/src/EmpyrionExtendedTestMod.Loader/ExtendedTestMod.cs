using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;

namespace EmpyrionExtendedTestMod
{
    /// <summary>
    /// Contract implemented by the runtime adapter. The loader deliberately has no
    /// compile-time reference to Harmony so a failed adapter cannot break mod loading.
    /// </summary>
    public interface IExtendedTestRuntime : IDisposable
    {
        void Start(IModApi api);
    }

    public sealed class ExtendedTestMod : IMod
    {
        private const string RuntimeAssemblyName = "EmpyrionExtendedTestMod.Runtime.dll";
        private const string RuntimeTypeName = "EmpyrionExtendedTestMod.Runtime.ExtendedTestRuntime";

        private IModApi api;
        private IExtendedTestRuntime runtime;

        public void Init(IModApi modApi)
        {
            api = modApi;
            try
            {
                Log("Empyrion Extended Test Mod starting (v" + Assembly.GetExecutingAssembly().GetName().Version + ").");

                string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(root))
                    throw new InvalidOperationException("mod-folder-unavailable");

                string runtimeFolder = Path.Combine(root, "runtime");
                string runtimePath = Path.Combine(runtimeFolder, RuntimeAssemblyName);
                string harmonyPath = Path.Combine(runtimeFolder, "0Harmony.dll");

                if (!File.Exists(runtimePath))
                    throw new FileNotFoundException("runtime adapter missing", runtimePath);
                if (!File.Exists(harmonyPath))
                    throw new FileNotFoundException("Harmony missing", harmonyPath);

                Assembly harmony = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase));
                if (harmony == null)
                {
                    harmony = Assembly.LoadFrom(harmonyPath);
                    Log("Loaded bundled Harmony: " + harmony.FullName);
                }
                else
                {
                    Log("Using already-loaded Harmony: " + harmony.FullName);
                }

                Assembly adapter = Assembly.LoadFrom(runtimePath);
                Type type = adapter.GetType(RuntimeTypeName, true);
                runtime = Activator.CreateInstance(type) as IExtendedTestRuntime;
                if (runtime == null)
                    throw new InvalidOperationException("runtime-contract-mismatch");

                runtime.Start(modApi);
                Log("Empyrion Extended Test Mod initialized.");
            }
            catch (Exception error)
            {
                Log("Empyrion Extended Test Mod disabled: " + error.GetType().Name + ": " + error.Message);
                DisposeRuntime();
            }
        }

        public void Shutdown()
        {
            try
            {
                Log("Empyrion Extended Test Mod shutting down.");
            }
            catch
            {
                // Logging during shutdown is optional.
            }
            finally
            {
                DisposeRuntime();
            }
        }

        private void DisposeRuntime()
        {
            try
            {
                if (runtime != null)
                    runtime.Dispose();
            }
            catch
            {
                // Shutdown must not throw back into the game.
            }
            finally
            {
                runtime = null;
            }
        }

        private void Log(string text)
        {
            try
            {
                if (api != null)
                    api.Log("[ExtendedTestMod] " + text);
            }
            catch
            {
                // The test mod remains safe if logging is unavailable.
            }
        }
    }
}
