using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;

namespace CUCoreLib.Helpers
{
    public abstract class CCLBase
    {
        protected CCLBase()
        {
        }

        protected CCLBase(BaseUnityPlugin plugin)
        {
            Plugin = plugin;
        }

        protected BaseUnityPlugin Plugin { get; private set; }

        protected ManualLogSource Logger
        {
            get
            {
                return CUCoreLibPlugin.Log;
            }
        }

        public static void Initialize(BaseUnityPlugin plugin)
        {
            if (plugin == null) return;

            ContentReload.ContentHostRuntime.RegisterPlugin(plugin);
        }

        internal void AttachPlugin(BaseUnityPlugin plugin)
        {
            Plugin = plugin;
        }

        protected T GetPlugin<T>() where T : BaseUnityPlugin
        {
            return Plugin as T;
        }

        protected PluginInfo GetPluginInfo()
        {
            return Plugin != null ? Plugin.Info : null;
        }
    }
}
