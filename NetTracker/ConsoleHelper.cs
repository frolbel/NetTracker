using System.Runtime.InteropServices;

namespace NetTracker
{
    public static class ConsoleHelper
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        /// <summary>        
        /// Определяет, запущена ли программа из существующей оболочки (CMD, PowerShell)
        /// или в собственном выделенном окне.
        /// </summary>
        /// <returns>True, если программа запущена в существующей консоли; False, если в собственном окне.</returns>
        public static bool IsLaunchedFromShell()
        {
            uint[] processList = new uint[1];
            uint count = GetConsoleProcessList(processList, 1);
            return count > 1;
        }

        /// <summary>
        /// Ожидает нажатия клавиши перед выходом, только если программа 
        /// запущена в собственном окне (например, через двойной клик в проводнике).
        /// </summary>
        public static void WaitForKeysBeforeExit()
        {
            // Если в списке процессов консоли только один процесс (наш),
            // значит окно закроется сразу после завершения программы.
            if (!IsLaunchedFromShell())
            {
                Console.WriteLine("Нажмите любую клавишу, чтобы выйти...");
                Console.ReadKey(true);
            }
        }
    }
}
