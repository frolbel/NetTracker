using System.Text.Json;

namespace NetTracker
{
    /// <summary>
    /// Отвечает за загрузку ранее сохраненных данных (CSV/JSON) для оффлайн-анализа.
    /// </summary>
    public static class DataImporter
    {
        /// <summary>
        /// Загружает сетевые записи из указанного файла (.json или .csv).
        /// </summary>
        public static IReadOnlyList<NetworkRecord> Import(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            return ext switch
            {
                ".json" => ImportJson(filePath),
                ".csv" => ImportCsv(filePath),
                _ => throw new NotSupportedException($"Формат {ext} не поддерживается для импорта. Используйте .json или .csv")
            };
        }

        private static IReadOnlyList<NetworkRecord> ImportJson(string filePath)
        {
            var jsonString = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // JsonSerializer вернет List, который неявно приводится к IReadOnlyList
            return JsonSerializer.Deserialize<NetworkRecord[]>(jsonString, options) ?? Array.Empty<NetworkRecord>();
        }

        private static IReadOnlyList<NetworkRecord> ImportCsv(string filePath)
        {
            var records = new List<NetworkRecord>();
            var lines = File.ReadAllLines(filePath);

            // Начинаем с 1, чтобы пропустить строку заголовков "TimeStamp;DestIp;DestPort;Domain"
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var parts = lines[i].Split(';');
                if (parts.Length >= 4 &&
                    DateTime.TryParse(parts[0], out var timeStamp) &&
                    int.TryParse(parts[2], out var port))
                {
                    records.Add(new NetworkRecord(timeStamp, parts[1], port, parts[3]));
                }
            }

            return records; // List безопасно приводится к IReadOnlyList
        }
    }
}
