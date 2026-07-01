namespace CUCoreLib.Util
{
    public class ConsoleUtils
    {
        public static readonly ConsoleScript ConsoleInstance = ConsoleScript.instance;

        public static void LogToConsole(ConsoleScript instance, string commandString)
        {
            ReflectionUtils.InvokeMethod(instance, "RunCommandString", commandString);
        }
        
        public static void RunCommand(ConsoleScript instance, string commandString)
        {
            ReflectionUtils.InvokeMethod(instance, "RunCommandString", commandString);
        }
    }
}