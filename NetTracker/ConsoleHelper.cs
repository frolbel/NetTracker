using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetTracker
{
    public static class ConsoleHelper
    {
        // Константы для имен оболочек
        private const string ShellCmd = "cmd";        
        private const string ShellPowerShell = "powershell";        
        private const string ShellPwsh = "pwsh";
        
        // Расширения
        private const string ExeExtension = ".exe";

        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        /// <summary>
        /// Содержит базовую информацию о процессе, включая идентификатор родительского процесса.
        /// Используется при вызове низкоуровневой функции NtQueryInformationProcess.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public IntPtr UniqueProcessId;
            
            /// <summary>
            /// Идентификатор родительского процесса (InheritedFromUniqueProcessId).
            /// </summary>
            public IntPtr InheritedFromUniqueProcessId;
        }

        /// <summary>
        /// Извлекает информацию о заданном процессе из системных структур Windows.
        /// </summary>
        /// <param name="processHandle">Дескриптор процесса.</param>
        /// <param name="processInformationClass">Тип извлекаемой информации (0 для базовой информации).</param>
        /// <param name="processInformation">Ссылка на структуру для получения данных.</param>
        /// <param name="processInformationLength">Размер структуры в байтах.</param>
        /// <param name="returnLength">Количество фактически записанных байт.</param>
        /// <returns>Статус NTSTATUS (0 в случае успеха).</returns>
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref PROCESS_BASIC_INFORMATION processInformation,
            int processInformationLength,
            out int returnLength);

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

        /// <summary>
        /// Определяет имя исполняемого файла родительской оболочки (cmd, powershell, pwsh), 
        /// из которой был запущен текущий экземпляр NetTracker.
        /// </summary>
        /// <returns>
        /// Имя файла оболочки с расширением (например, "powershell.exe"),
        /// либо null, если программа запущена не из известной консоли.
        /// </returns>
        public static string? GetParentShellName()
        {
            if (!IsLaunchedFromShell())
            {
                //  Программа запущена не из родительской оболочки
                return null;
            }

            try
            {
                PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                int returnLength;
                IntPtr handle = Process.GetCurrentProcess().Handle;

                // Вызываем системную функцию для получения ID родителя
                if (NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength) == 0)
                {
                    using (var parentProcess = Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32()))
                    {
                        string name = parentProcess.ProcessName.ToLower();
                        // Проверяем, является ли родитель известной консольной оболочкой
                        if (name == ShellCmd || name == ShellPowerShell || name == ShellPwsh)
                        {
                            return $"{name}{ExeExtension}";
                        }
                    }
                }
            }
            catch
            {
                // В случае ошибки доступа или отсутствия процесса игнорируем и возвращаем дефолт
            }

            // Запущена не из родительской оболочки
            return null;
        }

        /// <summary>
        /// Проверяет, является ли имя оболочки производной от PowerShell (powershell.exe или pwsh.exe).
        /// </summary>
        public static bool IsPowerShell(string shellName)
        {            
            if (string.IsNullOrEmpty(shellName))
            { 
                return false;
            }

            var name = shellName.ToLower().Replace(ExeExtension, "");
            
            return name == ShellPowerShell || name == ShellPwsh;   
        }
    }
}
