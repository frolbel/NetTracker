using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NetTracker
{
    internal class Program
    {
        /// <summary>
        /// Точка входа.
        /// </summary>
        /// <param name="args">Аргументы запуска утилиты.</param>
        static int Main(string[] args)
        {
            // 1. Разбор аргументов запуска утилиты.
            bool forceElevate = false;
            

            string? inPath = null;
            string? outPath = null;

            string? explicitTypeStr = null; 
            var outType = ExportFormat.Csv;
            
            var processArgs = new List<string>();
            string? statsPath = null;
            bool printStatsToConsole = false;

            foreach (var arg in args)
            {
                if (arg.StartsWith("--elevate", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-e", StringComparison.OrdinalIgnoreCase))
                {
                    forceElevate = true;
                }
                else if (arg.StartsWith("--in=", StringComparison.OrdinalIgnoreCase))
                {
                    inPath = arg.Substring(5).Trim('"', '\'');
                }
                else if (arg.StartsWith("-i=", StringComparison.OrdinalIgnoreCase))
                {
                    inPath = arg.Substring(3).Trim('"', '\'');
                }else if(arg.StartsWith("--out=", StringComparison.OrdinalIgnoreCase))
                {
                    outPath = arg.Substring(6).Trim('"', '\'');
                }
                else if (arg.StartsWith("-o=", StringComparison.OrdinalIgnoreCase))
                {
                    outPath = arg.Substring(3).Trim('"', '\'');
                }
                else if (arg.StartsWith("--type=", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-t=", StringComparison.OrdinalIgnoreCase))
                {
                    // Извлекаем значение после '='
                    // определение формата см. ниже
                    explicitTypeStr = arg.Substring(arg.IndexOf('=') + 1).Trim('"', '\'');
                }
                else if (arg.StartsWith("--stats=", StringComparison.OrdinalIgnoreCase))
                {
                    statsPath = arg.Substring(8).Trim('"', '\'');
                }
                else if (arg.Equals("--stats", StringComparison.OrdinalIgnoreCase))
                {
                    printStatsToConsole = true;
                }
                else if (arg.StartsWith("--loglevel=", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-l=", StringComparison.OrdinalIgnoreCase))
                {
                    var levelStr = arg.Substring(arg.IndexOf('=') + 1).Trim('"', '\'');
                    if (Enum.TryParse<LogLevel>(levelStr, true, out var level) ||
                        (int.TryParse(levelStr, out int levelInt) && Enum.IsDefined(typeof(LogLevel), levelInt)))
                    {
                        AppLogger.CurrentLevel = (LogLevel)Enum.Parse(typeof(LogLevel), levelStr, true);
                    }
                    else
                    {
                        AppLogger.Warning($"\n[Warning] Неизвестный уровень логирования '{levelStr}'. Используется по умолчанию (Debug).");
                    }
                }
                else
                {
                    // Автоматически убираем путь (C:\...\...\) и расширение (.exe)
                    var cleanName = Path.GetFileNameWithoutExtension(arg);
                    if (!string.IsNullOrWhiteSpace(cleanName))
                    {
                        processArgs.Add(cleanName);
                    }
                }
            }

            // Учет --stats с одобрения --loglevel
            printStatsToConsole = printStatsToConsole && (AppLogger.CurrentLevel >= LogLevel.Statistics);

            // === ОБРАБОТКА ШАБЛОНОВ И ОПРЕДЕЛЕНИЕ ФОРМАТА ЭКСПОРТА ===
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                // 1. Применяем умный парсер шаблонов с поддержкой %date и %time
                outPath = ProcessPathTemplate(outPath);

                // 2. Автоматическое определение формата по расширению (наивысший приоритет)
                var ext = System.IO.Path.GetExtension(outPath).ToLowerInvariant();
                if (ext == ".json") outType = ExportFormat.Json;
                else if (ext == ".csv") outType = ExportFormat.Csv;
                else if (ext == ".raw") outType = ExportFormat.Raw;
                else
                {
                    // 3. Если расширение неизвестно, проверяем явный флаг --type
                    if (!string.IsNullOrWhiteSpace(explicitTypeStr))
                    {
                        if (!Enum.TryParse<ExportFormat>(explicitTypeStr, true, out outType))
                        {
                            AppLogger.Warning($"\n[Warning] Неизвестный формат экспорта '{explicitTypeStr}'. Будет использован формат по умолчанию (Host).");
                            outType = ExportFormat.Host;
                        }
                    }
                }
            }

            // Применяем парсер шаблонов и к статистике
            if (!string.IsNullOrWhiteSpace(statsPath))
            {
                statsPath = ProcessPathTemplate(statsPath);
            }

            // 2.1 ОФФЛАЙН РЕЖИМ: Если указан файл для импорта, ETW мониторинг не запускаем
            if (!string.IsNullOrWhiteSpace(inPath))
            {
                try 
                {
                    AppLogger.Info($"[Импорт] Загрузка данных из {inPath}...");
                    var records = DataImporter.Import(inPath);
                    AppLogger.Info($"[Импорт] Загружено записей: {records.Count}");

                    // Выводим данные на экран (формат совпадает с реал-тайм мониторингом)
                    AppLogger.Capture(string.Empty);
                    AppLogger.Capture(OutputFormatter.GetHeader());
                    AppLogger.Capture(OutputFormatter.GetSeparator());
                    foreach ( var record in records )
                    {
                        AppLogger.Capture(OutputFormatter.FormatRecord(record));
                    }
                    AppLogger.Capture(OutputFormatter.GetSeparator());

                    if (!string.IsNullOrWhiteSpace(outPath))
                    {
                        DataExporter.Export(records, outPath, outType);
                    }

                    if (printStatsToConsole || !string.IsNullOrWhiteSpace(statsPath))
                    {
                        var report = StatisticsGenerator.GenerateReport(records);
                        if (printStatsToConsole)
                        {
                            AppLogger.Statistics("\n" + report);
                        }

                        if (!string.IsNullOrWhiteSpace(statsPath))
                        {
                            var dir = Path.GetDirectoryName(statsPath);
                            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                            File.WriteAllText(statsPath, report, System.Text.Encoding.UTF8);
                            AppLogger.Info($"\n[Статистика] Сохранена в файл: {statsPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"\n[Ошибка импорта] {ex.Message}");
                }

                ConsoleHelper.WaitForKeysBeforeExit();
                return ExitCode.Success;
            }

            // 2.2. Проверка и обработка прав администратора.
            if (!TraceEventSession.IsElevated() ?? false)
            {
                return HandleElevation(args, forceElevate);
            }

            // 3. Получение списка процессов для мониторинга
            HashSet<string>? targetProcesses = GetTargetProcesses(processArgs);
            if (targetProcesses == null || targetProcesses.Count == 0)
            {
                ConsoleHelper.WaitForKeysBeforeExit();
                return ExitCode.BadArguments;
            }

            // 4. Запуск основного цикла мониторинга

            // Предположение того, что не надо включать захват пакетов в память
            List<NetworkRecord>? capturedRecords = null;

            // Если запрошен экспорт ИЛИ статистика, нужно включить захват пакетов в память
            if (!string.IsNullOrWhiteSpace(outPath) || printStatsToConsole || !string.IsNullOrWhiteSpace(statsPath))
            {
                capturedRecords = new();
            }

            RunMonitoring(targetProcesses, capturedRecords);

            AppLogger.Info("\nСбор данных завершен.");

            if (capturedRecords != null && !string.IsNullOrWhiteSpace(outPath)) 
            { 
                DataExporter.Export(capturedRecords, outPath, outType); 
            }

            if (capturedRecords != null && (printStatsToConsole || !string.IsNullOrWhiteSpace(statsPath)))
            {
                var report = StatisticsGenerator.GenerateReport(capturedRecords);

                if (printStatsToConsole)
                {
                    AppLogger.Statistics(report);
                }
                if (!string.IsNullOrWhiteSpace(statsPath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(statsPath);
                        
                        if (!string.IsNullOrWhiteSpace(dir)) 
                        {
                            Directory.CreateDirectory(dir); 
                        }

                        File.WriteAllText(statsPath, report, System.Text.Encoding.UTF8);
                        AppLogger.Info($"\n[Статистика] Сохранена в файл: {statsPath}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"\n[Ошибка] Не удалось сохранить статистику: {ex.Message}");
                    }
                }
            }    

            ConsoleHelper.WaitForKeysBeforeExit();

            return ExitCode.Success;
        }

        /// <summary>
        /// Обрабатывает логику повышения привилегий (UAC) в зависимости от контекста запуска.
        /// </summary>
        /// <param name="args">Аргументы запуска утилиты.</param>
        /// <param name="forceElevate">Указывает, требуется ли принудительно запустить процесс с повышенными привилегиями.</param>
        /// <returns>Код завершения операции обработки повышения привилегий.</returns>
        private static int HandleElevation(string[] args, bool forceElevate)
        {
            if (ConsoleHelper.IsLaunchedFromShell() && !forceElevate)
            {
                AppLogger.Error("Ошибка: Для работы NetTracker требуются права администратора.");
                AppLogger.Warning("Запустите оболочку от имени администратора или используйте флаг --elevate (-e) для запуска в новом окне.");
                return ExitCode.AccessDenied;
            }

            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                var filteredArgs = args.Where(arg => arg != "--elevate" && arg != "-e").ToArray();
                var argsString = string.Join(" ", filteredArgs.Select(a => $"\"{a}\""));
                var shellName = ConsoleHelper.GetParentShellName();

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                if (shellName != null)
                {
                    startInfo.FileName = shellName;
                    if (ConsoleHelper.IsPowerShell(shellName))
                        startInfo.Arguments = $"-NoExit -Command \"& '{exePath}' {argsString}\"";
                    else
                        startInfo.Arguments = $"/K \"\"{exePath}\" {argsString}\"";
                }
                else
                {
                    startInfo.FileName = exePath!;
                    startInfo.Arguments = argsString;
                }

                Process.Start(startInfo);
                return ExitCode.ElevatedRelaunch;
            }
            catch
            {
                AppLogger.Warning("Для работы программы необходимы права администратора.");
                ConsoleHelper.WaitForKeysBeforeExit();
                return ExitCode.AccessDenied;
            }
        }

        /// <summary>
        /// Вспомогательный метод парсинга ввода.
        /// </summary>
        /// <param name="args">Аргументы запуска.</param>
        /// <returns>Коллекция имен процессов (без .exe) для фильтрации трафика.</returns>
        private static HashSet<string>? GetTargetProcesses(IEnumerable<string> args)
        {
            // Соединяем все аргументы в одну строку
            var input = args.Count() > 0 ? string.Join(" ", args) : null;

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Write("Введите имена процессов (через пробел или запятую): ");
                input = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                AppLogger.Warning("Имя процесса не может быть пустым.");
                return null;
            }

            // Разбиваем строку на коллекцию имен процессов, убирая .exe и переводя в нижний регистр
            var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in input.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                processes.Add(p.Replace(".exe", ""));
            }

            if (processes.Count == 0)
            {
                AppLogger.Warning("Не удалось распознать имена процессов.");
            }

            return processes;
        }

        /// <summary>
        /// Выполняет настройку интерфейса и запускает объект монитора.
        /// </summary>
        /// <param name="targetProcesses">Набор имён отслеживаемых процессов.</param>
        /// <param name="capturedRecords">
        /// Коллекция для накопления записей перехваченной сетевой активности.
        /// Если <see langword="null"/>, накопление записей не выполняется.
        /// </param>
        private static void RunMonitoring(HashSet<string> targetProcesses, IList<NetworkRecord>? capturedRecords)
        {
            AppLogger.Info($"\nОжидание сетевой активности от: {string.Join(", ", targetProcesses)}...");
            AppLogger.Info("Нажмите Ctrl+C для завершения...\n");

            AppLogger.Capture(OutputFormatter.GetHeader());
            AppLogger.Capture(OutputFormatter.GetSeparator());

            using (var monitor = new NetworkMonitor(targetProcesses, capturedRecords))
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    monitor.Stop();
                };

                monitor.Start();
            }
        }

        /// <summary>
        /// Обрабатывает строку пути, заменяя шаблоны в фигурных скобках <c>{...}</c> на текущие дату и время.
        /// </summary>
        /// <param name="path">Исходный путь к файлу (например, <c>.\result_{%date_%time}.csv</c>).</param>
        /// <returns>
        /// Готовая строка пути с подставленными значениями (например, <c>.\result_2026-11-26_151106.csv</c>).
        /// </returns>
        /// <remarks>
        /// Поддерживаемые лексемы внутри скобок: <c>%date</c> и <c>%time</c>. 
        /// После лексемы можно указать формат через двоеточие (например, <c>{%date:yyyy.MM.dd_%time:HH-mm}</c>).
        /// Если формат не указан, по умолчанию используются <c>yyyy-MM-dd</c> для даты и <c>HHmmss</c> для времени.
        /// </remarks>
        private static string ProcessPathTemplate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            // 1. Ищем всё, что заключено между { и }
            return Regex.Replace(path, @"\{([^}]+)\}", match =>
            {
                var template = match.Groups[1].Value;
                DateTime now = DateTime.Now;

                // 2. Внутри шаблона ищем %date и %time с опциональным форматом
                // Шаблон: %(date|time) затем опционально ':' и набор букв/дефисов/точек
                return Regex.Replace(template, @"%(date|time)(?::([a-zA-Z\-.]+))?", tokenMatch =>
                {
                    var type = tokenMatch.Groups[1].Value.ToLowerInvariant();
                    // Если формат указан (после ':'), берем его, иначе null
                    var format = tokenMatch.Groups[2].Success ? tokenMatch.Groups[2].Value : null;

                    if (type == "date")
                        return now.ToString(string.IsNullOrEmpty(format) ? "yyyy-MM-dd" : format);
                    if (type == "time")
                        return now.ToString(string.IsNullOrEmpty(format) ? "HHmmss" : format);

                    return tokenMatch.Value;
                });
            });
        }
    }
}
