using System.Text;
using System.Text.Json;

namespace NetTracker
{
    /// <summary>
    /// Описание сохранения перехваченных данных в файл после завершения мониторинга.
    /// </summary>
    public static class DataExporter
    {
        public static void Export(IReadOnlyList<NetworkRecord> records, string filePath, ExportFormat format)
        {
            if (records == null || records.Count == 0)
            {
                Console.WriteLine("\n[Экспорт] Нет данных для сохранения.");
                return;
            }

            try
            {
                Console.WriteLine($"\n[Экспорт] Сохранение в файл: {filePath} (Формат: {format})...");

                // Создаем директорию, если она указана, но еще не существует
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                switch (format)
                {
                    case ExportFormat.Csv:
                        ExportCsv(records, filePath);
                        break;
                    case ExportFormat.Json:
                        ExportJson(records, filePath);
                        break;
                    case ExportFormat.Host:
                        ExportHost(records, filePath, useDummyIp: false);
                        break;
                    case ExportFormat.Dummy:
                        ExportHost(records, filePath, useDummyIp: true);
                        break;
                    case ExportFormat.Raw:
                    default:
                        ExportRaw(records, filePath);
                        break;
                }

                Console.WriteLine("[Экспорт] Успешно завершено.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"\n[Ошибка экспорта] Не удалось сохранить файл: {ex.Message}");
                Console.ResetColor();
            }
        }

        private static void ExportRaw(IReadOnlyList<NetworkRecord> records, string filePath)
        {
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            writer.WriteLine($"{"Время",-14} | {"IP-адрес",-15} | {"Порт",-5} | {"DNS-имя (Домен)"}");
            writer.WriteLine(new string('-', 90));

            foreach (var r in records)
            {
                writer.WriteLine($"[{r.TimeStamp:HH:mm:ss.fff}] | {r.DestIp,-15} | {r.DestPort,-5} | {r.Domain}");
            }
        }

        private static void ExportCsv(IReadOnlyList<NetworkRecord> records, string filePath)
        {
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            writer.WriteLine("TimeStamp;DestIp;DestPort;Domain");
            foreach (var r in records)
            {
                writer.WriteLine($"{r.TimeStamp:O};{r.DestIp};{r.DestPort};{r.Domain}");
            }
        }

        private static void ExportJson(IReadOnlyList<NetworkRecord> records, string filePath)
        {
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                // Разрешаем сериализатору не экранировать кириллицу и другие символы
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var jsonString = JsonSerializer.Serialize(records, options);
            File.WriteAllText(filePath, jsonString, Encoding.UTF8);
        }

        private static void ExportHost(IReadOnlyList<NetworkRecord> records, string filePath, bool useDummyIp)
        {
            // Исключаем неизвестные домены и убираем дубликаты пар (IP + Домен)
            var uniqueEntries = records
                .Where(r => r.Domain != NetworkMonitor.UnknownDomainText)
                .Select(r => new { r.DestIp, r.Domain })
                .Distinct();

            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            writer.WriteLine("# Сгенерировано NetTracker");
            foreach (var entry in uniqueEntries)
            {
                var ip = useDummyIp ? "127.0.0.1" : entry.DestIp;
                writer.WriteLine($"{ip}\t{entry.Domain}");
            }
        }
    }
}
