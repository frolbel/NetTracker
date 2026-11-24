using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace NetTracker
{
    /// <summary>
    /// Описание мониторинга сетевой активности (TCP/IP) и DNS-запросов заданных процессов.
    /// </summary>
    /// <remarks>
    /// Использует ETW (Event Tracing for Windows) для захвата событий ядра в реальном времени.
    /// Реализует IDisposable для гарантированного освобождения системных сессий ETW.
    /// </remarks>
    public class NetworkMonitor : IDisposable
    {
        /// <summary>
        /// Системное имя ETW-провайдера, отвечающего за службу DNS-клиента Windows.
        /// </summary>
        /// <remarks>
        /// Используется для перехвата "живых" результатов резолвинга DNS-имен.
        /// </remarks>
        private const string DnsProviderName = "Microsoft-Windows-DNS-Client";

        /// <summary>
        /// Глобальное уникальное имя сессии ETW на уровне операционной системы Windows.
        /// </summary>
        /// <remarks>
        /// Используется для создания новой сессии и поиска зависших (stale) старых сессий.
        /// </remarks>
        private const string SessionName = "frolbel.NetTracker.Session";

        /// <summary>        
        /// Текст, который отображается, если для перехваченного IP-адреса 
        /// не было найдено соответствующего DNS-запроса.
        /// </summary>        
        private const string UnknownDomainText = "Неизвестный домен (прямой IP)";

        /// <summary>
        /// Коллекция имен процессов (без расширения .exe), сетевую активность которых необходимо отслеживать.
        /// </summary>
        /// <remarks>
        /// Использование HashSet обеспечивает максимально быструю фильтрацию (O(1)) при обработке событий.
        /// </remarks>
        private readonly HashSet<string> _targetProcesses;

        /// <summary>
        /// Словарь для хранения связок: [IP-адрес] -> [Настоящее DNS имя]
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _ipToDomainMap;

        /// <summary>
        /// Экземпляр системной сессии трассировки событий (ETW).
        /// </summary>
        /// <remarks>
        /// Управляет жизненным циклом перехвата трафика и маршрутизацией событий.
        /// </remarks>
        private TraceEventSession? _session;

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="NetworkMonitor"/>.
        /// </summary>
        /// <param name="targetProcesses">Коллекция имен процессов (без .exe) для фильтрации трафика.</param>
        public NetworkMonitor(HashSet<string> targetProcesses)
        {
            _session = null;
            _targetProcesses = targetProcesses;
            _ipToDomainMap = new ConcurrentDictionary<string, string>();
            CleanupStaleSessions();
        }

        /// <summary>
        /// Запускает сессию ETW и начинает перехват пакетов.
        /// </summary>
        /// <remarks>
        /// ВНИМАНИЕ: Это блокирующий вызов. Выполнение кода остановится здесь, 
        /// пока не будет вызван метод Stop() из другого потока или обработчика событий (например, Ctrl+C).
        /// </remarks>
        public void Start()
        {
            _session = new TraceEventSession(SessionName);

            // 1. Включаем провайдеры (ВАЖНО: Kernel-провайдер должен включаться самым первым!)
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            _session.EnableProvider(DnsProviderName);

            // 2. Настраиваем парсеры и обработчики событий
            SetupDnsProvider();
            SetupTcpIpProvider();

            // 3. Запускаем блокирующий цикл обработки
            _session.Source.Process();
        }

        /// <summary>
        /// Останавливает обработку событий ETW и освобождает связанную сессию ядра.
        /// </summary>
        /// <remarks>
        /// После вызова этого метода метод Start() завершит свою работу и вернет управление.
        /// </remarks>
        public void Stop()
        {
            if (_session != null)
            {
                _session.Source.StopProcessing();
                _session.Dispose();
                _session = null;
            }
        }

        /// <summary>
        /// Выполняет освобождение ресурсов.
        /// </summary>
        /// <remarks>
        /// Эквивалентно вызову метода Stop().
        /// </remarks>
        public void Dispose() 
        { 
            Stop(); 
        }

        /// <summary>
        /// Настраивает динамический парсер для перехвата ответов системного DNS-клиента.
        /// </summary>
        /// <remarks>
        /// Сопоставляет полученные IPv4-адреса с запрошенными доменными именами.
        /// </remarks>
        private void SetupDnsProvider()
        {
            var dynamicParser = _session!.Source.Dynamic;

            // Обработчик всех динамических событий (в т.ч. DNS)
            dynamicParser.All += data =>
            {
                // Оставить только проверку провайдера:  data.ProviderName != "Microsoft-Windows-DNS-Client")
                // Добавлять: data.EventName != "EventData" нестоит, т.к. будет происходить захват служебных сообщений
                // перед реальными данными, которые отправляет провайдер в виде служебных мета-событий (например, событие
                // с именем "ManifestData", которое содержит схему данных, а не сами данные)
                if (data.ProviderName != DnsProviderName)
                {
                    return;
                }

                var queryName = data.PayloadByName("QueryName") as string;
                var queryResults = data.PayloadByName("QueryResults") as string;

                if (!string.IsNullOrEmpty(queryName) && !string.IsNullOrEmpty(queryResults))
                {
                    var match = Regex.Match(queryResults, @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b");
                    if (match.Success)
                    {
                        string ip = match.Value;
                        _ipToDomainMap[ip] = queryName;
                    }
                }
            };
        }

        /// <summary>
        /// Настраивает Kernel-парсер для перехвата событий установки TCP-соединений.
        /// </summary>
        /// <remarks>
        /// Фильтрует события по заданным процессам и выводит информацию в консоль.
        /// </remarks>
        private void SetupTcpIpProvider()
        {
            var kernelParser = _session!.Source.Kernel;

            // Обработчик TCP-подключений
            kernelParser.TcpIpConnect += data =>
            {
                if (string.IsNullOrEmpty(data.ProcessName) || !_targetProcesses.Contains(data.ProcessName))
                {
                    return;
                }

                string destIp = data.daddr.ToString();
                string domain = _ipToDomainMap.TryGetValue(destIp, out string? mappedDomain)
                    ? mappedDomain
                    : UnknownDomainText;

                string time = $"[{data.TimeStamp:HH:mm:ss.fff}]";
                Console.WriteLine($"{time,-14} | {destIp,-15} | {data.dport,-5} | {domain}");
            };
        }

        /// <summary>
        /// Очистка старых сессий, если они остались.
        /// </summary>
        /// <remarks>
        /// Очищает зависшие сессии ETW с именем <see cref="SessionName"/>, 
        /// которые могли остаться после некорректного закрытия программы в прошлом.
        /// </remarks>
        private static void CleanupStaleSessions()
        {
            foreach (var activeSession in TraceEventSession.GetActiveSessionNames())
            {
                if (activeSession == SessionName)
                {
                    var staleSession = new TraceEventSession(SessionName);
                    staleSession.Dispose();
                }
            }
        }
    }
}
