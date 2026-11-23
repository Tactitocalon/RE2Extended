using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;

namespace RE2Extended
{
    public interface IRE2ExtendedRuntime : IDisposable
    {
        void Start(IModApi api);
    }

    public sealed class RE2ExtendedMod : IMod
    {
        private const string RuntimeAssemblyName = "RE2Extended.Runtime.dll";
        private const string RuntimeTypeName = "RE2Extended.Runtime.RE2ExtendedRuntime";

        private IModApi api;
        private IRE2ExtendedRuntime runtime;

        public void Init(IModApi modApi)
        {
            api = modApi;
            try
            {
                Log("RE2Extended starting (v" + Assembly.GetExecutingAssembly().GetName().Version + ").");

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
                runtime = Activator.CreateInstance(type) as IRE2ExtendedRuntime;
                if (runtime == null)
                    throw new InvalidOperationException("runtime-contract-mismatch");

                runtime.Start(modApi);
                Log("RE2Extended initialized.");
            }
            catch (Exception error)
            {
                Log("RE2Extended disabled: " + error.GetType().Name + ": " + error.Message);
                DisposeRuntime();
            }
        }

        public void Shutdown()
        {
            try
            {
                Log("RE2Extended shutting down.");
            }
            catch
            {
                // Optional during shutdown.
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
                    api.Log("[RE2Extended] " + text);
            }
            catch
            {
                // Logging must never break mod loading.
            }
        }
    }
}
